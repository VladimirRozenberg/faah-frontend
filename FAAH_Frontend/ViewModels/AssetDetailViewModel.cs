using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using FAAH_Frontend.Services;

namespace FAAH_Frontend.ViewModels;

public sealed class AssetDetailViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly AssetLogoService? _logos;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
    private bool _busy, _disposed, _catalogMetadataLoaded, _newsBusy;
    private string _quoteStatus = "", _chartStatus = "", _newsStatus = "";
    private IReadOnlyList<Candle> _candles = Array.Empty<Candle>();
    private IReadOnlyList<NewsArticle> _news = Array.Empty<NewsArticle>();
    private int _newsPage = 1, _newsPageCount = 1;
    private readonly ObservableCollection<int> _newsPageNumbers = new();
    private string _orderSide = "";
    private decimal? _quantity = 1;
    private decimal? _cashAmount;
    private string _orderInputMode = "Quantity";
    public IReadOnlyList<string> OrderInputModes { get; } = new[] { "Quantity", "Amount (USD)" };
    public string OrderInputMode
    {
        get => _orderInputMode;
        set
        {
            if (IsSubmitting || !OrderInputModes.Contains(value) || !SetField(ref _orderInputMode, value)) return;
            // Conserver l'équivalent de la quantité en passant au mode montant.
            if (IsAmountMode)
            {
                if (OrderSide == "Sell" && Quantity == HeldQuantity && CanSellAll)
                    FillSellAll();
                else
                    CashAmount = Quantity > 0 && Quantity <= 99999999 && _orderPrice > 0
                        ? decimal.Round(Quantity.Value * _orderPrice, 2) : null;
            }
            OnPropertyChanged(nameof(IsAmountMode));
            OnPropertyChanged(nameof(IsQuantityMode));
            NotifyTrading();
        }
    }
    public bool IsAmountMode => OrderInputMode == "Amount (USD)";
    public bool IsQuantityMode => !IsAmountMode;
    public decimal CashAmountMaximum => OrderSide == "Sell" && _orderPrice > 0
        ? Math.Max(99999999m, decimal.Round(HeldQuantity * _orderPrice, 2)) : 99999999m;
    public decimal? CashAmount
    {
        get => _cashAmount;
        set
        {
            if (IsSubmitting) return;
            SetField(ref _cashAmount, value);
            // Quantité = montant / prix. Arrondir vers le bas à 8 décimales.
            // Le cours du ticket reste figé ; seul le serveur fixe le prix final.
            Quantity = value > 0 && value <= CashAmountMaximum && _orderPrice > 0 && _orderPrice >= value.Value / 99999999m
                ? decimal.Floor(value.Value / _orderPrice * 100000000m) / 100000000m : null;
        }
    }
    public string OrderQuantityDisplay => Quantity > 0 && Quantity <= 99999999
        ? $"≈ {Quantity.Value:0.########} {Asset.Symbol}" : "Enter a valid amount or quantity.";
    public string AmountLabel => IsBuyOrder ? "Amount to invest (USD)" : "Amount to sell (USD)";
    private readonly int _userId;
    private readonly int? _preferredPortfolioId;
    private bool _initialPortfolioSelectionApplied;
    private bool _submitting, _uncertain;
    private string _orderStatus = "";
    private decimal _orderPrice;
    private UsdQuote? _usdQuote;
    private string _conversionStatus = "";
    private string _orderCurrency = "USD", _orderRateDate = "";
    private decimal _orderOriginalPrice, _orderRate = 1;
    private bool NeedsConversion => !string.Equals(Asset.Currency, "USD", StringComparison.Ordinal);
    public bool HasOrderConversion => IsOrderOpen && _orderCurrency != "USD";
    public string OrderOriginalTotal => Quantity > 0 && Quantity <= 99999999
        ? $"{Quantity.Value * _orderOriginalPrice:N2} {_orderCurrency}" : "—";
    public string OrderExchangeRate => $"1 {_orderCurrency} ≈ {_orderRate:0.########} USD";
    public string OrderRateDate => $"Reference rate · {_orderRateDate}";
    private DateTimeOffset _quoteAt, _orderAt;
    private Dictionary<string, List<string>> _historyOptions = new();
    private string _selectedPeriod = "5d", _selectedInterval = "5m";
    private int _chartRequest;
    private bool _changingPeriod;
    private IReadOnlyList<TradePortfolioResponse> _portfolios = Array.Empty<TradePortfolioResponse>();
    private TradePortfolioResponse? _selectedPortfolio;
    private bool _portfoliosReady;
    private string _portfolioStatus = "Loading portfolios…";
    private int _orderPortfolioId;

    public IReadOnlyList<TradePortfolioResponse> Portfolios => _portfolios;
    public string PortfolioStatus { get => _portfolioStatus; private set { SetField(ref _portfolioStatus, value); OnPropertyChanged(nameof(HasPortfolioStatus)); } }
    public bool HasPortfolioStatus => !string.IsNullOrEmpty(PortfolioStatus);
    public bool CanSelectPortfolio => !_disposed && !IsSubmitting && !IsBusy && _portfoliosReady;
    public TradePortfolioResponse? SelectedPortfolio
    {
        get => _selectedPortfolio;
        set
        {
            if (IsSubmitting || IsBusy || (value is not null && !Portfolios.Contains(value))) return;
            if (!SetField(ref _selectedPortfolio, value)) return;
            // Un ordre préparé ne doit jamais partir vers un autre portefeuille.
            OrderSide = "";
            if (!_uncertain) OrderStatus = "";
            NotifyTrading();
        }
    }
    public decimal HeldQuantity => SelectedPortfolio?.Positions
        .FirstOrDefault(p => string.Equals(p.Symbol, Asset.Symbol, StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0;
    // Afficher la quantité seulement quand les données du portefeuille sont disponibles.
    public bool HasHoldings => _portfoliosReady && SelectedPortfolio is not null;
    public string HoldingsDisplay => SelectedPortfolio is null ? "" : $"{HeldQuantity:0.########} {Asset.Symbol}";

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

    public AssetDetailViewModel(HttpClient http, Asset asset, Action goBack, int userId = 0, Action<int>? openArticle = null, int? preferredPortfolioId = null, AssetLogoService? logos = null)
    {
        _http = http;
        _logos = logos;
        Asset = asset;
        _userId = userId;
        _preferredPortfolioId = preferredPortfolioId;
        _openArticle = openArticle;
        BackCommand = new RelayCommand(goBack);
        RefreshCommand = new RelayCommand(() => { _ = RefreshAsync(); });
        BuyCommand = new RelayCommand(() => PrepareOrder("Achat"));
        SellCommand = new RelayCommand(() => PrepareOrder("Vente"));
        SellAllCommand = new RelayCommand(FillSellAll);
        CloseOrderCommand = new RelayCommand(() => { OrderSide = ""; });
        SubmitOrderCommand = new RelayCommand(() => { _ = SubmitOrderAsync(); });
        OpenArticleCommand = new RelayCommand(parameter =>
        {
            if (parameter is int id) _openArticle?.Invoke(id);
            else if (parameter is NewsArticle article) _openArticle?.Invoke(article.Id);
        }, parameter => !_disposed && _openArticle is not null);
        NewsFirstPageCommand = new RelayCommand(_ => _ = LoadNewsPageAsync(1), _ => !_disposed && !_newsBusy && _newsPage > 1);
        NewsPreviousPageCommand = new RelayCommand(_ => _ = LoadNewsPageAsync(_newsPage - 1), _ => !_disposed && !_newsBusy && _newsPage > 1);
        NewsNextPageCommand = new RelayCommand(_ => _ = LoadNewsPageAsync(_newsPage + 1), _ => !_disposed && !_newsBusy && _newsPage < _newsPageCount);
        NewsLastPageCommand = new RelayCommand(_ => _ = LoadNewsPageAsync(_newsPageCount), _ => !_disposed && !_newsBusy && _newsPage < _newsPageCount);
        NewsGoToPageCommand = new RelayCommand(
            parameter => { if (parameter is int page) _ = LoadNewsPageAsync(page); },
            parameter => !_disposed && !_newsBusy && parameter is int page && page >= 1 && page <= _newsPageCount && page != _newsPage);
        _timer.Tick += OnTick;
    }

    private readonly Action<int>? _openArticle;
    public Asset Asset { get; }
    public string Title => string.IsNullOrWhiteSpace(Asset.Name) ? Asset.Symbol : $"{Asset.Name} · {Asset.Symbol}";
    public string BackButtonText => _preferredPortfolioId.HasValue ? "← Back to portfolio" : "← Back to assets";
    public string Details => string.Join(" · ", new[] { Asset.Type, Asset.Exchange, Asset.Country, Asset.Sector, Asset.Industry }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public ICommand BackCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand BuyCommand { get; }
    public ICommand SellCommand { get; }
    public ICommand SellAllCommand { get; }
    public ICommand CloseOrderCommand { get; }
    public ICommand SubmitOrderCommand { get; }
    public ICommand OpenArticleCommand { get; }
    public string OrderStatus { get => _orderStatus; private set { SetField(ref _orderStatus, value); OnPropertyChanged(nameof(HasOrderStatus)); } }
    public bool HasOrderStatus => !string.IsNullOrEmpty(OrderStatus);
    public bool IsSubmitting { get => _submitting; private set { SetField(ref _submitting, value); NotifyTrading(); } }
    public bool CanPrepareOrder => !_disposed && !IsBusy && !IsSubmitting && !_uncertain && _userId > 0
        && _portfoliosReady && SelectedPortfolio is not null
        && Asset.Price > 0 && DateTimeOffset.UtcNow - _quoteAt < TimeSpan.FromMinutes(2)
        && (!NeedsConversion || _usdQuote is not null);
    public bool CanBuy => CanPrepareOrder;
    public bool CanSell => CanPrepareOrder && HeldQuantity > 0;
    public bool CanSellAll => IsOrderOpen && OrderSide == "Sell" && CanSell && HeldQuantity <= 99999999;
    public bool CanSubmitOrder => CanPrepareOrder && IsOrderOpen && Quantity > 0 && Quantity <= 99999999
        && SelectedPortfolio!.Id == _orderPortfolioId && (OrderSide != "Sell" || Quantity <= HeldQuantity)
        && _orderPrice > 0 && DateTimeOffset.UtcNow - _orderAt < TimeSpan.FromMinutes(2);
    public string TradingNotice => _userId <= 0 ? "Reconnecte-toi pour identifier ton portefeuille."
        : _uncertain ? "The result of the last request is uncertain: check your history before making another operation."
        : NeedsConversion && _usdQuote is null ? (_conversionStatus.Length > 0 ? _conversionStatus : "Loading USD conversion…")
        : SelectedPortfolio is null ? "Select a compatible portfolio to trade."
        : "";
    public bool HasTradingNotice => !string.IsNullOrEmpty(TradingNotice);
    private void NotifyTrading()
    {
        OnPropertyChanged(nameof(CanPrepareOrder));
        OnPropertyChanged(nameof(CanBuy));
        OnPropertyChanged(nameof(CanSell));
        OnPropertyChanged(nameof(CanSellAll));
        OnPropertyChanged(nameof(CashAmountMaximum));
        OnPropertyChanged(nameof(CanSelectPortfolio));
        OnPropertyChanged(nameof(HeldQuantity));
        OnPropertyChanged(nameof(HoldingsDisplay));
        OnPropertyChanged(nameof(HasHoldings));
        OnPropertyChanged(nameof(CanSubmitOrder));
        OnPropertyChanged(nameof(TradingNotice));
        OnPropertyChanged(nameof(HasTradingNotice));
        OnPropertyChanged(nameof(OrderTitle));
        OnPropertyChanged(nameof(ConfirmOrderLabel));
        OnPropertyChanged(nameof(IsBuyOrder));
        OnPropertyChanged(nameof(OrderUnitPrice));
        OnPropertyChanged(nameof(OrderTotal));
        OnPropertyChanged(nameof(OrderQuantityDisplay));
        OnPropertyChanged(nameof(AmountLabel));
        OnPropertyChanged(nameof(HasOrderConversion));
        OnPropertyChanged(nameof(OrderOriginalTotal));
        OnPropertyChanged(nameof(OrderExchangeRate));
        OnPropertyChanged(nameof(OrderRateDate));
    }
    public bool IsBusy { get => _busy; private set { SetField(ref _busy, value); NotifyTrading(); } }
    public string QuoteStatus { get => _quoteStatus; private set => SetField(ref _quoteStatus, value); }
    public string ChartStatus { get => _chartStatus; private set => SetField(ref _chartStatus, value); }
    public string NewsStatus { get => _newsStatus; private set => SetField(ref _newsStatus, value); }
    public IReadOnlyList<Candle> Candles { get => _candles; private set => SetField(ref _candles, value); }
    public IReadOnlyList<NewsArticle> News { get => _news; private set => SetField(ref _news, value); }
    public ObservableCollection<int> NewsPageNumbers => _newsPageNumbers;
    public int NewsPage => _newsPage;
    public int NewsPageCount => _newsPageCount;
    public bool HasMultipleNewsPages => _newsPageCount > 1;
    public string NewsPageLabel => $"Page {_newsPage} of {_newsPageCount}";
    public ICommand NewsFirstPageCommand { get; private set; } = null!;
    public ICommand NewsPreviousPageCommand { get; private set; } = null!;
    public ICommand NewsNextPageCommand { get; private set; } = null!;
    public ICommand NewsLastPageCommand { get; private set; } = null!;
    public ICommand NewsGoToPageCommand { get; private set; } = null!;

    // Le clic Acheter/Vendre ouvre le formulaire. Seule la confirmation envoie le POST.
    public string OrderSide
    {
        get => _orderSide;
        private set { SetField(ref _orderSide, value); OnPropertyChanged(nameof(IsOrderOpen)); NotifyTrading(); }
    }
    public bool IsOrderOpen => OrderSide.Length > 0;
    public bool IsBuyOrder => OrderSide == "Buy";
    public string OrderTitle => $"{OrderSide} {Asset.Symbol}";
    public string ConfirmOrderLabel => IsSubmitting ? "Processing…" : IsBuyOrder ? "Confirm buy" : "Confirm sell";
    // Estimation uniquement : le backend fixe toujours le prix final d'exécution.
    public string OrderUnitPrice => $"{_orderOriginalPrice:0.00######} {_orderCurrency}";
    public string OrderTotal => Quantity > 0 && Quantity <= 99999999 && _orderPrice > 0
        ? $"{Quantity.Value * _orderPrice:N2} USD" : "—";
    public decimal? Quantity
    {
        get => _quantity;
        set
        {
            if (IsSubmitting) return;
            SetField(ref _quantity, value);
            OnPropertyChanged(nameof(OrderEstimate));
            NotifyTrading();
        }
    }
    public string OrderEstimate => Quantity > 0 && Quantity <= 99999999 && _orderPrice > 0
        ? $"Portfolio: {SelectedPortfolio?.Name} · Estimated price: {_orderPrice:G10} USD · amount: {Quantity.Value * _orderPrice:N2} USD (excluding fees)"
        : "Enter a positive quantity. A quote is required for the estimate.";
    private void FillSellAll()
    {
        if (!CanSellAll) return;
        // ALL conserve la quantité exacte : arrondir le montant USD ne doit pas laisser un reliquat.
        _cashAmount = decimal.Round(HeldQuantity * _orderPrice, 2);
        OnPropertyChanged(nameof(CashAmount));
        Quantity = HeldQuantity;
    }
    private void PrepareOrder(string side)
    {
        if (side == "Achat" ? !CanBuy : !CanSell) return;
        _orderPortfolioId = SelectedPortfolio!.Id;
        // Figer les deux prix et le taux pour éviter que le ticket change pendant la saisie.
        _orderPrice = NeedsConversion ? _usdQuote!.PriceUsd : Asset.Price!.Value;
        _orderOriginalPrice = NeedsConversion ? _usdQuote!.OriginalPrice : _orderPrice;
        _orderCurrency = NeedsConversion ? _usdQuote!.OriginalCurrency : "USD";
        _orderRate = NeedsConversion ? _usdQuote!.RateToUsd : 1;
        _orderRateDate = NeedsConversion ? _usdQuote!.RateDate : "";
        _orderAt = _quoteAt;
        OrderInputMode = "Quantity";
        _cashAmount = null;
        OnPropertyChanged(nameof(CashAmount));
        Quantity = side == "Vente" ? Math.Min(1, HeldQuantity) : 1;
        OrderSide = side == "Achat" ? "Buy" : side == "Vente" ? "Sell" : side;
        OrderStatus = "";
    }

    public async Task SubmitOrderAsync()
    {
        if (_disposed || IsSubmitting) return;
        if (!CanSubmitOrder) { OrderStatus = "Verifie la quantite et actualise le cours avant de preparer un nouvel ordre."; return; }
        IsSubmitting = true;
        OrderStatus = "Processing order…";
        string action = OrderSide == "Buy" ? "buy" : "sell";
        int portfolioId = _orderPortfolioId;
        var data = new Dictionary<string, object>
        {
            ["symbol"] = Asset.Symbol,
            ["quantity"] = Quantity!.Value,
            [action == "buy" ? "purchase_price" : "sale_price"] = _orderPrice
        };
        try
        {
            // Le meme client HTTP conserve le token du compte connecte. Aucun nouvel essai automatique.
            using var response = await _http.PostAsJsonAsync($"api/users/me/portfolios/{portfolioId}/assets/{action}", data, Json, _lifetime.Token);
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
            if (portfolio is null || portfolio.Id != portfolioId || portfolio.UserId != _userId || portfolio.Positions is null) throw new JsonException();
            if (_disposed) return;
            decimal remaining = portfolio.Positions.FirstOrDefault(p => string.Equals(p.Symbol, Asset.Symbol, StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0;
            // La réponse du serveur devient la nouvelle quantité détenue, sans la deviner.
            SelectedPortfolio!.Positions = portfolio.Positions;
            NotifyTrading();
            OrderStatus = $"{OrderSide} recorded. Holdings: {remaining:G10} {Asset.Symbol}.";
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
        if (_disposed || IsBusy || IsSubmitting) return;
        IsBusy = true;
        try
        {
            await LoadCatalogMetadataAsync();
            if (_disposed) return;
        await Task.WhenAll(LoadQuoteAsync(), RefreshChartAsync(), LoadNewsAsync(_newsPage), LoadPortfoliosAsync());
        }
        finally { if (!_disposed) { IsBusy = false; NotifyTrading(); } }
    }

    private async Task LoadCatalogMetadataAsync()
    {
        if (_disposed || _catalogMetadataLoaded) return;
        try
        {
            var catalog = await GetAsync<AssetResponse>(
                $"api/assets?page=1&page_size={AssetListViewModel.PageSize}&search={Uri.EscapeDataString(Asset.Symbol)}");
            if (_disposed) return;
            if (catalog.Items is null) throw new JsonException("Asset catalog response did not contain items.");

            _catalogMetadataLoaded = true;
            var catalogAsset = catalog.Items.FirstOrDefault(item =>
                string.Equals(item.Symbol, Asset.Symbol, StringComparison.OrdinalIgnoreCase));
            if (catalogAsset is null) return;

            Asset.Id = catalogAsset.Id;
            Asset.Name = catalogAsset.Name ?? Asset.Name;
            Asset.Type = catalogAsset.Type ?? Asset.Type;
            Asset.Exchange = catalogAsset.Exchange ?? Asset.Exchange;
            Asset.Country = catalogAsset.Country ?? Asset.Country;
            Asset.Sector = catalogAsset.Sector ?? Asset.Sector;
            Asset.Industry = catalogAsset.Industry ?? Asset.Industry;
            Asset.Currency = catalogAsset.Currency ?? Asset.Currency;
            Asset.LogoUrl = catalogAsset.LogoUrl ?? Asset.LogoUrl ?? $"/api/assets/{Uri.EscapeDataString(Asset.Symbol)}/logo";
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Details));
            if (_logos is not null) await _logos.LoadAsync(Asset, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            if (!_disposed) System.Diagnostics.Debug.WriteLine($"ASSET CATALOG METADATA ERROR: {ex}");
        }
    }

    private async Task<T> GetAsync<T>(string route)
    {
        using var response = await _http.GetAsync(route, _lifetime.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, _lifetime.Token)
            ?? throw new JsonException("Reponse vide.");
    }

    private async Task LoadPortfoliosAsync()
    {
        _portfoliosReady = false;
        PortfolioStatus = "Loading portfolios…";
        NotifyTrading();
        try
        {
            if (_userId <= 0) { PortfolioStatus = "Sign in to load your portfolios."; return; }
            var result = await GetAsync<TradePortfolioListResponse>("api/users/me/portfolios");
            if (_disposed) return;
            if (result.Items is null || result.Items.Any(p => p is null || p.PortfolioId <= 0
                || p.Status is null || p.BaseCurrency is null)) throw new JsonException();

            // La nouvelle liste ne contient plus les positions ni les types autorisés.
            // Charger le détail des portefeuilles actifs en USD avant de permettre une transaction.
            var details = new List<TradePortfolioResponse>();
            foreach (var summary in result.Items.Where(p => p.Status == "active" && p.BaseCurrency == "USD"))
            {
                var portfolio = await GetAsync<TradePortfolioResponse>($"api/users/me/portfolios/{summary.PortfolioId}");
                if (_disposed) return;
                if (portfolio.Id != summary.PortfolioId || portfolio.UserId != _userId
                    || portfolio.Positions is null || portfolio.PreferredAssetTypes is null) throw new JsonException();
                details.Add(portfolio);
            }
            // Liste vide de préférences = tous les types, comme dans le backend.
            _portfolios = details.Where(p => p.IsActive
                && string.Equals(p.BaseCurrency, "USD", StringComparison.OrdinalIgnoreCase)
                && (p.PreferredAssetTypes.Count == 0 || p.PreferredAssetTypes.Contains(Asset.Type ?? "", StringComparer.OrdinalIgnoreCase)))
                .ToList();
            int? previousId = SelectedPortfolio?.Id;
            _selectedPortfolio = _portfolios.FirstOrDefault(p => p.Id == previousId);
            if (!_initialPortfolioSelectionApplied)
            {
                _selectedPortfolio ??= _portfolios.FirstOrDefault(p => p.Id == _preferredPortfolioId);
                _initialPortfolioSelectionApplied = true;
            }
            if (_selectedPortfolio is null) OrderSide = "";
            _portfoliosReady = true;
            OnPropertyChanged(nameof(Portfolios));
            OnPropertyChanged(nameof(SelectedPortfolio));
            PortfolioStatus = _portfolios.Count == 0 ? "No compatible active portfolio. Create one in Portfolio." : "";
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            if (!_disposed) { OrderSide = ""; PortfolioStatus = "Portfolios unavailable. " + Explain(ex); }
        }
        finally { if (!_disposed) NotifyTrading(); }
    }

    private async Task LoadQuoteAsync()
    {
        QuoteStatus = "Refreshing quote…";
        _usdQuote = null;
        _conversionStatus = "Loading USD conversion…";
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
            if (NeedsConversion)
            {
                try
                {
                    // Frankfurter reste côté backend : aucun appel externe depuis Avalonia.
                    var converted = await GetAsync<UsdQuote>($"api/assets/{Uri.EscapeDataString(Asset.Symbol)}/usd-quote");
                    if (_disposed) return;
                    if (converted.Symbol != Asset.Symbol || converted.OriginalCurrency != Asset.Currency ||
                        converted.OriginalPrice <= 0 || converted.PriceUsd <= 0 || converted.RateToUsd <= 0 ||
                        !DateOnly.TryParse(converted.RateDate, out var rateDate) ||
                        rateDate > DateOnly.FromDateTime(DateTime.UtcNow) ||
                        rateDate < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7))
                        throw new JsonException();
                    _usdQuote = converted;
                    _conversionStatus = "";
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
                {
                    if (!_disposed) _conversionStatus = "USD conversion unavailable. " + Explain(ex);
                }
            }
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
        finally { if (!_disposed) NotifyTrading(); }
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

    private async Task LoadNewsAsync(int page)
    {
        if (_newsBusy || _disposed) return;
        _newsBusy = true;
        RaiseNewsPageCommandState();
        NewsStatus = "Loading news…";
        try
        {
            // Le backend suit les liens de classification en BDD pour cet actif.
            string symbol = Uri.EscapeDataString(Asset.Symbol);
            var result = await GetAsync<NewsResponse>($"api/assets/{symbol}/news?page={page}&page_size=10");
            if (_disposed) return;
            News = result.Items.OrderByDescending(n => n.SortDate).ToList();
            _newsPage = Math.Max(1, result.Page);
            _newsPageCount = Math.Max(1, result.TotalPages);
            UpdateNewsPageNumbers();
            OnPropertyChanged(nameof(NewsPage));
            OnPropertyChanged(nameof(NewsPageCount));
            OnPropertyChanged(nameof(HasMultipleNewsPages));
            OnPropertyChanged(nameof(NewsPageLabel));
            NewsStatus = result.Count == 0 ? "No classified news linked to this asset in the database."
                : "News linked to this asset through classifications stored in the database.";
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            if (!_disposed) { News = Array.Empty<NewsArticle>(); NewsStatus = "News unavailable. " + Explain(ex); }
        }
        finally
        {
            _newsBusy = false;
            RaiseNewsPageCommandState();
        }
    }

    private Task LoadNewsPageAsync(int page) => page < 1 || page > _newsPageCount || page == _newsPage
        ? Task.CompletedTask : LoadNewsAsync(page);

    private void UpdateNewsPageNumbers()
    {
        const int windowSize = 5;
        var start = Math.Clamp(_newsPage - 2, 1, Math.Max(1, _newsPageCount - windowSize + 1));
        NewsPageNumbers.Clear();
        for (var page = start; page < start + windowSize && page <= _newsPageCount; page++)
            NewsPageNumbers.Add(page);
    }

    private void RaiseNewsPageCommandState()
    {
        foreach (var command in new[] { NewsFirstPageCommand, NewsPreviousPageCommand, NewsNextPageCommand, NewsLastPageCommand, NewsGoToPageCommand })
            (command as RelayCommand)?.RaiseCanExecuteChanged();
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
