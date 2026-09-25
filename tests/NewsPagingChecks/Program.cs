using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using FAAH_Frontend.ViewModels;

AppBuilder.Configure<FAAH_Frontend.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
using var vm = new NewsListViewModel(new HttpClient(new Api()) { BaseAddress = new Uri("https://example.test/") });

void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
async Task WaitForIdle() { while (vm.IsBusy) { Dispatcher.UIThread.RunJobs(); await Task.Delay(1); } }

await vm.RefreshAsync();
Check(vm.Articles.Count == 20 && vm.PageCount == 3 && vm.CurrentPage == 1, "first server page has 20 news");

vm.NextPageCommand.Execute(null);
await WaitForIdle();
Check(vm.CurrentPage == 2 && vm.Articles[0].Id == 21 && vm.Articles.Count == 20, "next requests news page two");

vm.NextPageCommand.Execute(null);
await WaitForIdle();
Check(vm.CurrentPage == 3 && vm.Articles[0].Id == 41 && vm.Articles.Count == 5 && !vm.NextPageCommand.CanExecute(null), "last news page is loaded from server");

class Api : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var page = int.Parse(request.RequestUri!.Query.Split('&').First(value => value.StartsWith("?page=") || value.StartsWith("page=")).Split('=').Last());
        var start = (page - 1) * 20 + 1;
        var count = Math.Max(0, Math.Min(20, 45 - start + 1));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { count = 45, page, page_size = 20, items = Enumerable.Range(start, count).Select(id => new { src_id = id, src_title = "News " + id }) })),
        });
    }
}
