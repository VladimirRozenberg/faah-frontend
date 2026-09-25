using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using FAAH_Frontend.ViewModels;

AppBuilder.Configure<FAAH_Frontend.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
using var vm = new AssetListViewModel(new HttpClient(new Api()) { BaseAddress = new Uri("https://example.test/") });

void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
async Task WaitForIdle() { while (vm.IsBusy) { Dispatcher.UIThread.RunJobs(); await Task.Delay(1); } }

await vm.RefreshAsync();
Check(vm.Assets.Count == 20 && vm.PageCount == 3 && vm.CurrentPage == 1, "first server page has 20 assets and three pages");
Check(vm.NextPageCommand.CanExecute(null), "next stays enabled from total count");

vm.NextPageCommand.Execute(null);
await WaitForIdle();
Check(vm.CurrentPage == 2 && vm.Assets[0].Id == 21 && vm.Assets.Count == 20, "next requests page two");

vm.NextPageCommand.Execute(null);
await WaitForIdle();
Check(vm.CurrentPage == 3 && vm.Assets[0].Id == 41 && vm.Assets.Count == 5 && !vm.NextPageCommand.CanExecute(null), "last page is loaded from server");

class Api : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/favorites") return Reply(new { asset_ids = Array.Empty<int>() });
        if (path == "/api/market") return Reply(new { items = Array.Empty<object>() });

        var page = ReadPage(request.RequestUri!);
        var start = (page - 1) * 20 + 1;
        var count = Math.Max(0, Math.Min(20, 45 - start + 1));
        return Reply(new { count = 45, page, page_size = 20, items = Enumerable.Range(start, count).Select(id => new { id, symbol = "A" + id }) });
    }

    private static int ReadPage(Uri uri) => int.Parse(uri.Query.Split('&').First(value => value.StartsWith("?page=") || value.StartsWith("page=")).Split('=').Last());
    private static Task<HttpResponseMessage> Reply(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) });
}
