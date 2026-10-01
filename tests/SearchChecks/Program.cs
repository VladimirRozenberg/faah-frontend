using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAAH_Frontend.ViewModels;
using FAAH_Frontend.Views;

AppBuilder.Configure<FAAH_Frontend.App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
void PumpUntil(Func<bool> done)
{
    // Une vraie boucle Avalonia est nécessaire pour déclencher les DispatcherTimer.
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
    check.Tick += (_, _) => { if (done()) timeout.Cancel(); };
    check.Start();
    try { Dispatcher.UIThread.MainLoop(timeout.Token); }
    catch (OperationCanceledException) { }
    finally { check.Stop(); }
    Check(done(), "asynchronous search completed");
}
using var handler = new SearchApi();
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test/") };
using var vm = new AssetListViewModel(http, _ => { });
await vm.RefreshAsync();
Check(vm.Assets.Count == 20 && vm.PageCount == 3, "initial paginated catalogue");
vm.SearchText = "  apple  ";
await vm.SearchAsync();
Check(handler.Search == "apple" && handler.Page == 1 && vm.Assets.Single().Symbol == "AAPL", "search by name finds asset outside original page");
vm.SearchText = "aApL";
await vm.SearchAsync();
Check(vm.Assets.Single().Name == "Apple Inc.", "symbol query sent to server");
vm.SearchText = "Company";
await vm.SearchAsync();
vm.NextPageCommand.Execute(null);
Check(handler.Page == 2 && handler.Search == "Company" && vm.Assets[0].Id == 21, "pagination preserves applied search");
vm.SearchText = "draft not submitted";
await vm.RefreshAsync();
Check(handler.Search == "Company" && handler.Page == 2, "refresh ignores unfinished draft and retains applied search");
vm.SearchText = "absent";
await vm.SearchAsync();
Check(handler.Page == 1 && vm.IsEmpty && vm.PageCount == 1, "new search resets page and handles zero matches");
vm.SearchText = "A&B + %_";
await vm.SearchAsync();
Check(handler.Search == "A&B + %_", "special characters encoded as one query value");
await vm.SearchAsync(clear: true);
Check(vm.SearchText == "" && handler.Search == "" && vm.Assets.Count == 20, "clear returns full catalogue");
handler.Fail = true;
vm.SearchText = "apple";
await vm.SearchAsync();
Check(vm.HasError && !vm.IsBusy && vm.SearchCommand.CanExecute(null), "network failure permits retry");
handler.Fail = false;
await vm.SearchAsync();
Check(vm.Assets.Count == 1 && !vm.HasError, "retry succeeds");

handler.IgnoreSearch = true;
vm.SearchText = "BTC";
await vm.SearchAsync();
Check(vm.HasError && vm.Error.Contains("Deploy") && vm.Assets.Count == 0, "old backend ignoring filter shows deployment error, not all assets");
handler.IgnoreSearch = false;
handler.HoldNext = true;
vm.SearchText = "Company";
var oldSearch = vm.SearchAsync();
vm.SearchText = "apple";
await vm.SearchAsync(); // Pendant l'appel précédent : mise en attente du dernier texte.
Check(vm.Assets.Count == 0, "obsolete results cleared while typing");
handler.Complete();
PumpUntil(() => oldSearch.IsCompleted && !vm.IsBusy && vm.Assets.Count == 1);
Check(vm.Assets[0].Symbol == "AAPL" && handler.Search == "apple", "late old response discarded and pending search runs");

