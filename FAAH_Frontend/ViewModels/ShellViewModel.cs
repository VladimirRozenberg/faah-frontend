using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Threading;
using FAAH_Frontend.Models;
using FAAH_Frontend.Services;
using FAAH_Frontend.Views;

namespace FAAH_Frontend.ViewModels;

/// <summary>
/// Fenetre unique : la barre de navigation reste affichee et
/// CurrentPage change selon l'ecran demande.
/// </summary>
public class ShellViewModel : ViewModelBase
{
    // Adresse HTTPS du serveur qui heberge le backend FastAPI.
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri((Environment.GetEnvironmentVariable("FAAH_API_URL") ?? "https://footballhero.ch").TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    // internal (pas private) : UserListViewModel s'en sert pour appeler /admin/utilisateurs
    // avec la meme connexion, deja authentifiee.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal HttpClient Http => _http;
    private readonly AssetLogoService _logos;
    private readonly DispatcherTimer _healthTimer = new() { Interval = TimeSpan.FromSeconds(60) };

    // Reponse de POST /auth/login : { "token": "...", "message": "..." }
    private class LoginResponse
    {
        public string Token { get; set; } = "";
        public string Message { get; set; } = "";
    }

    // Reponse de GET /auth/me : { "user_id": 1, "username": "...", "role": "..." }
    // Informations du compte connecte, affichees dans le panneau personnel.
    private class CurrentUserResponse
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public int? UserId { get; set; }
        public string? Email { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    private object? _currentPage;
    private bool _isLoggedIn;
    private string _section = "PORTFOLIO";
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string? _errorMessage;
    private string _userName = string.Empty;
    private string _initials = string.Empty;
    private bool _isAdmin;
    private string _profileEmail = "Not provided";
    private string _profileRole = "Unavailable";
    private string _profileUserId = "Unavailable";
    private string _profileRegistrationDate = "Not available";

    public ObservableCollection<Portfolio> Portfolios { get; } = new()
    {
        new Portfolio
        {
            Name = "Momentum Alpha", Description = "Short-term futures momentum",
            Risk = RiskLevel.High, MaxPositions = 8, ReturnPercent = 18.4m,
            Status = PortfolioStatus.Active
        },
        new Portfolio
        {
            Name = "Risky Takes", Description = "High-leverage speculative play",
            Risk = RiskLevel.VeryHigh, MaxPositions = 12, ReturnPercent = -4.2m,
            Status = PortfolioStatus.Active
        },
        new Portfolio
        {
            Name = "Conservative Investment", Description = "Blue-chip long-only, capital preservation",
            Risk = RiskLevel.Low, MaxPositions = 4, ReturnPercent = 6.1m,
            Status = PortfolioStatus.Active
        },
        new Portfolio
        {
            Name = "Arb Core", Description = "Cross-exchange arbitrage sleeve",
            Risk = RiskLevel.Medium, MaxPositions = 15, ReturnPercent = 9.7m,
            Status = PortfolioStatus.Active
        },
        new Portfolio
        {
            Name = "Legacy Swing", Description = "Old swing strategy, paused",
            Risk = RiskLevel.Medium, MaxPositions = 5, ReturnPercent = 1.3m,
            Status = PortfolioStatus.Paused
        }
    };

    public string ProfileEmail { get => _profileEmail; private set => SetField(ref _profileEmail, value); }
    public string ProfileRole { get => _profileRole; private set { if (SetField(ref _profileRole, value)) OnPropertyChanged(nameof(ProfileRoleDisplay)); } }
    public string ProfileUserId { get => _profileUserId; private set => SetField(ref _profileUserId, value); }
    public string ProfileRegistrationDate { get => _profileRegistrationDate; private set => SetField(ref _profileRegistrationDate, value); }
    public string ProfileRoleDisplay => ProfileRole == "admin" ? "Administrator" : ProfileRole == "employe" ? "Employee" : "Not available";
    public HealthDetailsViewModel Health { get; }

    public ShellViewModel()
    {
        _logos = new AssetLogoService(_http);
        Health = new HealthDetailsViewModel(_http);
        ShowHealthCommand = new RelayCommand(ShowHealthDetails);
        ShowDashboardCommand = new RelayCommand(ShowDashboard);
        LoginCommand = new RelayCommand(Login);
        LogoutCommand = new RelayCommand(Logout);
        ShowPortfoliosCommand = new RelayCommand(ShowPortfolios);
        ShowAssetsCommand = new RelayCommand(ShowAssets);
        ShowNewsCommand = new RelayCommand(ShowNews);
        ShowUsersCommand = new RelayCommand(ShowUsers);
        ShowProfileCommand = new RelayCommand(ShowProfile);
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        _healthTimer.Tick += (_, _) => _ = Health.RefreshAsync();

        ShowLogin();
    }

    // ---------- etat ----------

    public object? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage is Avalonia.Controls.Control old && old.DataContext is IDisposable page) page.Dispose();
            SetField(ref _currentPage, value);
        }
    }

    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        private set => SetField(ref _isLoggedIn, value);
    }

    // Rempli par Login() avec le nom que tu as tape pour te connecter (remplace le "Vladimir" en dur).
    public string UserName
    {
        get => _userName;
        private set => SetField(ref _userName, value);
    }

    public string Initials
    {
        get => _initials;
        private set => SetField(ref _initials, value);
    }

    // Vrai seulement si le compte connecte a le role "admin" cote backend.
    // Controle l'affichage de "Admin Console" dans le menu (MainWindow.axaml).
    public bool IsAdmin
    {
        get => _isAdmin;
        private set => SetField(ref _isAdmin, value);
    }

    /// <summary>Onglet mis en avant dans la barre de navigation.</summary>
    public string Section
    {
        get => _section;
        private set
        {
            if (!SetField(ref _section, value)) return;
            OnPropertyChanged(nameof(IsPortfolioActive));
            OnPropertyChanged(nameof(IsDashboardActive));
            OnPropertyChanged(nameof(IsAssetsActive));
            OnPropertyChanged(nameof(IsNewsActive));
        }
    }

    public bool IsDashboardActive => Section == "DASHBOARD";
    public bool IsPortfolioActive => Section == "PORTFOLIO";
    public bool IsAssetsActive => Section == "ASSETS";
    public bool IsNewsActive => Section == "NEWS";

    // ---------- champs de connexion ----------

    public string Username { get => _username; set => SetField(ref _username, value); }
    public string Password { get => _password; set => SetField(ref _password, value); }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    // ---------- commandes ----------

    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand ShowPortfoliosCommand { get; }
    public ICommand ShowDashboardCommand { get; }
    public ICommand ShowAssetsCommand { get; }
    public ICommand ShowNewsCommand { get; }
    public ICommand ShowUsersCommand { get; }
    public ICommand ShowProfileCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ShowHealthCommand { get; }

    // ---------- navigation ----------

    public void ShowLogin()
    {
        IsLoggedIn = false;
        CurrentPage = new LoginView { DataContext = this };
    }

    public void ShowDashboard()
    {
        Section = "DASHBOARD";
        CurrentPage = new DashboardView { DataContext = new DashboardViewModel(this) };
    }

    public void ShowPortfolios()
    {
        Section = "PORTFOLIO";
        CurrentPage = new PortfolioListView { DataContext = new PortfolioListViewModel(this) };
    }

    public void ShowProfile()
    {
        if (!int.TryParse(ProfileUserId, out var userId))
            return;

        Section = "PROFILE";
        CurrentPage = new PersonalInformationView
        {
            DataContext = new PersonalInformationViewModel(new User
            {
                UserId = userId,
                Username = UserName,
                Email = ProfileEmail,
                Role = ProfileRole,
                IsActive = true
            }, this)
        };
    }

    public void ShowSettings()
    {
        Section = "SETTINGS";
        CurrentPage = new SettingsView { DataContext = this };
    }

    public void ShowPortfolio(Portfolio portfolio)
    {
        Section = "PORTFOLIO";
        CurrentPage = new PortfolioView { DataContext = portfolio };
    }

    public void ShowPortfolioCreate()
    {
        Section = "PORTFOLIO";
        CurrentPage = new PortfolioCreateView
        {
            DataContext = new PortfolioCreateViewModel(this)
        };
    }

    public void ShowAssets()
    {
        Section = "ASSETS";
        var assets = new AssetListViewModel(_http, ShowAssetDetail, _logos);
        CurrentPage = new AssetListView { DataContext = assets };
        assets.Start();
    }

    // Le detail remplace la liste dans la fenetre existante (pas de nouvelle fenetre).
    public void ShowAssetDetail(Asset asset)
    {
        // Réutiliser le cache si le détail est ouvert avant la fin du téléchargement.
        _ = _logos.LoadAsync(asset, System.Threading.CancellationToken.None);
        Section = "ASSETS";
        int.TryParse(ProfileUserId, out int userId);
        var detail = new AssetDetailViewModel(_http, asset, ShowAssets, userId, ShowNewsDetail);
        CurrentPage = new AssetDetailView { DataContext = detail };
        detail.Start();
    }

    public void ShowNews()
    {
        Section = "NEWS";
        var news = new NewsListViewModel(_http, ShowNewsDetail);
        CurrentPage = new NewsListView { DataContext = news };
        news.Start();
    }

    public void ShowNewsDetail(int articleId)
    {
        Section = "NEWS";
        var detail = new NewsDetailViewModel(_http, articleId, ShowNews);
        CurrentPage = new NewsDetailView { DataContext = detail };
        detail.Start();
    }

    public void ShowUsers()
    {
        var viewModel = new UserListViewModel(this);
        CurrentPage = new UserListView { DataContext = viewModel };

        // Charge en arriere-plan : l'ecran s'affiche tout de suite,
        // les lignes apparaissent des que l'API repond.
        _ = viewModel.ChargerUtilisateursAsync();
    }

    public void ShowUserInformation(User user)
    {
        CurrentPage = new PersonalInformationView
        {
            DataContext = new PersonalInformationViewModel(user, this)
        };
    }

    public void ShowUserCreate()
    {
        CurrentPage = new UserCreateView { DataContext = new UserCreateViewModel(this) };
    }

    // ---------- actions ----------

    private async void Login()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Enter a username and a password to continue.";
            return;
        }

        try
        {
            ErrorMessage = null;
            IsAdmin = false;
            ProfileEmail = "Not provided";
            ProfileRole = "Unavailable";
            ProfileUserId = "Unavailable";
            ProfileRegistrationDate = "Not available";

            var loginResponse = await _http.PostAsJsonAsync(
                "auth/login",
                new { username = Username, password = Password },
                JsonOptions);

            if (!loginResponse.IsSuccessStatusCode)
            {
                ErrorMessage = "Nom d'utilisateur (ou email) ou mot de passe incorrect.";
                return;
            }

            var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            if (login is null || string.IsNullOrWhiteSpace(login.Token))
            {
                ErrorMessage = "L'API n'a pas renvoye de token.";
                return;
            }

            // Le token sert pour tous les appels suivants.
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", login.Token);

            // Le nom affiche est celui que tu as tape pour te connecter.
            UserName = Username;
            Initials = Username.Length >= 2 ? Username[..2].ToUpper() : Username.ToUpper();

            // Recupere le role (admin ou non) pour savoir si "Admin Console" doit apparaitre.
            // En best-effort : si ca echoue, on reste connecte mais sans le menu admin.
            try
            {
                var meResponse = await _http.GetAsync("auth/me");
                if (meResponse.IsSuccessStatusCode)
                {
                    var moi = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(JsonOptions);
                    IsAdmin = moi?.Role == "admin";
                    if (moi is not null)
                    {
                        if (!string.IsNullOrWhiteSpace(moi.Username)) UserName = moi.Username;
                        Initials = UserName.Length >= 2 ? UserName[..2].ToUpperInvariant() : UserName.ToUpperInvariant();
                        ProfileEmail = string.IsNullOrWhiteSpace(moi.Email) ? "Not provided" : moi.Email;
                        ProfileRole = string.IsNullOrWhiteSpace(moi.Role) ? "Unavailable" : moi.Role;
                        ProfileUserId = moi.UserId?.ToString() ?? "Unavailable";
                        ProfileRegistrationDate = moi.CreatedAt?.ToString("dd MMMM yyyy, HH:mm") ?? "Not available";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERREUR /auth/me (role) : {ex}");
                IsAdmin = false;
            }

            Password = string.Empty;
            IsLoggedIn = true;
            _healthTimer.Start();
            _ = Health.RefreshAsync();
            ShowPortfolios();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERREUR LOGIN : {ex}");
            ErrorMessage = "Impossible de contacter le serveur FAAH. Verifie qu'il est demarre.";
        }
    }

    private void Logout()
    {
        Username = string.Empty;
        Password = string.Empty;
        UserName = string.Empty;
        Initials = string.Empty;
        ProfileEmail = "Not provided";
        ProfileRole = "Unavailable";
        ProfileUserId = "Unavailable";
        ProfileRegistrationDate = "Not available";
        IsAdmin = false;
        ErrorMessage = null;
        Section = "PORTFOLIO";

        _http.DefaultRequestHeaders.Authorization = null;
        _healthTimer.Stop();

        ShowLogin();
    }

    private void ShowHealthDetails()
    {
        var window = new HealthDetailsWindow
        {
            DataContext = Health
        };
        window.Show();
    }
}
