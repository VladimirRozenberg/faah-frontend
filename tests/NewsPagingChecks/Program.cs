using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using FAAH_Frontend.ViewModels;
AppBuilder.Configure<FAAH_Frontend.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var handler=new Fake();
using var vm=new NewsListViewModel(new HttpClient(handler){BaseAddress=new Uri("https://example.test/")});
void Check(bool ok,string label){if(!ok){Console.Error.WriteLine(label);Environment.Exit(1);}Console.WriteLine("PASS "+label);}
await vm.RefreshAsync();Check(vm.Articles.Count==10 && vm.PageCount==3 && vm.Articles[0].Id==23,"ten newest articles on first page");
Check(!vm.PreviousPageCommand.CanExecute(null),"previous disabled on first page");
vm.NextPageCommand.Execute(null);Check(vm.Articles[0].Id==13 && vm.Articles.Count==10,"second page");
vm.NextPageCommand.Execute(null);Check(vm.Articles.Count==3 && !vm.NextPageCommand.CanExecute(null),"partial last page");
await vm.RefreshAsync();Check(vm.CurrentPage==3,"refresh preserves page");
vm.AlphabeticalCommand.Execute(null);Check(vm.CurrentPage==1 && vm.Articles[0].Id==1,"global alphabetical sort resets to first page");
vm.NextPageCommand.Execute(null);Check(vm.Articles[0].Id==11,"global sort applied before pagination");
handler.Fail=true;await vm.RefreshAsync();Check(vm.CurrentPage==2 && vm.Articles[0].Id==11 && vm.HasError,"failed refresh preserves visible page");
handler.Fail=false;handler.Count=4;await vm.RefreshAsync();Check(vm.CurrentPage==1 && vm.Articles.Count==4,"shrinking catalog clamps current page");
handler.Count=0;await vm.RefreshAsync();Check(vm.IsEmpty && vm.Articles.Count==0 && !vm.NextPageCommand.CanExecute(null),"empty result");
class Fake:HttpMessageHandler {
 public int Count=23;public bool Fail;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(new HttpResponseMessage(Fail?HttpStatusCode.InternalServerError:HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{items=Enumerable.Range(1,Count).Select(i=>new{src_id=i,src_title=$"Article {i:00}",src_created_at=new DateTime(2026,9,1).AddHours(i)})}))});
}
