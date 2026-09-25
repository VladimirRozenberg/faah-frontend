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
Check(vm.CanSubmitOrder && handler.Posts == 0, "preparing order does not send it");
await vm.SubmitOrderAsync();
Check(handler.Posts == 1 && handler.LastPath == "/api/users/42/portfolio/assets/buy", "buy route uses connected user");
Check(handler.LastBody!.Value.GetProperty("quantity").GetDecimal() == 0.25m
    && handler.LastBody.Value.GetProperty("purchase_price").GetDecimal() == 100m, "fractional quantity and purchase price serialized");
Check(vm.OrderStatus.Contains("recorded") && !vm.IsOrderOpen, "success shown only after server confirmation");
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
var selectors = view.GetVisualDescendants().OfType<ComboBox>().ToList();
Check(selectors.Count == 2 && Equals(selectors[0].SelectedItem, "1d"), "period and interval selectors bound in AXAML");
selectors[0].SelectedItem = "1mo";
Dispatcher.UIThread.RunJobs();
Check(vm.SelectedPeriod == "1mo" && Equals(selectors[1].SelectedItem, vm.SelectedInterval), "UI selection updates viewmodel and compatible intervals");
Check(buy.Command == vm.BuyCommand && buy.IsEnabled, "AXAML buy button is bound and enabled");
buy.Command!.Execute(null);
Dispatcher.UIThread.RunJobs();
Check(view.GetVisualDescendants().OfType<NumericUpDown>().Single().IsVisible, "inline confirmation form visible");
using (var bitmap = new RenderTargetBitmap(new PixelSize(1100, 900)))
{
    bitmap.Render(window);
    var screenshot = Path.Combine(Path.GetTempPath(), "faah-asset-detail-check.png");
    bitmap.Save(screenshot);
    Console.WriteLine("SCREENSHOT " + screenshot);
}
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
var loadedSelectors = loadingView.GetVisualDescendants().OfType<ComboBox>().ToList();
Check(Equals(loadedSelectors[0].SelectedItem, "1d") && Equals(loadedSelectors[1].SelectedItem, "5m"), "options loaded after view creation display default selections");
loadingWindow.Close();
using var noUser = new AssetDetailViewModel(http, asset, () => { });
await noUser.RefreshAsync();
Check(!noUser.CanPrepareOrder, "missing user identity blocks orders");
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
    public string LastChartQuery = "";
    private TaskCompletionSource<HttpResponseMessage>? _pendingChart;
    public void CompleteChart() => _pendingChart!.SetResult(Reply(HttpStatusCode.OK, """{"symbol":"BTC-USD","candles":[]}"""));
    public HttpStatusCode TradeStatus = HttpStatusCode.OK;
    private TaskCompletionSource<HttpResponseMessage>? _pending;
    public void CompleteTrade() => _pending!.SetResult(TradeResponse());
    private HttpResponseMessage TradeResponse() => Reply(TradeStatus,
        TradeStatus == HttpStatusCode.OK ? """{"user_id":42,"positions":[{"symbol":"BTC-USD","quantity":0.25}]}"""
        : """{"detail":"Quantite insuffisante."}""");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        if (request.Headers.Authorization?.Parameter != "test-session") throw new Exception("Missing bearer");
        string path = request.RequestUri!.AbsolutePath;
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
