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

namespace FAAH_Frontend.ViewModels;

public sealed class AssetListViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly Action<Asset>? _openAsset;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly HashSet<int> _favoriteIds = new();
    private int _currentPage = 1, _totalCount;
    private bool _isBusy, _disposed, _favoritesLoaded;
    private string _error = "", _updated = "Not loaded yet";

    public const int PageSize = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public AssetListViewModel(HttpClient http, Action<Asset>? openAsset = null)
    {
        _http = http;
        _openAsset = openAsset;
        OpenAssetCommand = new RelayCommand(OpenAsset, _ => !_disposed && _openAsset is not null);
        PreviousPageCommand = new RelayCommand(_ => { _ = LoadPageAsync(_currentPage - 1); }, _ => !_disposed && !IsBusy && _currentPage > 1);
        NextPageCommand = new RelayCommand(_ => { _ = LoadPageAsync(_currentPage + 1); }, _ => !_disposed && !IsBusy && _currentPage < PageCount);
        RefreshCommand = new RelayCommand(_ => { _ = LoadPageAsync(_currentPage, refreshFavorites: true); }, _ => !_disposed && !IsBusy);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite, _ => !_disposed && !IsBusy && _favoritesLoaded);
        _timer.Tick += OnTimerTick;
    }

    public ObservableCollection<Asset> Assets { get; } = new();
    public RelayCommand OpenAssetCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public int CurrentPage => _currentPage;
    public int PageCount => Math.Max(1, (_totalCount + PageSize - 1) / PageSize);
    public string PageLabel => $"Page {CurrentPage} of {PageCount} · {_totalCount} assets";
    public bool HasError => Error.Length > 0;
    public bool IsEmpty => !IsBusy && !HasError && _totalCount == 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            SetField(ref _isBusy, value);
            RefreshCommands();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public string Error
    {
        get => _error;
        private set
        {
            SetField(ref _error, value);
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public string Updated { get => _updated; private set => SetField(ref _updated, value); }

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
        _ = LoadPageAsync(1, refreshFavorites: true);
    }

    public Task RefreshAsync() => LoadPageAsync(_currentPage, refreshFavorites: true);

    private void OnTimerTick(object? sender, EventArgs e) => _ = LoadPageAsync(_currentPage, refreshFavorites: true);

    private async Task LoadPageAsync(int page, bool refreshFavorites = false)
    {
        if (_disposed || IsBusy || page < 1) return;
        IsBusy = true;
        Error = "";

        try
        {
            if (!_favoritesLoaded || refreshFavorites) await LoadFavoritesAsync();

            var catalog = await GetAsync<AssetResponse>($"api/assets?page={page}&page_size={PageSize}");
            if (catalog.Items is null || catalog.Items.Any(asset => asset is null || string.IsNullOrWhiteSpace(asset.Symbol))) throw new JsonException();

            _currentPage = catalog.Page;
            _totalCount = catalog.Count;
            Assets.Clear();

            for (var index = 0; index < catalog.Items.Count; index++)
            {
                var asset = catalog.Items[index];
                asset.IsFavorite = _favoriteIds.Contains(asset.Id);
                asset.IsAlternateRow = index % 2 == 1;
                asset.IsLoadingPrice = true;
                Assets.Add(asset);
            }

            await LoadPricesForVisibleAssetsAsync();
            Updated = $"Last updated: {DateTime.Now:HH:mm:ss} · Refreshes every 15 minutes";
            NotifyPage();
        }
        catch (Exception ex) when (IsRequestError(ex))
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            if (!_disposed) IsBusy = false;
        }
    }

    private async Task LoadFavoritesAsync()
    {
        var favorites = await GetAsync<FavoriteResponse>("api/favorites");
        if (favorites.AssetIds is null) throw new JsonException();
        _favoriteIds.Clear();
        foreach (var id in favorites.AssetIds) _favoriteIds.Add(id);
        _favoritesLoaded = true;
    }

    private async Task LoadPricesForVisibleAssetsAsync()
    {
        var symbols = string.Join(",", Assets.Select(asset => asset.Symbol));
        if (symbols.Length == 0) return;

        try
        {
            var market = await GetAsync<MarketResponse>("api/market?symbols=" + Uri.EscapeDataString(symbols));
            var quotes = market.Items.ToDictionary(quote => quote.Symbol, StringComparer.OrdinalIgnoreCase);

            foreach (var asset in Assets)
            {
                quotes.TryGetValue(asset.Symbol, out var quote);
                asset.Price = quote?.LastPrice;
                asset.ChangePercent = quote?.ChangePercent;
                asset.MarketVolume = quote?.Volume;
                asset.Currency = quote?.Currency ?? asset.Currency;
            }
        }
        finally
        {
            foreach (var asset in Assets)
            {
                asset.IsLoadingPrice = false;
                asset.RefreshQuoteDisplay();
            }
        }
    }

    private void ToggleFavorite(object? parameter)
    {
        if (parameter is Asset asset) _ = ToggleFavoriteAsync(asset);
    }

    public async Task ToggleFavoriteAsync(Asset asset)
    {
        if (_disposed || IsBusy || !_favoritesLoaded) return;
        IsBusy = true;
        Error = "";

        try
        {
            var addFavorite = !asset.IsFavorite;
            using var request = new HttpRequestMessage(addFavorite ? HttpMethod.Put : HttpMethod.Delete, $"api/favorites/{asset.Id}");
            using var response = await _http.SendAsync(request, _lifetime.Token);
            CheckResponse(response);
            if (addFavorite) _favoriteIds.Add(asset.Id); else _favoriteIds.Remove(asset.Id);
        }
        catch (Exception ex) when (IsRequestError(ex))
        {
            Error = FriendlyError(ex);
            return;
        }
        finally
        {
            if (!_disposed) IsBusy = false;
        }

        await LoadPageAsync(1);
    }

    private void OpenAsset(object? parameter)
    {
        if (!_disposed && parameter is Asset asset) _openAsset?.Invoke(asset);
    }

    private async Task<T> GetAsync<T>(string path)
    {
        using var response = await _http.GetAsync(path, _lifetime.Token);
        CheckResponse(response);
        return await response.Content.ReadFromJsonAsync<T>(Json, _lifetime.Token) ?? throw new JsonException();
    }

    private static void CheckResponse(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Session expired. Please sign in again.",
            HttpStatusCode.Forbidden => "Access denied.",
            HttpStatusCode.NotFound => "Service or asset unavailable. Check that the updated backend is deployed.",
            _ => $"Server error ({(int)response.StatusCode}). Please retry.",
        });
    }

    private static bool IsRequestError(Exception error) => error is HttpRequestException or InvalidOperationException or JsonException or OperationCanceledException;

    private static string FriendlyError(Exception error) => error switch
    {
        OperationCanceledException => "Request timed out. Please retry.",
        HttpRequestException => "Cannot reach the server. Please retry.",
        JsonException => "Unexpected server response.",
        InvalidOperationException => error.Message,
        _ => "Unable to load assets. Please retry.",
    };

    private void NotifyPage()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(IsEmpty));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        ToggleFavoriteCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _lifetime.Cancel();
    }
}
