using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using FAAH_Frontend.Models;
using FAAH_Frontend.Services;

namespace FAAH_Frontend.ViewModels;

// Cette classe prépare les données affichées dans la liste des actifs.
public sealed class AssetListViewModel : ViewModelBase, IDisposable
{
    // Le client HTTP fourni par le Shell contient déjà le token de connexion.
    private readonly HttpClient _http;
    private readonly AssetLogoService _logos;
    private readonly Action<Asset>? _openAsset;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _searchPending;

    // Le backend utilise snake_case et peut envoyer les nombres sous forme de texte.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private List<Asset> _allAssets = new();
    private int _currentPage = 1, _pageCount = 1, _totalCount;
    private string _pageInput = "1";
    private string _searchText = "", _appliedSearch = "";
    private bool _isBusy, _isPaging, _disposed, _catalogLoaded, _favoritesLoaded, _loadingPrices;
    private string _error = "", _updated = "Not loaded yet";
    public const int PageSize = 20;

    public AssetListViewModel(HttpClient http, Action<Asset>? openAsset = null, AssetLogoService? logos = null)
    {
        _http = http;
        _logos = logos ?? new AssetLogoService(http);
        _openAsset = openAsset;
        OpenAssetCommand = new RelayCommand(OpenAsset, _ => !_disposed && _openAsset is not null);
        PreviousPageCommand = new RelayCommand(_ => PreviousPage(), _ => !_disposed && !IsBusy && _currentPage > 1);
        NextPageCommand = new RelayCommand(_ => NextPage(), _ => !_disposed && !IsBusy && _currentPage < PageCount);
        GoToPageCommand = new RelayCommand(GoToPage, _ => !_disposed && !IsBusy && TryGetPage(_));
        RefreshCommand = new RelayCommand(parameter => { _ = RefreshAsync(); }, _ => !_disposed && !IsBusy);
        SearchCommand = new RelayCommand(_ => { _ = SearchAsync(); }, _ => !_disposed);
        ClearSearchCommand = new RelayCommand(_ => { _ = SearchAsync(clear: true); }, _ => !_disposed);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite, _ => !_disposed && !IsBusy && _favoritesLoaded);
        _timer.Tick += OnTimerTick;
        _searchTimer.Tick += OnSearchTick;
    }

    public bool IsPaging
    {
        get => _isPaging;
        private set
        {
            if (SetField(ref _isPaging, value)) OnPropertyChanged(nameof(ShowUpdatingAssets));
        }
    }

    public bool ShowUpdatingAssets => IsBusy && !IsPaging;

    // Propriétés et commandes utilisées par les Binding du fichier AXAML.
    // Le backend renvoie uniquement la page demandée, déjà filtrée si nécessaire.
    public ObservableCollection<Asset> Assets { get; } = new();
    public ObservableCollection<int> PageNumbers { get; } = new();
    public RelayCommand OpenAssetCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand GoToPageCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_disposed || !SetField(ref _searchText, value ?? "")) return;
            // Attendre une courte pause de frappe plutôt qu'appeler l'API à chaque touche.
            _searchTimer.Stop();
            _searchTimer.Start();
            // Ne pas laisser les anciens résultats visibles sous une autre recherche.
            Assets.Clear();
            _catalogLoaded = false;
            Error = "";
            OnPropertyChanged(nameof(SearchStatus));
        }
    }
    public string SearchStatus => HasError ? "" : !_catalogLoaded && SearchText.Trim().Length > 0
        ? $"Searching: {SearchText.Trim()}…"
        : _appliedSearch.Length == 0 ? "" : $"Search: {_appliedSearch}";

    // Garder la recherche validée pour la pagination et l'actualisation automatique.
    // Si une requête est en cours, mémoriser la dernière recherche au lieu de la perdre.
    public async Task SearchAsync(bool clear = false)
    {
        if (_disposed) return;
        if (clear) SearchText = "";
        _searchTimer.Stop();
        if (IsBusy) { _searchPending = true; return; }
        _searchPending = false;
        _appliedSearch = (SearchText ?? "").Trim();
        OnPropertyChanged(nameof(SearchStatus));
        _currentPage = 1;
        await RefreshAsync();
    }

    private void OnSearchTick(object? sender, EventArgs e) => _ = SearchAsync();

    private void ResumePendingSearch()
    {
        if (_searchPending && !_disposed) _ = SearchAsync();
    }

    public int PageCount => _pageCount;
    public string PageInput { get => _pageInput; set => SetField(ref _pageInput, value); }
    public string PageLabel => $"Page {_currentPage} of {PageCount} · {_totalCount} assets";
    public bool HasError => Error.Length > 0;
    public bool IsEmpty => _catalogLoaded && !IsBusy && !HasError && Assets.Count == 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            SetField(ref _isBusy, value);
            RefreshCommand.RaiseCanExecuteChanged();
            SearchCommand.RaiseCanExecuteChanged();
            ClearSearchCommand.RaiseCanExecuteChanged();
            ToggleFavoriteCommand.RaiseCanExecuteChanged();
            PreviousPageCommand.RaiseCanExecuteChanged();
            NextPageCommand.RaiseCanExecuteChanged();
            GoToPageCommand.RaiseCanExecuteChanged();
            UpdateDisplayProperties();
        }
    }

    public bool IsLoadingPrices
    {
        get => _loadingPrices;
        private set => SetField(ref _loadingPrices, value);
    }

    public string Error
    {
        get => _error;
        private set
        {
            SetField(ref _error, value);
            UpdateDisplayProperties();
        }
    }

    public string Updated
    {
        get => _updated;
        private set => SetField(ref _updated, value);
    }

    // Navigation vers le détail de l'actif sélectionné.
    private void OpenAsset(object? parameter)
    {
        if (!_disposed && parameter is Asset asset) _openAsset?.Invoke(asset);
    }

    private void PreviousPage()
    {
        if (_disposed || _currentPage <= 1) return;
        _currentPage--;
        _ = RefreshAsync(isPaging: true);
    }

    private void NextPage()
    {
        if (_disposed || _currentPage >= PageCount) return;
        _currentPage++;
        _ = RefreshAsync(isPaging: true);
    }

    private void ShowPage()
    {
        _currentPage = Math.Clamp(_currentPage, 1, PageCount);
        Assets.Clear();
        foreach (var asset in _allAssets) Assets.Add(asset);
        PageInput = _currentPage.ToString();
        UpdatePageNumbers();

        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
            GoToPageCommand.RaiseCanExecuteChanged();
        UpdateDisplayProperties();
        _ = LoadVisibleLogosAsync(); // Ne bloque ni la liste, ni les favoris, ni les cours.
    }

    public Task LoadVisibleLogosAsync()
    {
        if (_disposed) return Task.CompletedTask;
        return Task.WhenAll(Assets.Select(asset => _logos.LoadAsync(asset, _lifetime.Token)));
    }

    private void UpdatePageNumbers()
    {
        const int windowSize = 9;
        int maxStart = Math.Max(1, PageCount - windowSize + 1);
        int start = Math.Clamp(_currentPage - 2, 1, maxStart);
        PageNumbers.Clear();
        for (int page = start; page < start + windowSize && page <= PageCount; page++) PageNumbers.Add(page);
    }

    private void GoToPage(object? parameter)
    {
        if (!TryGetPage(parameter, out var page) || page < 1 || page > PageCount || page == _currentPage) return;
        _currentPage = page;
        _ = RefreshAsync(isPaging: true);
    }

    private static bool TryGetPage(object? parameter, out int page)
    {
        if (parameter is int integer) { page = integer; return true; }
        return int.TryParse(parameter?.ToString(), out page);
    }

    private static bool TryGetPage(object? parameter) => TryGetPage(parameter, out _);

    private void UpdateDisplayProperties()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(SearchStatus));
    }

    // Chargement initial, puis actualisation toutes les 60 secondes.
    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
        _ = RefreshAsync();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_searchTimer.IsEnabled) _ = RefreshAsync();
    }

    // Point de départ : charger le catalogue, puis les favoris et les cours.
    public async Task RefreshAsync(bool isPaging = false)
    {
        if (IsBusy || _disposed) return; // Évite deux actualisations simultanées.
        IsPaging = isPaging;
        IsBusy = true;
        Error = "";
        string requestedSearch = _appliedSearch;
        try
        {
            if (!await LoadAssetsAsync(_currentPage, requestedSearch)) return;
            if (_disposed) return;
            await LoadFavoritesAsync();
            if (_disposed) return;
            if (!_disposed)
                Updated = $"Last loaded: {DateTime.Now:HH:mm:ss} · Auto-refresh: 60 s";
        }
        catch (Exception ex) when (IsRequestError(ex))
        {
            if (SearchText.Trim() == requestedSearch) AddError(ex);
        }
        finally
        {
            if (!_disposed)
            {
                IsBusy = false;
                IsPaging = false;
                ResumePendingSearch();
            }
        }
    }

    // 1. Demander au backend la page de résultats, par symbole OU nom.
    private async Task<bool> LoadAssetsAsync(int page, string requestedSearch)
    {
        string route = $"api/assets?page={page}&page_size={PageSize}";
        if (requestedSearch.Length > 0) route += "&search=" + Uri.EscapeDataString(requestedSearch);
        var catalog = await GetAsync<AssetResponse>(route);
        // La saisie a changé pendant l'appel : ne pas afficher cette ancienne réponse.
        if (_disposed || SearchText.Trim() != requestedSearch) return false;
        if (catalog.Items is null || catalog.Items.Any(a => a is null || string.IsNullOrWhiteSpace(a.Symbol)))
            throw new JsonException();

        // Détecter un ancien backend qui ignore le filtre, au lieu d'afficher toute la liste.
        if (requestedSearch.Length > 0 && catalog.Items.Any(a =>
            !a.Symbol.Contains(requestedSearch, StringComparison.OrdinalIgnoreCase)
            && !(a.Name?.Contains(requestedSearch, StringComparison.OrdinalIgnoreCase) ?? false)))
            throw new InvalidOperationException("The server did not filter the assets. Deploy the backend search update.");

        _allAssets = catalog.Items;
        _currentPage = catalog.Page > 0 ? catalog.Page : page;
        _totalCount = catalog.Count;
        _pageCount = Math.Max(1, (int)Math.Ceiling(catalog.Count / (double)PageSize));
        _catalogLoaded = true;
        _favoritesLoaded = false;
        ShowPage(); // Les cours ne doivent pas retarder l'affichage de la liste.
        OnPropertyChanged(nameof(SearchStatus));
        return true;
    }

    // 2. Retrouver les favoris du compte connecté.
    private async Task LoadFavoritesAsync()
    {
        try
        {
            var favorites = await GetAsync<FavoriteResponse>("api/favorites");
            if (favorites.AssetIds is null) throw new JsonException();
            if (_disposed) return;

            // Un HashSet permet de retrouver rapidement un identifiant.
            var favoriteIds = favorites.AssetIds.ToHashSet();
            foreach (var asset in _allAssets)
                asset.IsFavorite = favoriteIds.Contains(asset.Id);
            _favoritesLoaded = true;
        }
        // Une panne des favoris ne doit pas empêcher le chargement des prix.
        catch (Exception ex) when (IsRequestError(ex)) { AddError(ex, "Favorites"); }
    }

    // L'étoile change seulement après confirmation du backend.
    private void ToggleFavorite(object? parameter)
    {
        if (parameter is Asset asset) _ = ToggleFavoriteAsync(asset);
    }

    public async Task ToggleFavoriteAsync(Asset asset)
    {
        if (IsBusy || _disposed || !_favoritesLoaded || !Assets.Contains(asset)) return;
        IsBusy = true;
        Error = "";
        try
        {
            bool add = !asset.IsFavorite;
            var method = add ? HttpMethod.Put : HttpMethod.Delete;
            using var request = new HttpRequestMessage(method, $"api/favorites/{asset.Id}");
            using var response = await _http.SendAsync(request, _lifetime.Token);
            CheckResponse(response);
            if (!_disposed) asset.IsFavorite = add;
        }
        catch (Exception ex) when (IsRequestError(ex)) { AddError(ex); }
        finally
        {
            if (!_disposed)
            {
                IsBusy = false;
                ResumePendingSearch();
            }
        }
    }

    // Code HTTP commun : envoyer la requête, vérifier le statut, lire le JSON.
    private async Task<T> GetAsync<T>(string path)
    {
        using var response = await _http.GetAsync(path, _lifetime.Token);
        CheckResponse(response);
        return await response.Content.ReadFromJsonAsync<T>(Json, _lifetime.Token)
            ?? throw new JsonException();
    }

    private static void CheckResponse(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Session expired. Please sign in again.",
            HttpStatusCode.Forbidden => "Access denied.",
            HttpStatusCode.NotFound => "Service or asset unavailable. Check that the updated backend is deployed.",
            _ => $"Server error ({(int)response.StatusCode}). Please retry."
        });
    }

    private static bool IsRequestError(Exception ex) =>
        ex is HttpRequestException or InvalidOperationException or JsonException or OperationCanceledException;

    private void AddError(Exception ex, string? section = null)
    {
        if (_disposed) return;
        string message = ex switch
        {
            OperationCanceledException => "Request timed out. Please retry.",
            HttpRequestException => "Cannot reach the server. Please retry.",
            JsonException => "Unexpected server response.",
            _ => ex.Message
        };
        if (section is not null) message = section + ": " + message;
        Error = HasError ? Error + Environment.NewLine + message : message;
    }

    // Quitter la page arrête le timer et annule les requêtes en cours.
    // Ne pas fermer _http : il appartient au Shell et sert aux autres pages.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _searchTimer.Stop();
        _searchTimer.Tick -= OnSearchTick;
        _lifetime.Cancel();
    }
}
