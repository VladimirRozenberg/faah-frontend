using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAAH_Frontend;
using FAAH_Frontend.Models;
using FAAH_Frontend.ViewModels;
using FAAH_Frontend.Views;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); }
void Pump() { Dispatcher.UIThread.RunJobs(); }
var handler = new Api();
var shell = new ShellViewModel(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });
shell.Username = "admin"; shell.Password = "test"; shell.LoginCommand.Execute(null); Pump();
Check(shell.IsLoggedIn && shell.IsAdmin, "login identity");
var dashboard = (DashboardViewModel)((Control)shell.CurrentPage!).DataContext!;
Check(dashboard.Opportunities.Count == 10 && dashboard.Cash.Contains("USD"), "dashboard shows only top 10 buy opportunities");
Check(dashboard.Opportunities[0].SigConfidence == 99 && dashboard.Opportunities[^1].SigConfidence == 90, "opportunities are ranked by confidence");
Check(dashboard.Cash == "100.00 USD", "users balance is displayed");
dashboard.SellCommand.Execute(null);
Check(dashboard.Opportunities.Single().AssetSymbol == "SELL", "sell filter");
dashboard.DetailCommand.Execute(dashboard.Opportunities[0]);
Check(dashboard.Selected!.Explanation == "Higher risk and a weakening trend.", "analysis details");
dashboard.BuyCommand.Execute(null); dashboard.DetailCommand.Execute(dashboard.Opportunities[0]);
var window = new MainWindow { DataContext = shell }; window.Show(); Pump();
Directory.CreateDirectory("work/revision3/ui-checks");
window.CaptureRenderedFrame()?.Save("work/revision3/ui-checks/dashboard.png");
shell.ShowPortfolios(); Pump();
Check(((PortfolioListViewModel)((Control)shell.CurrentPage!).DataContext!).Portfolios.Count == 5 && shell.IsPortfolioActive, "original portfolio restored separately");
window.CaptureRenderedFrame()?.Save("work/revision3/ui-checks/portfolio.png");
shell.ShowUsers(); Pump();
var users = (UserListViewModel)((Control)shell.CurrentPage!).DataContext!;
Check(users.Users.Count == 10 && users.PageCount == 2, "admin list pagination");
users.NextPageCommand.Execute(null); Check(users.Users.Count == 3, "admin list next page");
users.FirstPageCommand.Execute(null);
Check(!((Control)shell.CurrentPage!).GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "ID" || t.Text == "1"), "database IDs hidden from admin console");
window.CaptureRenderedFrame()?.Save("work/revision3/ui-checks/admin.png");
Check(!((Control)shell.CurrentPage!).GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() is "Make admin" or "Deactivate"), "user list has no management buttons");
shell.ShowUserInformation(new User { UserId=2, Username="Laura", Email="laura@example.test", Role="employe", IsActive=true }); Pump();
var profile = (PersonalInformationViewModel)((Control)shell.CurrentPage!).DataContext!;
Check(profile.CanManage && profile.Transactions.Count == 2, "profile lists paginated transactions");
profile.ChangeRoleCommand.Execute(null); Pump(); Check(profile.User.Role == "admin", "role change on profile");
profile.ToggleStatusCommand.Execute(null); Pump(); Check(profile.User.IsActive == false, "deactivate on profile");
profile.ToggleStatusCommand.Execute(null); Pump();
profile.DepositAmount="25,50"; profile.DepositCommand.Execute(null); Pump(); Check(handler.Deposits == 1, "admin deposit request");
profile.DepositAmount="-1"; profile.DepositCommand.Execute(null); Check(handler.Deposits == 1, "invalid amount blocked");
window.CaptureRenderedFrame()?.Save("work/revision3/ui-checks/profile.png");
shell.ShowProfile(); Pump();
Check(!((PersonalInformationViewModel)((Control)shell.CurrentPage!).DataContext!).CanManage, "admin self lockout protection");
handler.Legacy=true; shell.ShowProfile(); Pump();
Check(((PersonalInformationViewModel)((Control)shell.CurrentPage!).DataContext!).DepositCommand.CanExecute(null), "admin top-up remains available before balance is returned");
handler.Legacy=false;
shell.ShowSettingsCommand.Execute(null); Pump();
window.CaptureRenderedFrame()?.Save("work/revision3/ui-checks/settings.png");
shell.LogoutCommand.Execute(null); handler.Admin=false;
shell.Username="user"; shell.Password="test"; shell.LoginCommand.Execute(null); Pump(); shell.ShowProfile(); Pump();
profile=(PersonalInformationViewModel)((Control)shell.CurrentPage!).DataContext!;
Check(!profile.IsAdmin && !profile.CanManage && !profile.DepositCommand.CanExecute(null), "employee cannot fund or administer");
handler.Legacy=true;
shell.ShowDashboard(); Pump();
dashboard=(DashboardViewModel)((Control)shell.CurrentPage!).DataContext!;
Check(handler.LegacyCalls == 1 && dashboard.Opportunities.Count == 1 && dashboard.Opportunities[0].AssetSymbol == "LEGACY", "legacy API opportunities exclude expired and private signals");
Check(dashboard.Cash == "Unavailable" && dashboard.AccountMessage.Length > 0, "missing balance is not shown as zero");
shell.ShowProfile(); Pump();
profile=(PersonalInformationViewModel)((Control)shell.CurrentPage!).DataContext!;
Check(!profile.DepositCommand.CanExecute(null), "employee cannot top up when balance is unavailable");
var favoriteApi = new FavoriteApi();
using var assets = new AssetListViewModel(new HttpClient(favoriteApi) { BaseAddress = new Uri("http://test/") });
assets.RefreshAsync().GetAwaiter().GetResult(); assets.NextPageCommand.Execute(null);
var picked=assets.Assets[1];
var saving=assets.ToggleFavoriteAsync(picked);
Check(picked.IsFavorite && assets.Assets.Contains(picked) && !saving.IsCompleted, "star turns yellow immediately on page two without moving row");
var favoriteWindow = new Window { Width=1100, Height=650, Content=new AssetListView { DataContext=assets } };
favoriteWindow.Show(); Pump();
favoriteWindow.CaptureRenderedFrame()?.Save("work/revision3/ui-checks/favorite-pending.png");
favoriteWindow.Close();
favoriteApi.Pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
while(!saving.IsCompleted) { Pump(); Thread.Sleep(5); }
saving.GetAwaiter().GetResult();
Check(picked.IsFavorite && assets.Assets.Contains(picked), "saved favorite stays visible");
favoriteApi.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
var failing=assets.ToggleFavoriteAsync(picked);
Check(!picked.IsFavorite, "unfavorite updates immediately");
favoriteApi.Pending.SetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
while(!failing.IsCompleted) { Pump(); Thread.Sleep(5); }
failing.GetAwaiter().GetResult();
Check(picked.IsFavorite && assets.HasError, "failed save restores favorite");
window.Close();

