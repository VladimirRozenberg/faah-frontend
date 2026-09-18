using System.Net;
using Avalonia;
using Avalonia.Headless;
using FAAH_Frontend.ViewModels;
AppBuilder.Configure<FAAH_Frontend.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var handler=new Fake();
var http=new HttpClient(handler) { BaseAddress=new Uri("https://example.test/") };
http.DefaultRequestHeaders.Authorization=new("Bearer","session");
using var vm=new AssetListViewModel(http);
void Check(bool value,string label) { if(!value) throw new Exception(label); Console.WriteLine("PASS "+label); }
await vm.RefreshAsync();
Check(vm.Assets.Count==2 && vm.Assets[0].IsFavorite && !vm.Assets[1].IsFavorite,"catalog merged with saved favorites");
Check(vm.Assets[0].Price==123.45m && vm.Assets[0].Currency=="CHF" && vm.Assets[1].Price is null,"quote mapping and missing quote");
var first=vm.Assets[0];
await vm.ToggleFavoriteAsync(first);
Check(!first.IsFavorite && handler.Method==HttpMethod.Delete,"remove favorite after server confirmation");
await vm.ToggleFavoriteAsync(first);
Check(first.IsFavorite && handler.Method==HttpMethod.Put,"add favorite after server confirmation");
handler.FailWrite=true;
await vm.ToggleFavoriteAsync(first);
Check(first.IsFavorite && vm.HasError,"failed write preserves confirmed state");
handler.FailWrite=false; handler.FailMarket=true;
await vm.RefreshAsync();
Check(vm.Assets.Count==2 && vm.Assets[0].Price is null && vm.HasError,"market outage preserves catalog and favorites without invented prices");
await vm.ToggleFavoriteAsync(vm.Assets[0]);
Check(!vm.Assets[0].IsFavorite,"favorites work while market offline");
handler.FailMarket=false;
await vm.RefreshAsync();
Check(!vm.Assets[0].IsFavorite && !vm.HasError,"favorite persists across reload");
handler.FailRead=true;
await vm.RefreshAsync();
Check(vm.HasError && vm.Assets.Count==2,"favorite-read failure preserves existing snapshot");
vm.Dispose(); int calls=handler.Calls; await vm.RefreshAsync();
Check(handler.Calls==calls,"navigation disposal stops requests");
class Fake : HttpMessageHandler
{
 public bool Saved=true,FailWrite,FailMarket,FailRead; public int Calls; public HttpMethod? Method;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)
 {
   Calls++;
   if(r.Headers.Authorization?.Parameter!="session") throw new Exception("Missing bearer token");
   var path=r.RequestUri!.AbsolutePath;
   string body="{}"; var status=HttpStatusCode.OK;
   if(path=="/api/assets") body="""{"items":[{"id":1,"symbol":"AAA","currency":"USD"},{"id":2,"symbol":"BBB"}]}""";
   else if(path=="/api/market") { body="""{"items":[{"symbol":"AAA","last_price":123.45,"change_percent":-2.5,"volume":4500,"currency":"CHF"}]}"""; if(FailMarket) status=HttpStatusCode.BadGateway; }
   else if(path=="/api/favorites") { body=Saved ? "{\"asset_ids\":[1]}" : "{\"asset_ids\":[]}"; if(FailRead) status=HttpStatusCode.Unauthorized; }
   else if(path=="/api/favorites/1") { Method=r.Method; if(FailWrite) status=HttpStatusCode.InternalServerError; else { Saved=r.Method==HttpMethod.Put; status=HttpStatusCode.NoContent; } }
   else throw new Exception(path);
   return Task.FromResult(new HttpResponseMessage(status) { Content=new StringContent(body) });
 }
}
