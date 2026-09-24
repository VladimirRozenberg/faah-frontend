using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Input;
using FAAH_Frontend.Models;
using FAAH_Frontend.Views;

namespace FAAH_Frontend.ViewModels;

/// <summary>
/// Fenetre unique : la barre de navigation reste affichee et
/// CurrentPage change selon l'ecran demande.
/// </summary>
public class ShellViewModel : ViewModelBase
{
    // Adresse de la VM Linux qui heberge le backend FastAPI (trouvee avec "hostname -I").
    // Si tu redemarres la VM et que l'adresse change (attribution DHCP), il faudra la remettre a jour ici.
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri((Environment.GetEnvironmentVariable("FAAH_API_URL") ?? "http://192.168.108.131:8001").TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    // internal (pas private) : UserListViewModel s'en sert pour appeler /admin/utilisateurs
    // avec la meme connexion, deja authentifiee.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal HttpClient Http => _http;

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

    public string ProfileEmail { get => _profileEmail; private set => SetField(ref _profileEmail, value); }
    public string ProfileRole { get => _profileRole; private set => SetField(ref _profileRole, value); }
    public string ProfileUserId { get => _profileUserId; private set => SetField(ref _profileUserId, value); }

    public ShellViewModel()
    {
        LoginCommand = new RelayCommand(Login);
        LogoutCommand = new RelayCommand(Logout);
        ShowPortfoliosCommand = new RelayCommand(ShowPortfolios);
        ShowAssetsCommand = new RelayCommand(ShowAssets);
        ShowNewsCommand = new RelayCommand(ShowNews);
        ShowUsersCommand = new RelayCommand(ShowUsers);

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
            OnPropertyChanged(nameof(IsAssetsActive));
            OnPropertyChanged(nameof(IsNewsActive));
        }
    }

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
    public ICommand ShowAssetsCommand { get; }
    public ICommand ShowNewsCommand { get; }
    public ICommand ShowUsersCommand { get; }

    // ---------- navigation ----------

    public void ShowLogin()
    {
        IsLoggedIn = false;
        CurrentPage = new LoginView { DataContext = this };
    }

    public void ShowPortfolios()
    {
        Section = "PORTFOLIO";
        CurrentPage = new PortfolioListView { DataContext = new PortfolioListViewModel(this) };
    }

    public void ShowPortfolio(Portfolio portfolio)
    {
        Section = "PORTFOLIO";
        CurrentPage = new PortfolioView { DataContext = portfolio };
    }

    public void ShowAssets()
    {
        Section = "ASSETS";
        var assets = new AssetListViewModel(_http, ShowAssetDetail);
        CurrentPage = new AssetListView { DataContext = assets };
        assets.Start();
    }

    // Le detail remplace la liste dans la fenetre existante (pas de nouvelle fenetre).
    public void ShowAssetDetail(Asset asset)
    {
        Section = "ASSETS";
        int.TryParse(ProfileUserId, out int userId);
        var detail = new AssetDetailViewModel(_http, asset, ShowAssets, userId);
        CurrentPage = new AssetDetailView { DataContext = detail };
        detail.Start();
    }

    public void ShowNews()
    {
        Section = "NEWS";
        var news = new NewsListViewModel(_http);
        CurrentPage = new NewsListView { DataContext = news };
        news.Start();
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
        IsAdmin = false;
        ErrorMessage = null;
        Section = "PORTFOLIO";

        _http.DefaultRequestHeaders.Authorization = null;

        ShowLogin();
    }
}
