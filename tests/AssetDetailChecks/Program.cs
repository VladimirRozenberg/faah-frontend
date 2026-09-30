using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAAH_Frontend.Models;
using FAAH_Frontend.ViewModels;
using FAAH_Frontend.Views;

AppBuilder.Configure<FAAH_Frontend.App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine("PASS " + label);
}

var handler = new FakeApi();
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
http.DefaultRequestHeaders.Authorization = new("Bearer", "test-session");
var asset = new Asset { Id = 7, Symbol = "BTC-USD", Name = "Bitcoin USD", Type = "crypto", Currency = "USD" };
bool returned = false;
using var vm = new AssetDetailViewModel(http, asset, () => returned = true, 42);
await vm.RefreshAsync();
Check(handler.DetailIds.SequenceEqual(new[] { 10, 11, 12 }), "summary list followed by details; paused and non-USD portfolios skipped");
Check(vm.Portfolios.Select(p => p.Id).SequenceEqual(new[] { 10, 12 }), "only compatible active USD portfolios; empty preferences allow all types");
Check(vm.SelectedPortfolio is null && !vm.CanBuy && !vm.CanSell, "no portfolio auto-selected; buy and sell disabled");
vm.BuyCommand.Execute(null);
Check(!vm.IsOrderOpen, "buy command also refuses missing selection");
vm.SelectedPortfolio = vm.Portfolios[0];
Check(vm.CanBuy && !vm.CanSell, "compatible empty portfolio permits buy but not sell");
vm.SellCommand.Execute(null);
Check(!vm.IsOrderOpen, "sell command refuses an asset not held");
vm.BuyCommand.Execute(null);
vm.SelectedPortfolio = vm.Portfolios[1];
Check(!vm.IsOrderOpen && vm.CanSell && vm.HeldQuantity == 2, "switching portfolio closes order and updates held quantity");
vm.SellCommand.Execute(null);
vm.Quantity = 3;
Check(!vm.CanSubmitOrder, "sale exceeding holdings disabled");
vm.SelectedPortfolio = vm.Portfolios[0];
Check(vm.Asset.Price == 100 && vm.Candles.Count == 40, "market and candles decoded from actual backend formats");
Check(vm.Asset.Market?.LastPrice == 100 && vm.Asset.PriceDisplay.Contains("100"), "detail refresh updates the shared market display model");
Check(vm.News.Count == 1 && vm.News[0].Id == 1, "classified news from asset endpoint displayed even without symbol or name in text");
Check(vm.Periods.Contains("1mo") && vm.SelectedPeriod == "1d" && vm.SelectedInterval == "5m", "history options come from backend with initial 1d/5m selection");
vm.SelectedPeriod = "1mo";
Check(vm.SelectedInterval == "30m" && !vm.Intervals.Contains("5m"), "period change replaces incompatible interval");
vm.SelectedInterval = "1d";
Check(handler.LastChartQuery == "?period=1mo&interval=1d" && vm.ChartStatus.Contains("1mo"), "selected values sent to backend and shown in chart status");
int requestsBeforeInvalidSelection = handler.Calls;
vm.SelectedInterval = "5m";
Check(handler.Calls == requestsBeforeInvalidSelection && vm.SelectedInterval == "1d", "invalid interval is not sent");
handler.HoldNextChart = true;
var oldRefresh = vm.RefreshAsync();
vm.SelectedPeriod = "1d";
vm.SelectedInterval = "5m";
handler.CompleteChart();
var chartDeadline = DateTime.UtcNow.AddSeconds(5);
while (!oldRefresh.IsCompleted && DateTime.UtcNow < chartDeadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
Check(oldRefresh.IsCompleted, "delayed chart response completed");
await oldRefresh;
Check(vm.Candles.Count == 40 && vm.ChartStatus.Contains("candles: 5m"), "late old response cannot overwrite the new selection");
Check(vm.CanPrepareOrder && !vm.CanSubmitOrder, "fresh quote enables ticket, not submission without ticket");
vm.BuyCommand.Execute(null);
vm.Quantity = 0;
Check(!vm.CanSubmitOrder, "zero quantity rejected");
vm.Quantity = 0.25m;
Check(vm.ConfirmOrderLabel == "Confirm buy" && vm.OrderTitle == "Buy BTC-USD"
    && vm.OrderTotal == $"{25m:N2} USD", "buy ticket labels and estimated total follow quantity");
Check(vm.CanSubmitOrder && handler.Posts == 0, "preparing order does not send it");
await vm.SubmitOrderAsync();
Check(handler.Posts == 1 && handler.LastPath == "/api/users/42/portfolios/10/assets/buy", "buy route targets connected user and selected portfolio");
Check(handler.LastBody!.Value.GetProperty("quantity").GetDecimal() == 0.25m
    && handler.LastBody.Value.GetProperty("purchase_price").GetDecimal() == 100m, "fractional quantity and purchase price serialized");
Check(vm.OrderStatus.Contains("recorded") && !vm.IsOrderOpen, "success shown only after server confirmation");
Check(vm.CanSell && vm.HeldQuantity == 0.25m, "buy response enables sell using server holdings");
vm.SellCommand.Execute(null);
await vm.SubmitOrderAsync();
Check(handler.LastBody!.Value.TryGetProperty("sale_price", out _) && handler.LastPath.EndsWith("/sell"), "sell contract uses sale_price");
handler.TradeStatus = HttpStatusCode.BadRequest;
vm.SellCommand.Execute(null);
await vm.SubmitOrderAsync();
Check(vm.OrderStatus == "Quantite insuffisante." && vm.IsOrderOpen, "backend refusal displayed without false success");
handler.TradeStatus = HttpStatusCode.OK;
handler.DelayTrade = true;
var firstPost = vm.SubmitOrderAsync();
int sent = handler.Posts;
vm.SelectedPortfolio = vm.Portfolios[1];
Check(vm.SelectedPortfolio!.Id == 10 && !vm.CanSelectPortfolio, "portfolio cannot change while order is being submitted");
await vm.SubmitOrderAsync();
Check(handler.Posts == sent && vm.IsSubmitting, "double click does not send two orders");
handler.CompleteTrade();
var deadline = DateTime.UtcNow.AddSeconds(5);
while (!firstPost.IsCompleted && DateTime.UtcNow < deadline)
{
    Dispatcher.UIThread.RunJobs();
    Thread.Sleep(1);
}
Check(firstPost.IsCompleted, "delayed response completes on UI dispatcher");
await firstPost;
handler.DelayTrade = false;
handler.FailChart = true;
await vm.RefreshAsync();
Check(vm.Candles.Count == 0 && vm.News.Count == 1 && vm.ChartStatus.Contains("indisponible"), "chart failure leaves other blocks usable");
handler.FailChart = false;
handler.MalformedCandles = true;
await vm.RefreshAsync();
Check(vm.Candles.Count == 0 && vm.ChartStatus.Contains("format"), "invalid OHLC values rejected");
handler.MalformedCandles = false;
handler.FailNews = true;
await vm.RefreshAsync();
Check(vm.News.Count == 0 && vm.NewsStatus.Contains("unavailable") && vm.Candles.Count > 0,
    "news endpoint failure shown without text-search fallback or broken chart");
handler.FailNews = false;
handler.Empty = true;
await vm.RefreshAsync();
Check(vm.Candles.Count == 0 && vm.News.Count == 0, "empty API results handled");
handler.Empty = false;
await vm.RefreshAsync();
vm.BackCommand.Execute(null);
Check(returned, "back command returns to list");

// Charger les vrais fichiers AXAML et verifier les liaisons sans ouvrir une fenetre sur le bureau.
var view = new AssetDetailView { DataContext = vm };
var window = new Window { Width = 1100, Height = 900, Content = view };
window.Show();
Dispatcher.UIThread.RunJobs();
var buy = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Buy"));
var selectors = view.GetVisualDescendants().OfType<ComboBox>().Where(c => c.Name != "TradePortfolioSelector").ToList();
Check(selectors.Count == 2 && Equals(selectors[0].SelectedItem, "1d"), "period and interval selectors bound in AXAML");
selectors[0].SelectedItem = "1mo";
Dispatcher.UIThread.RunJobs();
Check(vm.SelectedPeriod == "1mo" && Equals(selectors[1].SelectedItem, vm.SelectedInterval), "UI selection updates viewmodel and compatible intervals");
Check(buy.Command == vm.BuyCommand && buy.IsEnabled, "AXAML buy button is bound and enabled");
var portfolioSelector = view.FindControl<ComboBox>("TradePortfolioSelector")!;
portfolioSelector.SelectedItem = null;
Dispatcher.UIThread.RunJobs();
Check(!buy.IsEnabled && !vm.CanSell, "clearing real selector disables trading buttons");
Check(!view.FindControl<Border>("HoldingsCard")!.IsVisible, "holdings card hidden without portfolio");
using (var disabledImage = new RenderTargetBitmap(new PixelSize(1100, 900)))
{
    disabledImage.Render(window);
    disabledImage.Save(Path.Combine(Path.GetTempPath(), "faah-portfolio-selection.png"));
}
portfolioSelector.SelectedItem = vm.Portfolios[0];
Dispatcher.UIThread.RunJobs();
Check(buy.IsEnabled && vm.SelectedPortfolio!.Id == 10, "real selector binding enables buy");
Check(view.FindControl<Border>("HoldingsCard")!.IsVisible && vm.HoldingsDisplay == "0 BTC-USD", "compact holdings card shows zero quantity for selected portfolio");
Check(!vm.HasTradingNotice, "routine simulation notice removed; warning messages remain available");
buy.Command!.Execute(null);
Dispatcher.UIThread.RunJobs();
Check(view.GetVisualDescendants().OfType<NumericUpDown>().Single().IsVisible, "inline confirmation form visible");
Check(view.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Confirm buy")), "styled confirmation button bound to buy action");
// Vérifier le plein écran et une fenêtre réduite avec le même formulaire ouvert.
window.Width = 1920;
window.Height = 1080;
Dispatcher.UIThread.RunJobs();
Check(Grid.GetColumn(view.FindControl<StackPanel>("TradingPanel")!) == 1, "wide layout places trading beside asset summary");
Check(Grid.GetColumn(view.FindControl<StackPanel>("OrderActions")!) == 3
    && view.FindControl<Border>("OrderCard")!.Bounds.Height < 240, "wide order is a compact horizontal panel");
Check(view.GetVisualDescendants().OfType<FAAH_Frontend.Controls.CandleChart>().Single().Bounds.Width > 1700,
    "chart keeps full page width on large screens");
using (var wideImage = new RenderTargetBitmap(new PixelSize(1920, 1080)))
{
    wideImage.Render(window);
    wideImage.Save(Path.Combine(Path.GetTempPath(), "faah-order-wide.png"));
}
window.Width = 900;
Dispatcher.UIThread.RunJobs();
Check(Grid.GetRow(view.FindControl<StackPanel>("TradingPanel")!) == 1, "narrow layout stacks trading below asset summary");
Check(Grid.GetRow(view.FindControl<StackPanel>("OrderActions")!) == 1, "medium order uses two rows");
using (var mediumImage = new RenderTargetBitmap(new PixelSize(900, 1080)))
{
    mediumImage.Render(window);
    mediumImage.Save(Path.Combine(Path.GetTempPath(), "faah-order-medium.png"));
}
window.Width = 600;
Dispatcher.UIThread.RunJobs();
Check(Grid.GetRow(view.FindControl<StackPanel>("OrderActions")!) == 3, "small order stacks all sections");
Check(view.FindControl<Border>("OrderCard")!.Bounds.Width <= 560, "small order fits available width");
using (var smallImage = new RenderTargetBitmap(new PixelSize(600, 1080)))
{
    smallImage.Render(window);
    smallImage.Save(Path.Combine(Path.GetTempPath(), "faah-order-small.png"));
}
window.Width = 1100;
window.Height = 900;
Dispatcher.UIThread.RunJobs();
using (var bitmap = new RenderTargetBitmap(new PixelSize(1100, 900)))
{
    bitmap.Render(window);
    var screenshot = Path.Combine(Path.GetTempPath(), "faah-asset-detail-check.png");
    bitmap.Save(screenshot);
    Console.WriteLine("SCREENSHOT " + screenshot);
}
vm.SelectedPortfolio = vm.Portfolios[1];
vm.SellCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Check(vm.ConfirmOrderLabel == "Confirm sell" && !vm.IsBuyOrder, "sell ticket has its own label and colour");
using (var sellImage = new RenderTargetBitmap(new PixelSize(1100, 900)))
{
    sellImage.Render(window);
    sellImage.Save(Path.Combine(Path.GetTempPath(), "faah-order-sell.png"));
}
vm.SelectedPortfolio = vm.Portfolios[0];
vm.CloseOrderCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
using (var bitmap = new RenderTargetBitmap(new PixelSize(1100, 900)))
{
    bitmap.Render(window);
    bitmap.Save(Path.Combine(Path.GetTempPath(), "faah-asset-detail-overview.png"));
}
window.Width = 900;
window.Height = 600;
Dispatcher.UIThread.RunJobs();
var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
scroll.Offset = new Vector(0, scroll.Extent.Height);
Dispatcher.UIThread.RunJobs();
using (var bitmap = new RenderTargetBitmap(new PixelSize(900, 600)))
{
    bitmap.Render(window);
    bitmap.Save(Path.Combine(Path.GetTempPath(), "faah-asset-detail-news.png"));
}
window.Close();

Asset? opened = null;
using var list = new AssetListViewModel(http, selected => opened = selected);
list.Assets.Add(asset);
var listView = new AssetListView { DataContext = list };
var listWindow = new Window { Width = 1100, Height = 650, Content = listView };
listWindow.Show();
Dispatcher.UIThread.RunJobs();
var rowButton = listView.GetVisualDescendants().OfType<Button>().Single(b => b.Command == list.OpenAssetCommand);
rowButton.Command!.Execute(rowButton.CommandParameter);
Check(ReferenceEquals(opened, asset), "asset row passes selected asset to detail callback");
Check(listView.GetVisualDescendants().OfType<Button>().Any(b => b.Command == list.ToggleFavoriteCommand), "favorite remains separate from navigation");
listWindow.Close();

handler.TradeStatus = HttpStatusCode.InternalServerError;
vm.BuyCommand.Execute(null);
await vm.SubmitOrderAsync();
Check(!vm.CanPrepareOrder && vm.OrderStatus.Contains("peut-etre"), "uncertain result blocks immediate retry");
using var euro = new AssetDetailViewModel(http, new Asset { Id = 8, Symbol = "BTC-USD", Currency = "EUR" }, () => { }, 42);
Check(!euro.CanPrepareOrder, "no submission without fresh quote");
handler.Currency = "EUR";
await euro.RefreshAsync();
Check(!euro.CanPrepareOrder && euro.TradingNotice.Contains("USD"), "non-USD trading blocked for current backend");
handler.Currency = "USD";
handler.FailOptions = true;
using var optionsFailure = new AssetDetailViewModel(http, asset, () => { });
await optionsFailure.RefreshAsync();
Check(!optionsFailure.HasHistoryOptions && optionsFailure.ChartStatus.Contains("unavailable") && optionsFailure.News.Count == 1, "options failure does not break other blocks");
handler.FailOptions = false;
var loadingView = new AssetDetailView { DataContext = optionsFailure };
var loadingWindow = new Window { Content = loadingView, Width = 1000, Height = 700 };
loadingWindow.Show();
Dispatcher.UIThread.RunJobs();
await optionsFailure.RefreshAsync();
Dispatcher.UIThread.RunJobs();
Check(optionsFailure.HasHistoryOptions && optionsFailure.Candles.Count == 40, "refresh retries failed options request");
var loadedSelectors = loadingView.GetVisualDescendants().OfType<ComboBox>().Where(c => c.Name != "TradePortfolioSelector").ToList();
Check(Equals(loadedSelectors[0].SelectedItem, "1d") && Equals(loadedSelectors[1].SelectedItem, "5m"), "options loaded after view creation display default selections");
loadingWindow.Close();
using var noUser = new AssetDetailViewModel(http, asset, () => { });
await noUser.RefreshAsync();
Check(!noUser.CanPrepareOrder, "missing user identity blocks orders");
handler.TradeStatus = HttpStatusCode.OK;
using var selectionChecks = new AssetDetailViewModel(http, asset, () => { }, 42);
await selectionChecks.RefreshAsync();
selectionChecks.SelectedPortfolio = selectionChecks.Portfolios[0];
handler.EmptyPositions = true;
selectionChecks.BuyCommand.Execute(null);
await selectionChecks.SubmitOrderAsync();
Check(!selectionChecks.CanSell && selectionChecks.HeldQuantity == 0, "zero holdings in response disables sell immediately");
handler.EmptyPositions = false;
handler.WrongPortfolio = true;
selectionChecks.BuyCommand.Execute(null);
await selectionChecks.SubmitOrderAsync();
Check(!selectionChecks.CanBuy && selectionChecks.OrderStatus.Contains("peut-etre"), "response for wrong portfolio treated as uncertain");
handler.WrongPortfolio = false;
using var unavailable = new AssetDetailViewModel(http, asset, () => { }, 42);
handler.FailPortfolios = true;
await unavailable.RefreshAsync();
Check(!unavailable.CanBuy && !unavailable.CanSell && unavailable.Candles.Count > 0, "portfolio outage blocks trading only");
handler.FailPortfolios = false;
await unavailable.RefreshAsync();
unavailable.SelectedPortfolio = unavailable.Portfolios[0];
Check(unavailable.CanBuy, "refresh recovers portfolio load");
handler.FailPortfolioDetail = true;
await unavailable.RefreshAsync();
Check(!unavailable.CanBuy && !unavailable.CanSell && unavailable.PortfolioStatus.Contains("unavailable"), "detail failure blocks trading instead of guessing holdings");
handler.FailPortfolioDetail = false;
handler.WrongDetailOwner = true;
await unavailable.RefreshAsync();
Check(!unavailable.CanBuy && unavailable.PortfolioStatus.Contains("format"), "detail for another user rejected");
handler.WrongDetailOwner = false;
await unavailable.RefreshAsync();
Check(unavailable.SelectedPortfolio?.Id == 10 && unavailable.CanBuy, "refresh recovers details and preserves selected portfolio");
handler.NoPortfolios = true;
await unavailable.RefreshAsync();
Check(unavailable.SelectedPortfolio is null && !unavailable.CanBuy && unavailable.PortfolioStatus.Contains("No compatible"), "removed portfolio clears selection and disables trading");
handler.NoPortfolios = false;
vm.Dispose();
int calls = handler.Calls;
await vm.RefreshAsync();
Check(handler.Calls == calls, "leaving detail stops API calls");

class FakeApi : HttpMessageHandler
{
    public int Posts, Calls;
    public string LastPath = "", Currency = "USD";
    public JsonElement? LastBody;
    public bool FailChart, MalformedCandles, Empty, DelayTrade;
    public bool FailOptions, HoldNextChart, FailNews;
    public bool EmptyPositions, WrongPortfolio, FailPortfolios, NoPortfolios;
    public bool FailPortfolioDetail, WrongDetailOwner;
    public List<int> DetailIds = new();
    public string LastChartQuery = "";
    private TaskCompletionSource<HttpResponseMessage>? _pendingChart;
    public void CompleteChart() => _pendingChart!.SetResult(Reply(HttpStatusCode.OK, """{"symbol":"BTC-USD","candles":[]}"""));
    public HttpStatusCode TradeStatus = HttpStatusCode.OK;
    private TaskCompletionSource<HttpResponseMessage>? _pending;
    public void CompleteTrade() => _pending!.SetResult(TradeResponse());
    private HttpResponseMessage TradeResponse() => Reply(TradeStatus,
        TradeStatus == HttpStatusCode.OK ? JsonSerializer.Serialize(new { id = WrongPortfolio ? 999 : 10, user_id = 42,
            positions = EmptyPositions ? Array.Empty<object>() : new object[] { new { symbol = "BTC-USD", quantity = 0.25m } } })
        : """{"detail":"Quantite insuffisante."}""");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        if (request.Headers.Authorization?.Parameter != "test-session") throw new Exception("Missing bearer");
        string path = request.RequestUri!.AbsolutePath;
        if (path == "/api/users/42/portfolios") return FailPortfolios ? Reply(HttpStatusCode.ServiceUnavailable, "{}")
            : Reply(HttpStatusCode.OK, NoPortfolios ? "{\"items\":[]}" : """
            {"items":[
              {"portfolio_id":10,"name":"Crypto","status":"active","base_currency":"USD"},
              {"portfolio_id":11,"name":"Stocks","status":"active","base_currency":"USD"},
              {"portfolio_id":12,"name":"Mixed","status":"active","base_currency":"USD"},
              {"portfolio_id":13,"name":"Paused","status":"paused","base_currency":"USD"},
              {"portfolio_id":14,"name":"EUR","status":"active","base_currency":"EUR"}
            ]}
            """);
        if (request.Method == HttpMethod.Get && path.StartsWith("/api/users/42/portfolios/"))
        {
            int id = int.Parse(path.Split('/').Last());
            DetailIds.Add(id);
            return FailPortfolioDetail ? Reply(HttpStatusCode.ServiceUnavailable, "{}")
                : Reply(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    id, user_id = WrongDetailOwner ? 99 : 42,
                    name = id == 10 ? "Crypto" : id == 11 ? "Stocks" : "Mixed",
                    is_active = true, base_currency = "USD",
                    preferred_asset_types = id == 12 ? Array.Empty<string>() : new[] { id == 10 ? "crypto" : "stock" },
                    positions = id == 12 ? new object[] { new { symbol = "BTC-USD", quantity = 2 } } : Array.Empty<object>()
                }));
        }
        if (path == "/api/history-options") return FailOptions ? Reply(HttpStatusCode.ServiceUnavailable, "{}")
            : Reply(HttpStatusCode.OK, """{"1d":["1m","5m","1h"],"5d":["5m","1h"],"1mo":["30m","1h","1d"]}""");
        if (request.Method == HttpMethod.Post)
        {
            Posts++;
            LastPath = path;
            LastBody = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(token));
            if (DelayTrade) { _pending = new(); return await _pending.Task; }
            return TradeResponse();
        }
        if (path.EndsWith("/market")) return Reply(HttpStatusCode.OK, JsonSerializer.Serialize(new { symbol = "BTC-USD", last_price = 100m, change_percent = 2m, volume = 1000, currency = Currency }));
        if (path.EndsWith("/candles"))
        {
            LastChartQuery = request.RequestUri.Query;
            if (HoldNextChart) { HoldNextChart = false; _pendingChart = new(); return await _pendingChart.Task; }
            if (FailChart) return Reply(HttpStatusCode.NotFound, "{}");
            var candles = Enumerable.Range(0, Empty ? 0 : 40).Select(i => new
            {
                timestamp = DateTimeOffset.Parse("2026-09-21T10:00:00Z").AddMinutes(i * 5),
                open = 100 + Math.Sin(i) * 3, close = 100 + Math.Cos(i) * 3,
                high = MalformedCandles ? 1 : 105, low = 95, volume = 100
            });
            return Reply(HttpStatusCode.OK, JsonSerializer.Serialize(new { symbol = "BTC-USD", candles }));
        }
        if (path == "/api/assets/BTC-USD/news") return FailNews ? Reply(HttpStatusCode.NotFound, "{}")
            : Reply(HttpStatusCode.OK, Empty ? "{\"items\":[]}" : """{"count":1,"items":[{"src_id":1,"src_title":"Une évolution du secteur","src_content":"Liée par classification, sans mention du symbole ni du nom.","src_original_url":"https://example.test/article","src_published_at":"2026-09-21T10:00:00Z"}]}""");
        throw new Exception("Unexpected route " + path);
    }
    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
}
