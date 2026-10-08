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
    private int? _queuedPage;
    private string _pageInput = "1";
    private string _searchText = "", _appliedSearch = "";
    private bool _favoritesOnly;
    private string _assetTypeFilter = "All", _currencyFilter = "", _countryFilter = "", _exchangeFilter = "";
    private int _filterVersion;
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
        FirstPageCommand = new RelayCommand(_ => FirstPage(), _ => !_disposed && !IsBusy && _currentPage > 1);
        LastPageCommand = new RelayCommand(_ => LastPage(), _ => !_disposed && !IsBusy && _currentPage < PageCount);
        GoToPageCommand = new RelayCommand(GoToPage);
        RefreshCommand = new RelayCommand(parameter => { _ = RefreshAsync(); }, _ => !_disposed && !IsBusy);
        SearchCommand = new RelayCommand(_ => { _ = SearchAsync(); }, _ => !_disposed);
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
    public RelayCommand FirstPageCommand { get; }
    public RelayCommand LastPageCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand GoToPageCommand { get; }
    public RelayCommand SearchCommand { get; }
    public IReadOnlyList<string> AssetTypeOptions { get; } = new[] { "All", "Stocks", "Crypto", "Forex", "Futures" };
    public string AssetTypeFilter
    {
        get => _assetTypeFilter;
        set
        {
            if (value is null || !AssetTypeOptions.Contains(value) || !SetField(ref _assetTypeFilter, value)) return;
            OnPropertyChanged(nameof(ShowSectorFilter));
            FiltersChanged();
        }
    }
    public string CurrencyFilter { get => _currencyFilter; set { if (SetField(ref _currencyFilter, value ?? "")) FiltersChanged(false); } }
    public string CountryFilter { get => _countryFilter; set { if (SetField(ref _countryFilter, value ?? "")) FiltersChanged(false); } }
    public string ExchangeFilter { get => _exchangeFilter; set { if (SetField(ref _exchangeFilter, value ?? "")) FiltersChanged(false); } }

    public bool ShowSectorFilter => AssetTypeFilter == "Stocks";
    public sealed class NicheChoice
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }
    private sealed class NicheCatalog { public List<NicheChoice> Items { get; set; } = new(); }
    private List<NicheChoice> _niches = new();
    private string _nicheSearch = "", _nicheError = "";
    private NicheChoice? _selectedNiche;
    private bool _loadingNiches, _nichesLoaded, _updatingNicheOptions;
    public ObservableCollection<NicheChoice> NicheOptions { get; } = new();
    public bool HasNicheError => NicheError.Length > 0;
    public string NicheError
    {
        get => _nicheError;
        private set { if (SetField(ref _nicheError, value)) OnPropertyChanged(nameof(HasNicheError)); }
    }
    public NicheChoice? SelectedNiche
    {
        get => _selectedNiche;
        set
        {
            int previousId = _selectedNiche?.Id ?? 0;
            if (SetField(ref _selectedNiche, value) && !_updatingNicheOptions &&
                previousId != (value?.Id ?? 0)) FiltersChanged();
        }
    }
    public string NicheSearch
    {
        get => _nicheSearch;
        set
        {
            if (!SetField(ref _nicheSearch, value ?? "")) return;
            UpdateNicheOptions();
        }
    }
    private void UpdateNicheOptions()
    {
        // Cette saisie réduit les choix du menu ; sélectionner une niche filtre les actifs.
        var selected = SelectedNiche;
        _updatingNicheOptions = true;
        NicheOptions.Clear();
        NicheOptions.Add(new NicheChoice { Name = "All niches" });
        foreach (var niche in _niches.Where(n => n == selected ||
                     n.Name.Contains(NicheSearch.Trim(), StringComparison.OrdinalIgnoreCase)))
            NicheOptions.Add(niche);
        SelectedNiche = selected is not null && selected.Id > 0 ? selected : NicheOptions[0];
        _updatingNicheOptions = false;
    }
    public async Task LoadNichesAsync()
    {
        if (_loadingNiches || _nichesLoaded || _disposed) return;
        _loadingNiches = true;
        NicheError = "";
        try
        {
            var catalog = await GetAsync<NicheCatalog>("api/niches");
            if (_disposed) return;
            _niches = catalog.Items.OrderBy(n => n.Name).ToList();
            _nichesLoaded = true;
            UpdateNicheOptions();
        }
        catch (Exception ex) when (IsRequestError(ex))
        {
            if (!_disposed) NicheError = "Unable to load niches. Press Refresh to retry.";
        }
        finally { _loadingNiches = false; }
    }

    private void FiltersChanged(bool immediate = true)
    {
        if (_disposed) return;
        // Invalider aussi une ancienne réponse qui arrive après un changement de filtre.
        _filterVersion++;
        Assets.Clear();
        _catalogLoaded = false;
        Error = "";
        _searchTimer.Stop();
        if (immediate) _ = SearchAsync();
        else _searchTimer.Start();
    }

    private string BuildFilterQuery()
    {
        string type = AssetTypeFilter switch { "Stocks" => "stock", "Crypto" => "crypto", "Forex" => "forex", "Futures" => "future", _ => "" };
        var filters = new Dictionary<string, string>
        {
            ["asset_type"] = type,
            ["currency"] = CurrencyFilter.Trim().ToUpperInvariant(),
            ["country"] = CountryFilter.Trim(),
            ["exchange"] = ExchangeFilter.Trim().ToUpperInvariant(),
            // Une niche masquée ne doit jamais filtrer les cryptos ou le forex.
            ["niche_id"] = ShowSectorFilter && SelectedNiche?.Id > 0 ? SelectedNiche.Id.ToString() : ""
        };
        // Le serveur combine ces critères AVANT de découper les résultats en pages.
        return string.Concat(filters.Where(f => f.Value.Length > 0)
            .Select(f => "&" + f.Key + "=" + Uri.EscapeDataString(f.Value)))
            + (FavoritesOnly ? "&favorites_only=true" : "");
    }
    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (_disposed || !SetField(ref _favoritesOnly, value)) return;
            FiltersChanged();
        }
    }
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
            ToggleFavoriteCommand.RaiseCanExecuteChanged();
            PreviousPageCommand.RaiseCanExecuteChanged();
            NextPageCommand.RaiseCanExecuteChanged();
            FirstPageCommand.RaiseCanExecuteChanged();
            LastPageCommand.RaiseCanExecuteChanged();
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

    private void FirstPage()
    {
        if (_disposed || _currentPage <= 1) return;
        _currentPage = 1;
        _ = RefreshAsync(isPaging: true);
    }

    private void LastPage()
    {
        if (_disposed || _currentPage >= PageCount) return;
        _currentPage = PageCount;
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
        for (var index = 0; index < _allAssets.Count; index++)
        {
            _allAssets[index].IsAlternateRow = index % 2 == 1;
            Assets.Add(_allAssets[index]);
        }
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
        if (IsBusy) { _queuedPage = page; return; }
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
        int requestedFilters = _filterVersion;
        string filterQuery = BuildFilterQuery();
        _ = LoadNichesAsync();
        try
        {
            if (!await LoadAssetsAsync(_currentPage, requestedSearch, filterQuery, requestedFilters)) return;
            if (_disposed) return;
            _ = LoadFavoritesAsync(); // Favorites must not keep the list or pager locked.
            if (!_disposed)
                Updated = $"Last loaded: {DateTime.Now:HH:mm:ss} · Auto-refresh: 60 s";
        }
        catch (Exception ex) when (IsRequestError(ex))
        {
            if (SearchText.Trim() == requestedSearch && requestedFilters == _filterVersion) AddError(ex);
        }
        finally
        {
            if (!_disposed)
            {
                IsBusy = false;
                IsPaging = false;
                if (_queuedPage is int queuedPage)
                {
                    _queuedPage = null;
                    if (queuedPage >= 1 && queuedPage <= PageCount && queuedPage != _currentPage)
                    {
                        _currentPage = queuedPage;
                        _ = RefreshAsync(isPaging: true);
                    }
                }
                ResumePendingSearch();
            }
        }
    }

    // 1. Demander au backend la page de résultats, par symbole OU nom.
    private async Task<bool> LoadAssetsAsync(int page, string requestedSearch, string filterQuery, int requestedFilters)
    {
        string route = $"api/assets?page={page}&page_size={PageSize}";
        if (requestedSearch.Length > 0) route += "&search=" + Uri.EscapeDataString(requestedSearch);
        route += filterQuery;
        var catalog = await GetAsync<AssetResponse>(route);
        // La saisie a changé pendant l'appel : ne pas afficher cette ancienne réponse.
        if (_disposed || SearchText.Trim() != requestedSearch || requestedFilters != _filterVersion) return false;
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
        int pageSize = catalog.PageSize > 0 ? catalog.PageSize : PageSize;
        int computedPages = Math.Max(1, (int)Math.Ceiling(catalog.Count / (double)pageSize));
        // If count is only the size of the current page, a full page means more may follow.
        if (catalog.TotalPages <= 0 && catalog.Items.Count >= pageSize && computedPages <= _currentPage)
            computedPages = _currentPage + 1;
        _pageCount = catalog.TotalPages > 0 ? catalog.TotalPages : computedPages;
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
            var loadedAssets = _allAssets;
            var favorites = await GetAsync<FavoriteResponse>("api/favorites");
            if (favorites.AssetIds is null) throw new JsonException();
            if (_disposed || !ReferenceEquals(loadedAssets, _allAssets)) return;

            // Un HashSet permet de retrouver rapidement un identifiant.
            var favoriteIds = favorites.AssetIds.ToHashSet();
            foreach (var asset in loadedAssets)
                asset.IsFavorite = favoriteIds.Contains(asset.Id);
            _favoritesLoaded = true;
            ToggleFavoriteCommand.RaiseCanExecuteChanged();
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
        bool refreshFilteredFavorites = false;
        IsBusy = true;
        Error = "";
        try
        {
            bool add = !asset.IsFavorite;
            var method = add ? HttpMethod.Put : HttpMethod.Delete;
            using var request = new HttpRequestMessage(method, $"api/favorites/{asset.Id}");
            using var response = await _http.SendAsync(request, _lifetime.Token);
            CheckResponse(response);
            if (!_disposed)
            {
                asset.IsFavorite = add;
                refreshFilteredFavorites = FavoritesOnly;
                if (refreshFilteredFavorites) _currentPage = 1;
            }
        }
        catch (Exception ex) when (IsRequestError(ex)) { AddError(ex); }
        finally
        {
            if (!_disposed)
            {
                IsBusy = false;
                if (refreshFilteredFavorites) _ = RefreshAsync(isPaging: true);
                else ResumePendingSearch();
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
