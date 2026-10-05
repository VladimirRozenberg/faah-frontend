using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAAH_Frontend.Models;
using FAAH_Frontend.ViewModels;
using FAAH_Frontend.Views;

AppBuilder.Configure<FAAH_Frontend.App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
var api = new Api();
var shell = new ShellViewModel(new HttpClient(api) { BaseAddress = new Uri("https://test/") });
shell.Username = "test"; shell.Password = "test";
shell.LoginCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Check(shell.ProfileUserId == "42", "test login");
var portfolioPage = (Control)shell.CurrentPage!;
shell.ShowDashboard();
var dashboardPage = (Control)shell.CurrentPage!;
var dashboardState = (DashboardViewModel)dashboardPage.DataContext!;
dashboardState.SelectedKind = "opportunity";
shell.ShowAssets();
var assetPage = (Control)shell.CurrentPage!;
shell.ShowNews();
var newsPage = (Control)shell.CurrentPage!;
shell.ShowDashboard();
Check(ReferenceEquals(shell.CurrentPage, dashboardPage) && dashboardState.SelectedKind == "opportunity",
    "dashboard screen and filter are restored after switching sections");
shell.ShowPortfolios();
Check(ReferenceEquals(shell.CurrentPage, portfolioPage), "portfolio screen is restored after switching sections");
shell.ShowAssets();
Check(ReferenceEquals(shell.CurrentPage, assetPage), "asset screen is restored after switching sections");
shell.ShowNews();
Check(ReferenceEquals(shell.CurrentPage, newsPage), "news screen is restored after switching sections");
var summary = new Portfolio { Id = 9, Name = "crypto", StatusText = "active" };
var edit = new PortfolioCreateViewModel(shell, summary);
await edit.Initialization;
Check(edit.AssetTypes.Single(x => x.Value == "crypto").IsSelected, "saved asset type restored from detail");
Check(edit.Niches.Single(x => x.Id == 3).IsSelected && edit.StrategyType == "growth"
    && edit.MaxPositionSizePct == "15", "niches and other settings restored");
var view = new PortfolioCreateView { DataContext = edit };
var window = new Window { Content = view, Width = 1100, Height = 900 };
window.Show(); Dispatcher.UIThread.RunJobs();
Check(view.GetVisualDescendants().OfType<CheckBox>().Single(x => Equals(x.Content, "Crypto")).IsChecked == true,
    "saved checkbox visibly checked");
edit.SubmitCommand.Execute(null); Dispatcher.UIThread.RunJobs();
Check(api.Patches == 0, "unchanged settings do not send a patch");
edit = new PortfolioCreateViewModel(shell, summary);
await edit.Initialization;
edit.AssetTypes.Single(x => x.Value == "stock").IsSelected = true;
edit.SubmitCommand.Execute(null); Dispatcher.UIThread.RunJobs();
Check(api.Patches == 1 && api.LastBody!.Value.GetProperty("preferred_asset_types").GetArrayLength() == 2,
    "only changed preferences are submitted");
var create = new PortfolioCreateViewModel(shell);
await create.Initialization;
create.Name = "New crypto";
create.AssetTypes.Single(x => x.Value == "crypto").IsSelected = true;
create.Niches.Single(x => x.Id == 3).IsSelected = true;
create.SubmitCommand.Execute(null); Dispatcher.UIThread.RunJobs();
Check(api.LastBody!.Value.GetProperty("preferred_asset_types")[0].GetString() == "crypto"
    && api.LastBody.Value.GetProperty("preferred_niche_ids")[0].GetInt32() == 3, "creation sends selected preferences");
var created = shell.Portfolios.Single(p => p.Id == 9);
Check(created.IsActive && created.PreferredAssetTypes.Contains("crypto"), "created response maps id and active state correctly");
api.FailDetails = true;
var failed = new PortfolioCreateViewModel(shell, summary);
await failed.Initialization;
Check(failed.HasError && !failed.CanSubmit && !failed.CanEditSettings, "failed load cannot overwrite settings with defaults");
shell.LogoutCommand.Execute(null);
shell.Username = "test";
shell.Password = "test";
shell.LoginCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
shell.ShowDashboard();
Check(!ReferenceEquals(shell.CurrentPage, dashboardPage), "logout clears cached sections before the next login");
window.Close();

class Api : HttpMessageHandler
{
    public bool FailDetails;
    public int Patches;
    public JsonElement? LastBody;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path == "/auth/login") return Reply(new { token = "test", message = "ok" });
        if (path == "/auth/me") return Reply(new { user_id = 42, username = "test", role = "employe" });
        if (path == "/api/niches") return Reply(new { items = new[] { new { id = 3, name = "Energy", category = "Resources", description = "Test" } } });
        if (path == "/api/users/42/portfolios/9" || path == "/api/users/42/portfolio/create")
        {
            if (request.Method == HttpMethod.Get && FailDetails) return new(HttpStatusCode.ServiceUnavailable);
            if (request.Content is not null)
                LastBody = JsonDocument.Parse(await request.Content.ReadAsStringAsync()).RootElement.Clone();
            if (request.Method == HttpMethod.Patch) Patches++;
            return Reply(new { id = 9, user_id = 42, name = "crypto", description = "Saved",
                strategy_type = "growth", risk_tolerance = "medium", max_position_size_pct = 15,
                max_open_positions = 10, base_currency = "USD", is_active = true,
                preferred_asset_types = new[] { "crypto" }, preferred_niche_ids = new[] { 3 }, positions = Array.Empty<object>() },
                request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK);
        }
        return Reply(new { items = Array.Empty<object>(), count = 0 });
    }
    private static HttpResponseMessage Reply(object data, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(data)) };
}