class Api : HttpMessageHandler
{
 public bool Admin=true, Active=true, Legacy; public int Deposits, LegacyCalls; private string role="employe";
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
 {
  var path=request.RequestUri!.AbsolutePath;
  string json;
  if (Legacy && path=="/api/opportunities") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
  if (path=="/api/trading-signals") { LegacyCalls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent("""{"items":[{"sig_id":1,"asset_symbol":"LEGACY","sig_action":"buy","sig_status":"active","sig_created_at":"2026-09-21T10:00:00Z"},{"sig_id":2,"asset_symbol":"PRIVATE","sig_action":"buy","sig_status":"active","sig_prt_id":999,"sig_created_at":"2026-09-21T10:00:00Z"},{"sig_id":3,"asset_symbol":"EXPIRED","sig_action":"buy","sig_status":"active","sig_expires_at":"2020-01-01T00:00:00Z","sig_created_at":"2020-01-01T00:00:00Z"}]}""",Encoding.UTF8,"application/json") }); }
  if(path=="/auth/login") json="{\"token\":\"test\",\"message\":\"ok\"}";
  else if(path=="/auth/me") json=Admin ? "{\"user_id\":1,\"username\":\"admin\",\"role\":\"admin\",\"email\":\"admin@example.test\"}" : "{\"user_id\":2,\"username\":\"user\",\"role\":\"employe\"}";
  else if(path=="/api/opportunities") json=System.Text.Json.JsonSerializer.Serialize(new { items =
    Enumerable.Range(0,12).Select(id=>new { sig_id=id+1, asset_symbol="BUY"+id, asset_name="Bullish asset "+id, sig_action="buy", analysis_summary="Improved earnings and positive momentum.", sig_created_at="2026-09-21T10:00:00Z", sig_confidence=99-id })
    .Append(new { sig_id=20, asset_symbol="SELL", asset_name="Bearish asset", sig_action="sell", analysis_summary="Higher risk and a weakening trend.", sig_created_at="2026-09-21T10:00:00Z", sig_confidence=75 }) });
  else if(path.EndsWith("/transactions")) json="""{"transactions":[{"name":"Example asset","symbol":"TEST","type":"buy","quantity":1,"amount":100,"created_at":"2026-09-21T10:00:00Z","currency":"USD"},{"name":"Example asset","symbol":"TEST","type":"sell","quantity":1,"amount":105,"created_at":"2026-09-22T10:00:00Z","currency":"USD"}]}""";
  else if(path.EndsWith("/portfolio")) json=Legacy ? """{"id":2,"total_current_value":250,"base_currency":"USD"}""" : """{"id":2,"balance":100,"cash_balance":999,"total_current_value":250,"base_currency":"USD"}""";
  else if(path=="/admin/utilisateurs") json=System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1,13).Select(id=>new {user_id=id,username="User "+id,email=$"user{id}@example.com",role="employe",is_active=id%3!=0}));
  else if(path.EndsWith("/deposit")) { Deposits++; json="{\"cash_balance\":125.5}"; }
  else { if(path.EndsWith("/role")) role=role=="admin"?"employe":"admin"; if(path.EndsWith("/statut")) Active=!Active; json=$"{{\"user_id\":2,\"username\":\"Laura\",\"role\":\"{role}\",\"is_active\":{Active.ToString().ToLowerInvariant()}}}"; }
  return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(json,Encoding.UTF8,"application/json") });
 }
}

class FavoriteApi : HttpMessageHandler
{
 public TaskCompletionSource<HttpResponseMessage> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
 {
  if(request.Method != HttpMethod.Get) return Pending.Task;
  var json=request.RequestUri!.AbsolutePath switch {
   "/api/assets" => System.Text.Json.JsonSerializer.Serialize(new {items=Enumerable.Range(1,23).Select(id=>new {id,symbol="ASSET"+id,name="Asset "+id})}),
   "/api/favorites" => "{\"asset_ids\":[]}",
   _ => "{\"items\":[]}" };
  return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(json,Encoding.UTF8,"application/json")});
 }
}
