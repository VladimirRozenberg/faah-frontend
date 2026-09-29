using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FAAH_Frontend.ViewModels;

public sealed class HealthDetailsViewModel : ViewModelBase
{
    private readonly HttpClient _http;
    private readonly RelayCommand _refreshCommand;
    private bool _isRefreshing;
    private bool _isHealthy;
    private string _statusLabel = "CHECKING";
    private string _summary = "Checking API health...";
    private string _checkedAt = "Not checked yet";
    private string _databaseStatus = "Unknown";
    private string _orchestratorStatus = "Unknown";
    private string _strategistStatus = "Unknown";
    private string _rssStatus = "Unknown";
    private string _strategistDetails = "";
    private string _rssDetails = "";

    public HealthDetailsViewModel(HttpClient http)
    {
        _http = http;
        _refreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsRefreshing);
        RefreshCommand = _refreshCommand;
        _ = RefreshAsync();
    }

    public bool IsHealthy { get => _isHealthy; private set => SetField(ref _isHealthy, value); }
    public string StatusLabel { get => _statusLabel; private set => SetField(ref _statusLabel, value); }
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }
    public string CheckedAt { get => _checkedAt; private set => SetField(ref _checkedAt, value); }
    public string DatabaseStatus { get => _databaseStatus; private set => SetField(ref _databaseStatus, value); }
    public string OrchestratorStatus { get => _orchestratorStatus; private set => SetField(ref _orchestratorStatus, value); }
    public string StrategistStatus { get => _strategistStatus; private set => SetField(ref _strategistStatus, value); }
    public string RssStatus { get => _rssStatus; private set => SetField(ref _rssStatus, value); }
    public string StrategistDetails { get => _strategistDetails; private set => SetField(ref _strategistDetails, value); }
    public string RssDetails { get => _rssDetails; private set => SetField(ref _rssDetails, value); }
    public bool IsRefreshing { get => _isRefreshing; private set { if (SetField(ref _isRefreshing, value)) _refreshCommand.RaiseCanExecuteChanged(); } }
    public ICommand RefreshCommand { get; }

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            using var response = await _http.GetAsync("health");
            if (!response.IsSuccessStatusCode)
            {
                SetUnavailable($"Health endpoint returned HTTP {(int)response.StatusCode}.");
                return;
            }

            var health = await response.Content.ReadFromJsonAsync<HealthResponse>(ShellViewModel.JsonOptions);
            if (health is null)
            {
                SetUnavailable("The health response was empty.");
                return;
            }

            IsHealthy = string.Equals(health.Status, "ok", StringComparison.OrdinalIgnoreCase);
            StatusLabel = IsHealthy ? "LIVE" : "DEGRADED";
            Summary = IsHealthy ? "API is healthy" : "API reported a degraded status";
            CheckedAt = health.CheckedAt ?? "Not provided";
            DatabaseStatus = FormatStatus(health.Database?.Status, health.Database?.Connected);
            OrchestratorStatus = FormatStatus(health.Orchestrator?.Status, health.Orchestrator?.Running);
            StrategistStatus = FormatStatus(health.PortfolioStrategists?.Status, health.PortfolioStrategists?.Running);
            RssStatus = FormatStatus(health.RssFeeds?.Status, health.RssFeeds?.Running);
            StrategistDetails = $"{health.PortfolioStrategists?.Active ?? 0} active · {health.PortfolioStrategists?.Paused ?? 0} paused";
            RssDetails = $"{health.RssFeeds?.Active ?? 0} active · {health.RssFeeds?.Paused ?? 0} paused · {health.RssFeeds?.FailedFeeds ?? 0} failed";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HEALTH CHECK ERROR: {ex}");
            SetUnavailable("Unable to reach the health endpoint.");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void SetUnavailable(string message)
    {
        IsHealthy = false;
        StatusLabel = "OFFLINE";
        Summary = message;
        DatabaseStatus = OrchestratorStatus = StrategistStatus = RssStatus = "Unavailable";
        StrategistDetails = RssDetails = "";
    }

    private static string FormatStatus(string? status, bool? running) =>
        string.IsNullOrWhiteSpace(status) ? (running == true ? "Running" : "Unknown") : status;

    private sealed class HealthResponse
    {
        public string? Status { get; set; }
        [JsonPropertyName("checked_at")] public string? CheckedAt { get; set; }
        public ComponentHealth? Database { get; set; }
        public ComponentHealth? Orchestrator { get; set; }
        [JsonPropertyName("portfolio_strategists")] public StrategistHealth? PortfolioStrategists { get; set; }
        [JsonPropertyName("rss_feeds")] public RssHealth? RssFeeds { get; set; }
    }

    private class ComponentHealth
    {
        public string? Status { get; set; }
        public bool? Connected { get; set; }
        public bool? Running { get; set; }
    }

    private sealed class StrategistHealth : ComponentHealth
    {
        public int Active { get; set; }
        public int Paused { get; set; }
    }

    private sealed class RssHealth : ComponentHealth
    {
        public int Active { get; set; }
        public int Paused { get; set; }
        [JsonPropertyName("failed_feeds")] public int FailedFeeds { get; set; }
    }
}
