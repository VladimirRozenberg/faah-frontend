using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using FAAH_Frontend.ViewModels;
AppBuilder.Configure<FAAH_Frontend.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var handler=new Fake();
using var vm=new AssetListViewModel(new HttpClient(handler){BaseAddress=new Uri("https://example.test/")});
void Check(bool ok,string name) {if(!ok) throw new Exception(name); Console.WriteLine("PASS "+name);}
void Finish(Task task) { var deadline=DateTime.UtcNow.AddSeconds(5); while(!task.IsCompleted && DateTime.UtcNow<deadline) {Dispatcher.UIThread.RunJobs(); Thread.Sleep(1);} if(!task.IsCompleted) throw new Exception("Timed out"); task.GetAwaiter().GetResult(); }
var loading=vm.RefreshAsync();
Check(!loading.IsCompleted && vm.Assets.Count==10,"first ten assets visible before market response");
Check(!vm.PreviousPageCommand.CanExecute(null) && vm.NextPageCommand.CanExecute(null),"first page boundaries");
vm.NextPageCommand.Execute(null); Check(vm.Assets[0].Id==11 && vm.Assets.Count==10,"navigation during price loading");
vm.NextPageCommand.Execute(null); Check(vm.Assets.Count==3 && !vm.NextPageCommand.CanExecute(null),"last page remainder and boundary");
handler.Market.SetResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"items\":[{\"symbol\":\"A21\",\"last_price\":123,\"volume\":99}]}")});
Finish(loading);
Check(vm.Assets[0].Id==21 && vm.Assets[0].Price==123,"late quotes update currently visible page without resetting it");
Check(vm.Assets[0].IsFavorite,"saved favorite mapped on third page");
Finish(vm.ToggleFavoriteAsync(vm.Assets[0]));
vm.PreviousPageCommand.Execute(null); vm.NextPageCommand.Execute(null);
Check(!vm.Assets[0].IsFavorite,"favorite change survives page navigation");
handler.Count=4; Finish(vm.RefreshAsync());
Check(vm.Assets.Count==4 && vm.PageLabel.StartsWith("Page 1 of 1"),"refresh clamps page when catalog shrinks");
handler.Count=0; Finish(vm.RefreshAsync()); Check(vm.IsEmpty && !vm.NextPageCommand.CanExecute(null),"empty catalog");
class Fake:HttpMessageHandler
{
 public int Count=23;
 public TaskCompletionSource<HttpResponseMessage> Market=new();
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)
 {
   var path=r.RequestUri!.AbsolutePath;
   if(path=="/api/market") {
     if(!Market.Task.IsCompleted) return Market.Task;
     return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"items\":[]}")});
   }
   var body=path=="/api/assets" ? JsonSerializer.Serialize(new {items=Enumerable.Range(1,Count).Select(i=>new{id=i,symbol="A"+i})}) : "{\"asset_ids\":[21]}";
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
 }
}