var view = new AssetListView { DataContext = vm };
var window = new Window { Content = view, Width = 1100, Height = 700 };
window.Show(); Dispatcher.UIThread.RunJobs();
var searchBox = view.FindControl<TextBox>("AssetSearchBox")!;
searchBox.Text = "Company";
Dispatcher.UIThread.RunJobs();
var button = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Search"));
Check(vm.SearchText == "Company" && button.Command == vm.SearchCommand, "real AXAML text and search button bindings");
button.Command!.Execute(null);
Check(handler.Search == "Company", "UI search action reaches API");
int beforeTyping = handler.Calls;
searchBox.Text = "a";
searchBox.Text = "ap";
searchBox.Text = "apple";
PumpUntil(() => handler.Search == "apple" && vm.Assets.Count == 1);
Check(handler.Calls == beforeTyping + 2, "typing sends one debounced search plus favorites, without pressing Search");
Check(!view.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Clear")), "Clear button removed");
Check(view.FindControl<Border>("FiltersPanel")!.GetVisualDescendants().OfType<CheckBox>().Any(), "favorites moved into filters panel");
await vm.SearchAsync(clear: true);
var typeBox = view.FindControl<ComboBox>("AssetTypeFilterBox")!;
typeBox.SelectedItem = "Crypto";
Dispatcher.UIThread.RunJobs();
Check(handler.Filters.GetValueOrDefault("asset_type") == "crypto" && vm.Assets.All(a => a.Type == "crypto"), "real type selector filters across the catalogue");
vm.NextPageCommand.Execute(null);
Check(handler.Page == 2 && vm.Assets.Count == 2 && handler.Filters["asset_type"] == "crypto", "pagination retains type filter");
vm.CurrencyFilter = "usd";
vm.CountryFilter = "uni";
vm.ExchangeFilter = "nyq";
await vm.SearchAsync();
Check(handler.Page == 1 && handler.Filters["currency"] == "USD" && handler.Filters["exchange"] == "NYQ"
    && handler.Filters["country"] == "uni" && vm.Assets.Count == 20, "partial text filters encoded and currency/exchange normalized");
vm.FavoritesOnly = true;
Check(handler.Filters["favorites_only"] == "true" && vm.Assets.Count == 2, "favorites combine with other filters");
vm.SearchText = "Company 2";
await vm.SearchAsync();
Check(vm.Assets.Single().Id == 2 && handler.Filters["asset_type"] == "crypto", "main search combines with sidebar filters");
Check(!vm.ShowSectorFilter, "niches hidden for crypto");
vm.AssetTypeFilter = "Stocks";
await vm.LoadNichesAsync();
vm.NicheSearch = "en";
Check(vm.NicheOptions.Any(n => n.Name == "Energy") && vm.NicheOptions.All(n => n.Id == 0 || n.Name == "Energy"), "partial niche name filters dropdown");
vm.SelectedNiche = vm.NicheOptions.First(n => n.Id == 1);
await vm.SearchAsync();
Check(vm.ShowSectorFilter && handler.Filters["niche_id"] == "1", "selected niche sent as database ID for stocks");
vm.AssetTypeFilter = "Crypto";
Check(!handler.Filters.ContainsKey("niche_id"), "hidden niche does not filter other asset types");
vm.CountryFilter = "A&B + %_";
await vm.SearchAsync();
Check(handler.Filters["country"] == "A&B + %_" && vm.IsEmpty, "filter special characters remain one parameter and empty results handled");
vm.CurrencyFilter = vm.CountryFilter = vm.ExchangeFilter = "";
vm.SelectedNiche = null;
vm.FavoritesOnly = false;
await vm.SearchAsync(clear: true);
handler.HoldNext = true;
vm.AssetTypeFilter = "Stocks";
vm.AssetTypeFilter = "Crypto";
Check(vm.Assets.Count == 0, "filter changes hide stale rows while request pending");
handler.Complete();
PumpUntil(() => !vm.IsBusy && vm.Assets.Count > 0);
Check(handler.Filters["asset_type"] == "crypto" && vm.Assets.All(a => a.Type == "crypto"), "late filter response discarded and newest selection loaded");
vm.AssetTypeFilter = "Stocks";
vm.NicheSearch = "";
await vm.SearchAsync();
foreach (int width in new[] { 1100, 1920, 900 })
{
    window.Width = width;
    window.Height = 800;
    Dispatcher.UIThread.RunJobs();
    Check(Grid.GetColumn(view.FindControl<Border>("FiltersPanel")!) == (width >= 1050 ? 1 : 0), "filters layout at width " + width);
    Check(view.FindControl<TextBox>("AssetSearchBox")!.Bounds.Width <= 850, "search bar has limited width");
    Check(view.FindControl<StackPanel>("NicheFilterPanel")!.IsVisible, "stocks show niche menu in real view");
    using var screenshot = new RenderTargetBitmap(new PixelSize(width, 800));
    screenshot.Render(window);
    screenshot.Save(Path.Combine(Path.GetTempPath(), $"faah-asset-filters-{width}.png"));
}
Check(!view.GetVisualDescendants().OfType<Expander>().Any(), "filters cannot be collapsed");
Dispatcher.UIThread.RunJobs();
Check(view.FindControl<ScrollViewer>("FilterScroll")!.Bounds.Height <= 220, "fixed narrow filters leave room for asset rows");
window.Close();
vm.SearchText = "Company"; // Un timer en attente doit être arrêté à la fermeture.
vm.Dispose();
int calls = handler.Calls;
await vm.SearchAsync();
Check(calls == handler.Calls, "disposed page makes no requests");

class SearchApi : HttpMessageHandler
{
    public string Search = "";
    public Dictionary<string, string> Filters = new();
    public int Page, Calls;
    public bool Fail, IgnoreSearch, HoldNext;
    private TaskCompletionSource<HttpResponseMessage>? _pending;
    private HttpResponseMessage? _heldResponse;
    public void Complete() => _pending!.SetResult(_heldResponse!);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        if (request.RequestUri!.AbsolutePath == "/api/favorites") return Reply(new { asset_ids = new[] { 2, 4 } });
        if (request.RequestUri.AbsolutePath == "/api/niches") return Reply(new { items = new[] { new { id = 1, name = "Energy" }, new { id = 2, name = "Software" } } });
        if (request.RequestUri.AbsolutePath != "/api/assets") throw new Exception("Unexpected route");
        var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Search = query.GetValueOrDefault("search", "");
        Filters = query;
        Page = int.Parse(query["page"]);
        int size = int.Parse(query["page_size"]);
        var all = Enumerable.Range(1, 45).Select(i => new { id = i, symbol = i == 45 ? "AAPL" : "X" + i, name = i == 45 ? "Apple Inc." : "Company " + i,
            type = i % 2 == 0 ? "crypto" : "stock", currency = "USD", country = "United States", exchange = "NYQ", sector = "Technology",
            market = new { last_price = 123.45m, previous_close = 122m, change = 1.45m, change_percent = 1.19m, currency = "USD", volume = 1234567 } });
        var filtered = all.Where(a => (IgnoreSearch || a.symbol.Contains(Search, StringComparison.OrdinalIgnoreCase) || a.name.Contains(Search, StringComparison.OrdinalIgnoreCase))
            && (!query.ContainsKey("asset_type") || a.type == query["asset_type"])
            && (!query.ContainsKey("currency") || a.currency.Contains(query["currency"], StringComparison.OrdinalIgnoreCase))
            && (!query.ContainsKey("country") || a.country.Contains(query["country"], StringComparison.OrdinalIgnoreCase))
            && (!query.ContainsKey("exchange") || a.exchange.Contains(query["exchange"], StringComparison.OrdinalIgnoreCase))
            && (!query.ContainsKey("sector") || a.sector == query["sector"])
            && (!query.ContainsKey("favorites_only") || a.id == 2 || a.id == 4)).ToList();
        var response = Reply(new { count = filtered.Count, page = Page, page_size = size, items = filtered.Skip((Page - 1) * size).Take(size) });
        if (HoldNext) { HoldNext = false; _heldResponse = response.Result; _pending = new(); return _pending.Task; }
        return response;
    }
    private static Task<HttpResponseMessage> Reply(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) });
}
