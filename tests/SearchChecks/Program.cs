using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
using var vm = new AssetListViewModel(http);
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
window.Close();
vm.SearchText = "Company"; // Un timer en attente doit être arrêté à la fermeture.
vm.Dispose();
int calls = handler.Calls;
await vm.SearchAsync();
Check(calls == handler.Calls, "disposed page makes no requests");

class SearchApi : HttpMessageHandler
{
    public string Search = "";
    public int Page, Calls;
    public bool Fail, IgnoreSearch, HoldNext;
    private TaskCompletionSource<HttpResponseMessage>? _pending;
    private HttpResponseMessage? _heldResponse;
    public void Complete() => _pending!.SetResult(_heldResponse!);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        if (request.RequestUri!.AbsolutePath == "/api/favorites") return Reply(new { asset_ids = Array.Empty<int>() });
        if (request.RequestUri.AbsolutePath != "/api/assets") throw new Exception("Unexpected route");
        var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Search = query.GetValueOrDefault("search", "");
        Page = int.Parse(query["page"]);
        int size = int.Parse(query["page_size"]);
        var all = Enumerable.Range(1, 45).Select(i => new { id = i, symbol = i == 45 ? "AAPL" : "X" + i, name = i == 45 ? "Apple Inc." : "Company " + i });
        var filtered = all.Where(a => IgnoreSearch || a.symbol.Contains(Search, StringComparison.OrdinalIgnoreCase) || a.name.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
        var response = Reply(new { count = filtered.Count, page = Page, page_size = size, items = filtered.Skip((Page - 1) * size).Take(size) });
        if (HoldNext) { HoldNext = false; _heldResponse = response.Result; _pending = new(); return _pending.Task; }
        return response;
    }
    private static Task<HttpResponseMessage> Reply(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) });
}
