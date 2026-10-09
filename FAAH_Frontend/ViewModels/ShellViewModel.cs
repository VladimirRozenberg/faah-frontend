using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly SessionHandler _sessionHandler = new(new HttpClientHandler());

    // Detecte un token expire/invalide (401) sur les appels authentifies.
    private sealed class SessionHandler : DelegatingHandler
    {
        public Action? OnUnauthorized { get; set; }

        public SessionHandler(HttpMessageHandler inner) : base(inner) { }

        protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                && request.Headers.Authorization is not null)
                OnUnauthorized?.Invoke();
            return response;
        }
    }

    private readonly HttpClient _http;

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri((Environment.GetEnvironmentVariable("FAAH_API_URL") ?? "https://footballhero.ch").TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly DispatcherTimer _expiryTimer = new();
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? _expiresAt;
    private string _sessionTimeLeft = string.Empty;

    public string SessionTimeLeft { get => _sessionTimeLeft; private set => SetField(ref _sessionTimeLeft, value); }

    private void UpdateCountdown()
    {
        if (_expiresAt is null) { SessionTimeLeft = string.Empty; return; }
        var left = _expiresAt.Value - DateTimeOffset.UtcNow;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        SessionTimeLeft = left.TotalHours >= 1
            ? $"Session {(int)left.TotalHours}h {left.Minutes:00}m"
            : $"Session {left.Minutes}:{left.Seconds:00}";
    }

    // Planifie la deconnexion a l'instant d'expiration (claim "exp" du JWT).
    private void ScheduleExpiry(string token)
    {
        _expiryTimer.Stop();
        _countdownTimer.Stop();
        _expiresAt = null;
        UpdateCountdown();
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!doc.RootElement.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var seconds)) return;

            var delay = DateTimeOffset.FromUnixTimeSeconds(seconds) - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.FromMilliseconds(100)) delay = TimeSpan.FromMilliseconds(100);
            _expiryTimer.Interval = delay;
            _expiryTimer.Start();
            _expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            UpdateCountdown();
            _countdownTimer.Start();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"Token exp illisible : {ex.Message}");
        }
    }

    private void HandleSessionExpired()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsLoggedIn) return;
            Logout();
            ErrorMessage = "Session expired. Please sign in again.";
        });
    }

    // internal (pas private) : UserListViewModel s'en sert pour appeler /admin/utilisateurs
    // avec la meme connexion, deja authentifiee.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal HttpClient Http => _http;
    private readonly AssetLogoService _logos;
    private readonly DispatcherTimer _healthTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly Dictionary<string, Avalonia.Controls.Control> _sectionPages = new(StringComparer.Ordinal);

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

    public ObservableCollection<Portfolio> Portfolios { get; } = new();

    public string ProfileEmail { get => _profileEmail; private set => SetField(ref _profileEmail, value); }
    public string ProfileRole { get => _profileRole; private set { if (SetField(ref _profileRole, value)) OnPropertyChanged(nameof(ProfileRoleDisplay)); } }
    public string ProfileUserId { get => _profileUserId; private set => SetField(ref _profileUserId, value); }
    public string ProfileRegistrationDate { get => _profileRegistrationDate; private set => SetField(ref _profileRegistrationDate, value); }
    public string ProfileRoleDisplay => ProfileRole == "admin" ? "Administrator" : ProfileRole == "employe" ? "Employee" : "Not available";
    public HealthDetailsViewModel Health { get; }

    public ShellViewModel() : this(null) { }

    // Permet aux tests de remplacer l'API par des réponses locales, sans toucher au cloud.
    public ShellViewModel(HttpClient? http)
    {
        _http = http ?? CreateClient(_sessionHandler);
        _sessionHandler.OnUnauthorized = HandleSessionExpired;
        _expiryTimer.Tick += (_, _) => { _expiryTimer.Stop(); HandleSessionExpired(); };
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        _logos = new AssetLogoService(_http);
        Health = new HealthDetailsViewModel(_http);
        ShowHealthCommand = new RelayCommand(ShowHealthDetails);
        ShowDashboardCommand = new RelayCommand(ShowDashboard);
        LoginCommand = new RelayCommand(Login);
        LogoutCommand = new RelayCommand(Logout);
        ShowPortfoliosCommand = new RelayCommand(ShowPortfolios);
        ShowAssetsCommand = new RelayCommand(ShowAssets);
        ShowNewsCommand = new RelayCommand(ShowNews);
        ShowOrchestratorCommand = new RelayCommand(ShowOrchestrator);
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
            if (_currentPage is Avalonia.Controls.Control old && !_sectionPages.Values.Contains(old)
                && old.DataContext is IDisposable page) page.Dispose();
            SetField(ref _currentPage, value);
        }
    }

    public async System.Threading.Tasks.Task UpdatePortfolioActiveStateAsync(Portfolio portfolio, bool isActive, bool previousState)
    {
        if (portfolio.IsStatusUpdating) return;

        portfolio.IsStatusUpdating = true;
        portfolio.StatusErrorMessage = null;
        try
        {
            using var response = await _http.PatchAsJsonAsync(
                $"api/users/me/portfolios/{portfolio.Id}", new { is_active = isActive }, JsonOptions);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Portfolio status update failed (HTTP {(int)response.StatusCode}).");

            var updated = await response.Content.ReadFromJsonAsync<PortfolioUpdateResponse>(JsonOptions)
                ?? throw new JsonException("The server returned an empty portfolio response.");
            ApplyPortfolioUpdate(portfolio, updated);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PORTFOLIO STATUS UPDATE ERROR: {ex}");
            portfolio.IsActive = previousState;
            portfolio.StatusErrorMessage = ex is HttpRequestException httpException && httpException.Message.Contains("HTTP ", StringComparison.Ordinal)
                ? httpException.Message
                : "Unable to update portfolio status.";
        }
        finally
        {
            portfolio.IsStatusUpdating = false;
        }
    }

    internal void ApplyPortfolioUpdate(Portfolio portfolio, PortfolioUpdateResponse updated)
    {
        portfolio.Name = updated.Name;
        portfolio.Description = updated.Description;
        portfolio.StrategyType = updated.StrategyType;
        if (updated.PreferredAssetTypes is not null)
            portfolio.PreferredAssetTypes = new List<string>(updated.PreferredAssetTypes);
        if (updated.PreferredNicheIds is not null)
            portfolio.PreferredNicheIds = new List<int>(updated.PreferredNicheIds);
        portfolio.RiskTolerance = updated.RiskTolerance;
        portfolio.MaxPositionSizePct = updated.MaxPositionSizePct;
        portfolio.MaxPositions = updated.MaxOpenPositions;
        portfolio.BaseCurrency = updated.BaseCurrency;
        portfolio.IsActive = updated.IsActive;
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
            OnPropertyChanged(nameof(IsOrchestratorActive));
        }
    }

    public bool IsDashboardActive => Section == "DASHBOARD";
    public bool IsPortfolioActive => Section == "PORTFOLIO";
    public bool IsAssetsActive => Section == "ASSETS";
    public bool IsNewsActive => Section == "NEWS";
    public bool IsOrchestratorActive => Section == "ORCHESTRATOR";

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
    public ICommand ShowOrchestratorCommand { get; }
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
        CurrentPage = GetOrCreateSectionPage("DASHBOARD", () => new DashboardView { DataContext = new DashboardViewModel(this) });
    }

    public void ShowPortfolios()
    {
        Section = "PORTFOLIO";
        CurrentPage = GetOrCreateSectionPage("PORTFOLIO", () =>
        {
            _ = LoadPortfoliosAsync();
            return new PortfolioListView { DataContext = new PortfolioListViewModel(this) };
        });
    }

    public void ShowOrchestrator()
    {
        if (!IsAdmin) return;

        Section = "ORCHESTRATOR";
        CurrentPage = GetOrCreateSectionPage("ORCHESTRATOR", () =>
        {
            var history = new OrchestratorDecisionHistoryViewModel(_http);
            var page = new OrchestratorDecisionView { DataContext = history };
            history.Start();
            return page;
        });
    }

    private Avalonia.Controls.Control GetOrCreateSectionPage(string section, Func<Avalonia.Controls.Control> create)
    {
        if (_sectionPages.TryGetValue(section, out var page)) return page;
        page = create();
        _sectionPages.Add(section, page);
        return page;
    }

    private sealed class PortfolioListResponse
    {
        public int Count { get; set; }
        public List<Portfolio> Items { get; set; } = new();
    }

    private async System.Threading.Tasks.Task LoadPortfoliosAsync()
    {
        try
        {
            var page = await _http.GetFromJsonAsync<PortfolioListResponse>("api/users/me/portfolios", JsonOptions);
            Portfolios.Clear();
            foreach (var p in page?.Items ?? new()) Portfolios.Add(p);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"ERREUR /portfolios : {ex}");
        }
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

    public void ShowPortfolioEdit(Portfolio portfolio)
    {
        Section = "PORTFOLIO";
        CurrentPage = new PortfolioCreateView
        {
            DataContext = new PortfolioCreateViewModel(this, portfolio)
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
        CurrentPage = new PortfolioView { DataContext = new PortfolioDetailViewModel(this, portfolio) };
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
        CurrentPage = GetOrCreateSectionPage("ASSETS", () =>
        {
            var assets = new AssetListViewModel(_http, ShowAssetDetail, _logos);
            var page = new AssetListView { DataContext = assets };
            assets.Start();
            return page;
        });
    }

    // Le detail remplace la liste dans la fenetre existante (pas de nouvelle fenetre).
    public void ShowAssetDetail(Asset asset) => ShowAssetDetail(asset, null);

    public void ShowAssetDetail(Asset asset, Portfolio? preferredPortfolio)
    {
        // Réutiliser le cache si le détail est ouvert avant la fin du téléchargement.
        _ = _logos.LoadAsync(asset, System.Threading.CancellationToken.None);
        Section = "ASSETS";
        int.TryParse(ProfileUserId, out int userId);
        Action goBack = preferredPortfolio is null ? ShowAssets : () => ShowPortfolio(preferredPortfolio);
        var detail = new AssetDetailViewModel(_http, asset, goBack, userId, ShowNewsDetail, preferredPortfolio?.Id, _logos);
        CurrentPage = new AssetDetailView { DataContext = detail };
        detail.Start();
    }

    public void ShowNews()
    {
        Section = "NEWS";
        CurrentPage = GetOrCreateSectionPage("NEWS", () =>
        {
            var news = new NewsListViewModel(_http, ShowNewsDetail, ShowAssetFromNews);
            var page = new NewsListView { DataContext = news };
            news.Start();
            return page;
        });
    }

    private void ShowAssetFromNews(string symbol) => ShowAssetDetail(new Asset
    {
        Id = 0,
        Symbol = symbol,
        LogoUrl = $"/api/assets/{Uri.EscapeDataString(symbol)}/logo"
    });

    public void ShowNewsDetail(int articleId)
    {
        Section = "NEWS";
        var detail = new NewsDetailViewModel(_http, articleId, ShowNews, ShowAssetFromNews);
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
                ErrorMessage = "Incorrect username/email or password.";
                return;
            }

            var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            if (login is null || string.IsNullOrWhiteSpace(login.Token))
            {
                ErrorMessage = "The server did not return an authentication token.";
                return;
            }

            // Le token sert pour tous les appels suivants.
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", login.Token);
            ScheduleExpiry(login.Token);

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
            ShowDashboard();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Login error: {ex}");
            ErrorMessage = "Unable to contact the FAAH server. Check that it is running.";
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
        _expiryTimer.Stop();
        _countdownTimer.Stop();
        _expiresAt = null;
        SessionTimeLeft = string.Empty;
        _healthTimer.Stop();

        ShowLogin();
        ClearSectionPages();
    }

    private void ClearSectionPages()
    {
        foreach (var page in _sectionPages.Values)
            if (page.DataContext is IDisposable viewModel) viewModel.Dispose();
        _sectionPages.Clear();
        Portfolios.Clear();
    }

    private HealthDetailsWindow? _healthWindow;

    private void ShowHealthDetails()
    {
        if (_healthWindow is not null)
        {
            if (_healthWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                _healthWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            _healthWindow.Activate();
            return;
        }
        var window = new HealthDetailsWindow
        {
            DataContext = Health
        };
        window.Closed += (_, _) => _healthWindow = null;
        _healthWindow = window;
        window.Show();
    }
}
