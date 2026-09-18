using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using FAAH_Frontend.Models;
namespace FAAH_Frontend.ViewModels;
public sealed class AssetListViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, NumberHandling = JsonNumberHandling.AllowReadingFromString };
    private bool _busy, _disposed, _loaded, _favoritesLoaded;
    private List<Asset> _allAssets = new();
    private int _page = 1;
    private bool _loadingPrices;
    public const int PageSize = 10;
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public int PageCount => Math.Max(1, (_allAssets.Count + PageSize - 1) / PageSize);
    public bool IsLoadingPrices { get => _loadingPrices; private set => SetField(ref _loadingPrices, value); }
    private void ShowPage()
    {
        _page = Math.Clamp(_page, 1, PageCount);
        Assets.Clear();
        foreach (var asset in _allAssets.Skip((_page - 1) * PageSize).Take(PageSize)) Assets.Add(asset);
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        Notify();
    }
    private string _error = "", _updated = "Not loaded yet";
    public AssetListViewModel(HttpClient http)
    {
        _http = http;
        PreviousPageCommand = new RelayCommand(p => { if (_page > 1) { _page--; ShowPage(); } }, p => !_disposed && _page > 1);
        NextPageCommand = new RelayCommand(p => { if (_page < PageCount) { _page++; ShowPage(); } }, p => !_disposed && _page < PageCount);
        RefreshCommand = new RelayCommand(p => { _ = RefreshAsync(); }, p => !IsBusy && !_disposed);
        ToggleFavoriteCommand = new RelayCommand(p => { if(p is Asset asset) _ = ToggleFavoriteAsync(asset); }, p => !IsBusy && !_disposed && _favoritesLoaded);
        _timer.Tick += OnTick;
    }
    public ObservableCollection<Asset> Assets { get; } = new();
    public ICommand RefreshCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public bool IsBusy { get => _busy; private set { SetField(ref _busy,value); ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged(); ((RelayCommand)ToggleFavoriteCommand).RaiseCanExecuteChanged(); Notify(); } }
    public string Error { get => _error; private set { SetField(ref _error,value); Notify(); } }
    public string Updated { get => _updated; private set => SetField(ref _updated,value); }
    public bool HasError => Error.Length > 0;
    public bool IsEmpty => _loaded && !IsBusy && !HasError && Assets.Count == 0;
    public string PageLabel => $"Page {_page} of {PageCount} · {_allAssets.Count} assets";
    private void Notify() { OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(PageLabel)); }
    public void Start() { if (_disposed) return; _timer.Start(); _ = RefreshAsync(); }
    private void OnTick(object? sender, EventArgs e) => _ = RefreshAsync();
    private static void Ensure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode switch {
            HttpStatusCode.Unauthorized => "Session expired. Please sign in again.",
            HttpStatusCode.Forbidden => "Access denied.",
            HttpStatusCode.NotFound => "Service or asset unavailable. Check that the updated backend is deployed.",
            _ => $"Server error ({(int)response.StatusCode}). Please retry."
        });
    }
    private async Task<T> GetAsync<T>(string path)
    {
        using var response = await _http.GetAsync(path,_lifetime.Token); Ensure(response);
        return await response.Content.ReadFromJsonAsync<T>(Json,_lifetime.Token) ?? throw new JsonException();
    }
    public async Task RefreshAsync()
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; Error = "";
        try
        {
            var catalog = await GetAsync<AssetResponse>("api/assets");
            if (catalog.Items is null || catalog.Items.Any(a => a is null || string.IsNullOrWhiteSpace(a.Symbol))) throw new JsonException();
            if (_disposed) return;
            _favoritesLoaded = false;
            _allAssets = catalog.Items;
            _loaded = true;
            ShowPage(); // Display the catalog immediately; prices must not delay pagination.
            var favorites = await GetAsync<FavoriteResponse>("api/favorites");
            if (favorites.AssetIds is null) throw new JsonException();
            if (_disposed) return;
            var favoriteIds = favorites.AssetIds.ToHashSet();
            foreach (var asset in _allAssets) asset.IsFavorite = favoriteIds.Contains(asset.Id);
            _favoritesLoaded = true;
            MarketResponse? market = null;
            IsLoadingPrices = true;
            foreach (var asset in _allAssets) asset.IsLoadingPrice = true;
            try { market = await GetAsync<MarketResponse>("api/market"); if(market.Items is null || market.Items.Any(q => q is null || string.IsNullOrWhiteSpace(q.Symbol))) throw new JsonException(); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException || ex is OperationCanceledException && !_disposed)
            { market = null; Error = "Market prices unavailable. Assets and favorites are available; please retry."; }
            if (_disposed) return;
            var quotes = market?.Items.GroupBy(q => q.Symbol,StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,g => g.Last(),StringComparer.OrdinalIgnoreCase);
            foreach(var asset in _allAssets)
            {
                if(quotes is not null && quotes.TryGetValue(asset.Symbol,out var quote))
                { asset.Price=quote.LastPrice; asset.ChangePercent=quote.ChangePercent; asset.MarketVolume=quote.Volume; asset.Currency=quote.Currency ?? asset.Currency; }
                asset.IsLoadingPrice = false;
                asset.RefreshQuoteDisplay();
            }
            Updated="Last loaded: " + DateTime.Now.ToString("HH:mm:ss") + " · Auto-refresh: 60 s";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or OperationCanceledException) { SetError(ex); }
        finally { IsLoadingPrices=false; IsBusy=false; }
    }
    public async Task ToggleFavoriteAsync(Asset asset)
    {
        if(IsBusy || _disposed || !_favoritesLoaded || !Assets.Contains(asset)) return;
        IsBusy=true; Error="";
        try
        {
            bool add=!asset.IsFavorite;
            using var request=new HttpRequestMessage(add ? HttpMethod.Put : HttpMethod.Delete,$"api/favorites/{asset.Id}");
            using var response=await _http.SendAsync(request,_lifetime.Token); Ensure(response);
            if(!_disposed) asset.IsFavorite=add;
        }
        catch(Exception ex) when(ex is HttpRequestException or InvalidOperationException or OperationCanceledException) { SetError(ex); }
        finally { IsBusy=false; }
    }
    private void SetError(Exception ex)
    {
        if(_disposed) return;
        Error=ex switch { OperationCanceledException => "Request timed out. Please retry.", HttpRequestException => "Cannot reach the server. Please retry.", JsonException => "Unexpected server response.", _ => ex.Message };
    }
    public void Dispose() { if(_disposed) return; _disposed=true; _timer.Stop(); _timer.Tick-=OnTick; _lifetime.Cancel(); }
}
