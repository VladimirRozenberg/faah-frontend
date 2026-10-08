using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAAH_Frontend.ViewModels;
using FAAH_Frontend.Views;

AppBuilder.Configure<FAAH_Frontend.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
string? openedAsset = null;
using var vm = new NewsListViewModel(new HttpClient(new Api()) { BaseAddress = new Uri("https://example.test/") },
    openAsset: symbol => openedAsset = symbol);

void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
async Task WaitForIdle() { while (vm.IsBusy) { Dispatcher.UIThread.RunJobs(); await Task.Delay(1); } }

await vm.RefreshAsync();
Check(vm.Articles.Count == 20 && vm.PageCount == 3 && vm.CurrentPage == 1, "first server page has 20 news");

vm.NextPageCommand.Execute(null);
await WaitForIdle();
Check(vm.CurrentPage == 2 && vm.Articles[0].Id == 21 && vm.Articles.Count == 20, "next requests news page two");
Check(vm.Articles[0].AssetTags.Single() == "NVDA", "news item exposes its related asset symbol");
var newsWindow = new Window { Content = new NewsListView { DataContext = vm }, Width = 1000, Height = 600 };
newsWindow.Show();
Dispatcher.UIThread.RunJobs();
var assetLink = newsWindow.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "NVDA");
Check(assetLink.IsVisible && assetLink.Command is not null, "related asset link is visible and bound in the news list");
assetLink.Command!.Execute(assetLink.CommandParameter);
Check(openedAsset == "NVDA", "related asset command opens the selected symbol");
newsWindow.Close();

vm.NextPageCommand.Execute(null);
await WaitForIdle();
Check(vm.CurrentPage == 3 && vm.Articles[0].Id == 41 && vm.Articles.Count == 5 && !vm.NextPageCommand.CanExecute(null), "last news page is loaded from server");

string? detailOpenedAsset = null;
using var detailVm = new NewsDetailViewModel(new HttpClient(new DetailApi()) { BaseAddress = new Uri("https://example.test/") },
    88, () => { }, symbol => detailOpenedAsset = symbol);
detailVm.Start();
while (detailVm.IsBusy) { Dispatcher.UIThread.RunJobs(); await Task.Delay(1); }
Check(detailVm.HasDetail && detailVm.HasRelatedAssets, "news detail exposes assets from classification");
var detailWindow = new Window { Content = new NewsDetailView { DataContext = detailVm }, Width = 1000, Height = 700 };
detailWindow.Show();
Dispatcher.UIThread.RunJobs();
var detailAssetLink = detailWindow.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "NVDA");
var detailText = detailWindow.GetVisualDescendants().OfType<TextBlock>().ToList();
Check(detailAssetLink.IsVisible && detailText.FindIndex(text => text.Text == "Related assets") < detailText.FindIndex(text => text.Text == "Classification"),
    "news detail asset link is visible above Classification");
detailAssetLink.Command!.Execute(detailAssetLink.CommandParameter);
Check(detailOpenedAsset == "NVDA", "news detail asset link opens the selected symbol");
detailWindow.Close();

class Api : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var page = int.Parse(request.RequestUri!.Query.Split('&').First(value => value.StartsWith("?page=") || value.StartsWith("page=")).Split('=').Last());
        var start = (page - 1) * 20 + 1;
        var count = Math.Max(0, Math.Min(20, 45 - start + 1));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { count = 45, page, page_size = 20, items = Enumerable.Range(start, count).Select(id => new { src_id = id, src_title = "News " + id, related_assets = id == 21 ? new[] { "NVDA" } : Array.Empty<string>() }) })),
        });
    }
}

class DetailApi : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        const string detail = """{"source":{"src_id":88,"src_title":"NVDA news","src_content":"Company update"},"classifications":[{"cls_category":"company","assets":[{"ast_id":1,"ast_symbol":"NVDA"}]}],"analyses":[]}""";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(detail) });
    }
}
