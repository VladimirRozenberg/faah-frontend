using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed class AssetDetailViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
    private bool _busy, _disposed;
    private string _quoteStatus = "", _chartStatus = "", _newsStatus = "";
    private IReadOnlyList<Candle> _candles = Array.Empty<Candle>();
    private IReadOnlyList<NewsArticle> _news = Array.Empty<NewsArticle>();
    private string _orderSide = "";
    private decimal? _quantity = 1;
    private readonly int _userId;
    private bool _submitting, _uncertain;
    private string _orderStatus = "";
    private decimal _orderPrice;
    private DateTimeOffset _quoteAt, _orderAt;
    private Dictionary<string, List<string>> _historyOptions = new();
    private string _selectedPeriod = "1d", _selectedInterval = "5m";
    private int _chartRequest;
    private bool _changingPeriod;

    public IReadOnlyList<string> Periods => _historyOptions.Keys.ToList();
    public IReadOnlyList<string> Intervals => _historyOptions.TryGetValue(_selectedPeriod, out var values) ? values : Array.Empty<string>();
    public bool HasHistoryOptions => _historyOptions.Count > 0;

    public string SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (_disposed || _changingPeriod || value is null || !_historyOptions.ContainsKey(value) || value == _selectedPeriod) return;
            // Changer la periode peut rendre l'ancien intervalle incompatible.
            _changingPeriod = true;
            _selectedPeriod = value;
            string nextInterval = Intervals.Contains(_selectedInterval) ? _selectedInterval : Intervals[0];
            // Vider la selection avant de remplacer les choix du ComboBox.
            _selectedInterval = "";
            OnPropertyChanged(nameof(SelectedInterval));
            OnPropertyChanged(nameof(SelectedPeriod));
            OnPropertyChanged(nameof(Intervals));
            _selectedInterval = nextInterval;
            OnPropertyChanged(nameof(SelectedInterval));
            _changingPeriod = false;
            _ = LoadCandlesAsync();
        }
    }

    public string SelectedInterval
    {
        get => _selectedInterval;
        set
        {
            if (_disposed || _changingPeriod || value is null || !Intervals.Contains(value)) return;
            if (SetField(ref _selectedInterval, value)) _ = LoadCandlesAsync();
        }
    }

    public AssetDetailViewModel(HttpClient http, Asset asset, Action goBack, int userId = 0)
    {
        _http = http;
        Asset = asset;
        _userId = userId;
        BackCommand = new RelayCommand(goBack);
        RefreshCommand = new RelayCommand(() => { _ = RefreshAsync(); });
        BuyCommand = new RelayCommand(() => PrepareOrder("Achat"));
        SellCommand = new RelayCommand(() => PrepareOrder("Vente"));
        CloseOrderCommand = new RelayCommand(() => { OrderSide = ""; });
        SubmitOrderCommand = new RelayCommand(() => { _ = SubmitOrderAsync(); });
        _timer.Tick += OnTick;
    }

    public Asset Asset { get; }
    public string Title => string.IsNullOrWhiteSpace(Asset.Name) ? Asset.Symbol : $"{Asset.Name} · {Asset.Symbol}";
    public string Details => string.Join(" · ", new[] { Asset.Type, Asset.Exchange, Asset.Country, Asset.Sector, Asset.Industry }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public ICommand BackCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand BuyCommand { get; }
    public ICommand SellCommand { get; }
    public ICommand CloseOrderCommand { get; }
    public ICommand SubmitOrderCommand { get; }
    public string OrderStatus { get => _orderStatus; private set => SetField(ref _orderStatus, value); }
    public bool IsSubmitting { get => _submitting; private set { SetField(ref _submitting, value); NotifyTrading(); } }
    public bool CanPrepareOrder => !_disposed && !IsSubmitting && !_uncertain && _userId > 0
        && Asset.Price > 0 && DateTimeOffset.UtcNow - _quoteAt < TimeSpan.FromMinutes(2)
        && string.Equals(Asset.Currency, "USD", StringComparison.OrdinalIgnoreCase);
    public bool CanSubmitOrder => CanPrepareOrder && IsOrderOpen && Quantity > 0 && Quantity <= 99999999
        && _orderPrice > 0 && DateTimeOffset.UtcNow - _orderAt < TimeSpan.FromMinutes(2);
    public string TradingNotice => _userId <= 0 ? "Reconnecte-toi pour identifier ton portefeuille."
        : _uncertain ? "The result of the last request is uncertain: check your history before making another operation."
        : !string.Equals(Asset.Currency, "USD", StringComparison.OrdinalIgnoreCase) ? "Simulation unavailable: the backend currently records transactions in USD only."
        : "Simulation only; no real money is involved. A recent quote is required. The current backend does not yet manage an available balance.";
    private void NotifyTrading()
    {
        OnPropertyChanged(nameof(CanPrepareOrder));
        OnPropertyChanged(nameof(CanSubmitOrder));
        OnPropertyChanged(nameof(TradingNotice));
    }
    public bool IsBusy { get => _busy; private set => SetField(ref _busy, value); }
    public string QuoteStatus { get => _quoteStatus; private set => SetField(ref _quoteStatus, value); }
    public string ChartStatus { get => _chartStatus; private set => SetField(ref _chartStatus, value); }
    public string NewsStatus { get => _newsStatus; private set => SetField(ref _newsStatus, value); }
    public IReadOnlyList<Candle> Candles { get => _candles; private set => SetField(ref _candles, value); }
    public IReadOnlyList<NewsArticle> News { get => _news; private set => SetField(ref _news, value); }

    // Le clic Acheter/Vendre ouvre le formulaire. Seule la confirmation envoie le POST.
    public string OrderSide
    {
        get => _orderSide;
        private set { SetField(ref _orderSide, value); OnPropertyChanged(nameof(IsOrderOpen)); NotifyTrading(); }
    }
    public bool IsOrderOpen => OrderSide.Length > 0;
    public decimal? Quantity
    {
        get => _quantity;
        set { SetField(ref _quantity, value); OnPropertyChanged(nameof(OrderEstimate)); NotifyTrading(); }
    }
    public string OrderEstimate => Quantity > 0 && Quantity <= 99999999 && _orderPrice > 0
        ? $"Simulation price: {_orderPrice:G10} USD · amount: {Quantity.Value * _orderPrice:N2} USD (excluding fees)"
        : "Enter a positive quantity. A quote is required for the estimate.";
    private void PrepareOrder(string side)
    {
        if (!CanPrepareOrder) return;
        _orderPrice = Asset.Price!.Value; // Prix fige pour que le montant ne change pas pendant la confirmation.
        _orderAt = _quoteAt;
        Quantity = 1;
        OrderSide = side == "Achat" ? "Buy" : side == "Vente" ? "Sell" : side;
        OrderStatus = "";
    }

    public async Task SubmitOrderAsync()
    {
        if (_disposed || IsSubmitting) return;
        if (!CanSubmitOrder) { OrderStatus = "Verifie la quantite et actualise le cours avant de preparer un nouvel ordre."; return; }
        IsSubmitting = true;
        OrderStatus = "Saving simulation…";
        string action = OrderSide == "Buy" ? "buy" : "sell";
        var data = new Dictionary<string, object>
        {
            ["symbol"] = Asset.Symbol,
            ["quantity"] = Quantity!.Value,
            [action == "buy" ? "purchase_price" : "sale_price"] = _orderPrice
        };
        try
        {
            // Le meme client HTTP conserve le token du compte connecte. Aucun nouvel essai automatique.
            using var response = await _http.PostAsJsonAsync($"api/users/{_userId}/portfolio/assets/{action}", data, Json, _lifetime.Token);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode >= 500) throw new HttpRequestException();
                var error = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: _lifetime.Token);
                string reason = error.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String
                    ? detail.GetString()! : $"Demande refusee ({(int)response.StatusCode}).";
                if (!_disposed) OrderStatus = reason;
                return;
            }
            var portfolio = await response.Content.ReadFromJsonAsync<TradePortfolioResponse>(Json, _lifetime.Token);
            if (portfolio is null || portfolio.UserId != _userId || portfolio.Positions is null) throw new JsonException();
            if (_disposed) return;
            decimal remaining = portfolio.Positions.FirstOrDefault(p => string.Equals(p.Symbol, Asset.Symbol, StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0;
            OrderStatus = $"Simulated {OrderSide.ToLowerInvariant()} recorded. Holdings: {remaining:G10} {Asset.Symbol}.";
            OrderSide = "";
        }
        catch (Exception)
        {
            // Un timeout peut arriver APRES l'ecriture en base : ne pas annoncer un echec certain.
            if (!_disposed)
            {
                _uncertain = true;
                OrderStatus = "Confirmation non recue. L'operation a peut-etre ete enregistree : verifie l'historique avant de recommencer.";
            }
        }
        finally { if (!_disposed) IsSubmitting = false; }
    }

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
        _ = RefreshAsync();
    }
    private void OnTick(object? sender, EventArgs e) => _ = RefreshAsync();

    public async Task RefreshAsync()
    {
        if (_disposed || IsBusy) return;
        IsBusy = true;
        // Les trois blocs sont independants : une panne des news ne masque pas le graphique.
        try { await Task.WhenAll(LoadQuoteAsync(), RefreshChartAsync(), LoadNewsAsync()); }
        finally { if (!_disposed) { IsBusy = false; NotifyTrading(); } }
    }

    private async Task<T> GetAsync<T>(string route)
    {
        using var response = await _http.GetAsync(route, _lifetime.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, _lifetime.Token)
            ?? throw new JsonException("Reponse vide.");
    }

    private async Task LoadQuoteAsync()
    {
            QuoteStatus = "Refreshing quote…";
        try
        {
            var quote = await GetAsync<AssetListMarket>($"api/assets/{Uri.EscapeDataString(Asset.Symbol)}/market");
            if (_disposed) return;
            if (!string.Equals(quote.Symbol, Asset.Symbol, StringComparison.OrdinalIgnoreCase)) throw new JsonException();
            // La liste et le détail affichent maintenant les cours via le même objet Market.
            Asset.Market = quote;
            Asset.Price = quote?.LastPrice;
            Asset.ChangePercent = quote?.ChangePercent;
            Asset.MarketVolume = quote?.Volume;
            if (quote?.Currency is not null) Asset.Currency = quote.Currency;
            Asset.IsLoadingPrice = false;
            Asset.RefreshQuoteDisplay();
            _quoteAt = DateTimeOffset.UtcNow;
            OnPropertyChanged(nameof(OrderEstimate));
            QuoteStatus = quote?.LastPrice is null ? "Quote unavailable for this asset." : $"Received at {DateTime.Now:HH:mm:ss} · refreshes every 60 s";
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            if (!_disposed) QuoteStatus = "Quote not refreshed (the last value was kept). " + Explain(ex);
        }
    }

    private async Task RefreshChartAsync()
    {
        // On demande les choix au backend une seule fois, puis on les reutilise.
        if (!HasHistoryOptions)
        {
            ChartStatus = "Loading available periods…";
            try
            {
                var options = await GetAsync<Dictionary<string, List<string>>>("api/history-options");
                if (_disposed) return;
                if (options.Count == 0 || options.Any(p => string.IsNullOrWhiteSpace(p.Key)
                    || p.Value is null || p.Value.Count == 0 || p.Value.Any(string.IsNullOrWhiteSpace)))
                    throw new JsonException();
                string period = options.ContainsKey(_selectedPeriod) ? _selectedPeriod : options.Keys.First();
                string interval = options[period].Contains(_selectedInterval) ? _selectedInterval : options[period][0];
                _changingPeriod = true;
                _selectedPeriod = "";
                _selectedInterval = "";
                OnPropertyChanged(nameof(SelectedPeriod));
                OnPropertyChanged(nameof(SelectedInterval));
                _historyOptions = options;
                _selectedPeriod = period;
                OnPropertyChanged(nameof(Periods));
                OnPropertyChanged(nameof(Intervals));
                _selectedInterval = interval;
                OnPropertyChanged(nameof(SelectedPeriod));
                OnPropertyChanged(nameof(SelectedInterval));
                _changingPeriod = false;
                OnPropertyChanged(nameof(HasHistoryOptions));
            }
            catch (Exception ex)
            {
                if (!_disposed) ChartStatus = "Periods unavailable. " + Explain(ex);
                return; // Le bouton Actualiser permettra de reessayer.
            }
        }
        await LoadCandlesAsync();
    }

    private async Task LoadCandlesAsync()
    {
        if (_disposed || !HasHistoryOptions) return;
        // Une ancienne reponse ne doit pas remplacer le dernier choix de l'utilisateur.
        int request = ++_chartRequest;
        string period = SelectedPeriod, interval = SelectedInterval;
        Candles = Array.Empty<Candle>();
        ChartStatus = "Loading candles…";
        try
        {
            // On transmet les deux selections a la route Python existante.
            string symbol = Uri.EscapeDataString(Asset.Symbol);
            var result = await GetAsync<CandleResponse>($"api/assets/{symbol}/candles?period={Uri.EscapeDataString(period)}&interval={Uri.EscapeDataString(interval)}");
            if (_disposed || request != _chartRequest) return;
            if (!string.Equals(result.Symbol, Asset.Symbol, StringComparison.OrdinalIgnoreCase) || result.Candles is null)
                throw new JsonException();
            if (result.Candles.Any(c => c is null || c.High < c.Low || c.High < Math.Max(c.Open, c.Close) || c.Low > Math.Min(c.Open, c.Close)))
                throw new JsonException();
            Candles = result.Candles.OrderBy(c => c.Timestamp).DistinctBy(c => c.Timestamp).ToList();
            ChartStatus = Candles.Count == 0 ? "No candles available for this period." : $"Period: {period} · candles: {interval} · UTC hours · green: rise / red: fall";
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            if (!_disposed && request == _chartRequest) { Candles = Array.Empty<Candle>(); ChartStatus = "Graphique indisponible. " + Explain(ex); }
        }
    }

    private async Task LoadNewsAsync()
    {
        NewsStatus = "Loading news…";
        try
        {
            // Le backend suit les liens de classification en BDD pour cet actif.
            string symbol = Uri.EscapeDataString(Asset.Symbol);
            var result = await GetAsync<NewsResponse>($"api/assets/{symbol}/news");
            if (_disposed) return;
            News = result.Items.OrderByDescending(n => n.SortDate).ToList();
            NewsStatus = News.Count == 0 ? "No classified news linked to this asset in the database."
                : "News linked to this asset through classifications stored in the database.";
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            if (!_disposed) { News = Array.Empty<NewsArticle>(); NewsStatus = "News unavailable. " + Explain(ex); }
        }
    }

    private static string Explain(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => "Session expired: please sign in again.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } => "The route or asset was not found on the current backend.",
        JsonException => "The received format does not match the expected format.",
        _ => "Check the connection and backend, then refresh."
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _lifetime.Cancel();
        // Le HttpClient appartient au Shell : on ne le ferme pas ici.
    }
}
