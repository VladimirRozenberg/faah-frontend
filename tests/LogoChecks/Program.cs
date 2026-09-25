using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAAH_Frontend.Models;
using FAAH_Frontend.Services;
using FAAH_Frontend.ViewModels;
using FAAH_Frontend.Views;

AppBuilder.Configure<FAAH_Frontend.App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
void Check(bool value, string label)
{
    if (!value) throw new Exception(label);
    Console.WriteLine("PASS " + label);
}
void Finish(Task task)
{
    var end = DateTime.UtcNow.AddSeconds(10);
    while (!task.IsCompleted && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
    if (!task.IsCompleted) throw new Exception("Timeout");
    task.GetAwaiter().GetResult();
}

// Image fabriquée uniquement pour les tests, aucun appel réel à Twelve Data.
using var pixels = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96));
using var png = new MemoryStream();
pixels.Save(png);
var handler = new LogoApi(png.ToArray());
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://backend.test/") };
http.DefaultRequestHeaders.Authorization = new("Bearer", "test-token");
var logos = new AssetLogoService(http);
using var list = new AssetListViewModel(http, logos: logos);
Finish(list.RefreshAsync());
Finish(list.LoadVisibleLogosAsync());
Check(handler.LogoCalls == 9, "only first page logos fetched; null logo_url skipped");
Check(list.Assets[0].HasLogo && list.Assets[0].Logo!.PixelSize.Width == 64, "logo_url decoded and bitmap resized");
Check(!list.Assets[1].HasLogo && list.Assets[1].IconText == "A2", "missing logo keeps initials");
Check(!list.Assets[2].HasLogo && !list.Assets[3].HasLogo && !list.Assets[4].HasLogo, "404, invalid image and HTML keep initials");
var firstImage = list.Assets[0].Logo;
Finish(list.RefreshAsync());
Finish(list.LoadVisibleLogosAsync());
Check(handler.LogoCalls == 9 && ReferenceEquals(firstImage, list.Assets[0].Logo), "refresh reuses cache including recent failures");
list.NextPageCommand.Execute(null);
Finish(list.LoadVisibleLogosAsync());
Check(handler.LogoCalls == 19 && list.Assets[0].HasLogo, "new page loads only new logos");
list.PreviousPageCommand.Execute(null);
Finish(list.LoadVisibleLogosAsync());
Check(handler.LogoCalls == 19, "return to previous page makes no new requests");
using var reopened = new AssetListViewModel(http, logos: logos);
Finish(reopened.RefreshAsync());
Finish(reopened.LoadVisibleLogosAsync());
Check(handler.LogoCalls == 19, "shared cache survives list recreation");

var view = new AssetListView { DataContext = list };
var window = new Window { Content = view, Width = 1100, Height = 700 };
window.Show();
Dispatcher.UIThread.RunJobs();
var images = view.GetVisualDescendants().OfType<Image>().ToList();
Check(images.Count == 10 && images[0].IsVisible && !images[1].IsVisible, "AXAML switches from initials to actual image");
Check(ReferenceEquals(images[0].Source, firstImage), "image binding uses downloaded bitmap");
window.Close();

int before = handler.LogoCalls;
var foreign = new Asset { Id = 99, Symbol = "A99", LogoUrl = "https://foreign.test/logo" };
Finish(logos.LoadAsync(foreign, CancellationToken.None));
Check(handler.LogoCalls == before && !foreign.HasLogo, "external logo URLs rejected");
var encoded = new Asset { Id = 30, Symbol = "BTC/USD", LogoUrl = "/api/assets/BTC%2FUSD/logo" };
Finish(logos.LoadAsync(encoded, CancellationToken.None));
Check(encoded.HasLogo && handler.LastLogoPath.Contains("BTC%2FUSD"), "encoded symbols use backend route unchanged");

handler.DelayNext = true;
var delayed = new Asset { Id = 40, Symbol = "A40", LogoUrl = "/api/assets/A40/logo" };
using var cancel = new CancellationTokenSource();
var waiting = logos.LoadAsync(delayed, cancel.Token);
var second = new Asset { Id = 40, Symbol = "A40", LogoUrl = delayed.LogoUrl };
var shared = logos.LoadAsync(second, CancellationToken.None);
before = handler.LogoCalls;
cancel.Cancel();
Finish(waiting);
handler.Complete();
Finish(shared);
Check(!delayed.HasLogo && second.HasLogo && handler.LogoCalls == before, "canceled page not updated; in-flight download shared with next page");
list.Dispose();
before = handler.LogoCalls;
Finish(list.LoadVisibleLogosAsync());
Check(handler.LogoCalls == before, "disposed list starts no logo requests");

class LogoApi(byte[] png) : HttpMessageHandler
{
    public int LogoCalls;
    public string LastLogoPath = "";
    public bool DelayNext;
    private TaskCompletionSource<HttpResponseMessage>? _pending;
    public void Complete() => _pending!.SetResult(Image());
    private HttpResponseMessage Image() => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(png) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }
    };
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Headers.Authorization?.Parameter != "test-token" || request.RequestUri!.Host != "backend.test")
            throw new Exception("Unexpected authentication/host");
        string path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/logo"))
        {
            LogoCalls++;
            LastLogoPath = path;
            if (DelayNext) { DelayNext = false; _pending = new(); return _pending.Task; }
            if (path == "/api/assets/A3/logo") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var response = Image();
            if (path == "/api/assets/A4/logo") response.Content = new StringContent("bad image") { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } };
            if (path == "/api/assets/A5/logo") response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return Task.FromResult(response);
        }
        string body = path switch
        {
            "/api/assets" => JsonSerializer.Serialize(new { items = Enumerable.Range(1, 23).Select(i => new { id = i, symbol = "A" + i, logo_url = i == 2 ? null : $"/api/assets/A{i}/logo" }) }),
            "/api/favorites" => "{\"asset_ids\":[]}",
            "/api/market" => "{\"items\":[]}",
            _ => throw new Exception(path)
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
