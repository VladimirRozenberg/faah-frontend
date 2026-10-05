using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
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
    private string _liveMarketWorkerStatus = "unavailable";
    private string _liveMarketWorkerColor = "#64748B";
    private int _liveMarketWorkerAssets;
    private int _liveMarketWorkerLivePrices;
    private int _liveMarketWorkerDelayedPrices;
    private int _liveMarketWorkerUnavailablePrices;
    private string _liveMarketWorkerHeartbeat = "";
    private string _liveMarketWorkerError = "";
    private bool _hasLiveMarketWorkerHeartbeat;
    private bool _hasLiveMarketWorkerError;
    private ExternalSourceHealth? _yahoo, _twelve;
    private string _externalError = "Checking sources…";

    public string YahooStatus => SourceStatus(_yahoo);
    public string TwelveStatus => SourceStatus(_twelve);
    public string YahooDetails => SourceDetails(_yahoo);
    public string TwelveDetails => SourceDetails(_twelve);
    public string YahooColor => SourceColor(_yahoo);
    public string TwelveColor => SourceColor(_twelve);

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
    public string LiveMarketWorkerStatus { get => _liveMarketWorkerStatus; private set => SetField(ref _liveMarketWorkerStatus, value); }
    public string LiveMarketWorkerColor { get => _liveMarketWorkerColor; private set => SetField(ref _liveMarketWorkerColor, value); }
    public int LiveMarketWorkerAssets { get => _liveMarketWorkerAssets; private set => SetField(ref _liveMarketWorkerAssets, value); }
    public int LiveMarketWorkerLivePrices { get => _liveMarketWorkerLivePrices; private set { if (SetField(ref _liveMarketWorkerLivePrices, value)) OnPropertyChanged(nameof(LiveMarketWorkerDisplayedLivePrices)); } }
    public int LiveMarketWorkerDelayedPrices { get => _liveMarketWorkerDelayedPrices; private set { if (SetField(ref _liveMarketWorkerDelayedPrices, value)) OnPropertyChanged(nameof(LiveMarketWorkerDisplayedLivePrices)); } }
    public int LiveMarketWorkerDisplayedLivePrices => LiveMarketWorkerLivePrices + LiveMarketWorkerDelayedPrices;
    public int LiveMarketWorkerUnavailablePrices { get => _liveMarketWorkerUnavailablePrices; private set => SetField(ref _liveMarketWorkerUnavailablePrices, value); }
    public string LiveMarketWorkerHeartbeat { get => _liveMarketWorkerHeartbeat; private set => SetField(ref _liveMarketWorkerHeartbeat, value); }
    public string LiveMarketWorkerError { get => _liveMarketWorkerError; private set => SetField(ref _liveMarketWorkerError, value); }
    public bool HasLiveMarketWorkerHeartbeat { get => _hasLiveMarketWorkerHeartbeat; private set => SetField(ref _hasLiveMarketWorkerHeartbeat, value); }
    public bool HasLiveMarketWorkerError { get => _hasLiveMarketWorkerError; private set => SetField(ref _hasLiveMarketWorkerError, value); }
    public bool IsRefreshing { get => _isRefreshing; private set { if (SetField(ref _isRefreshing, value)) _refreshCommand.RaiseCanExecuteChanged(); } }
    public ICommand RefreshCommand { get; }

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        // Les sources externes ne bloquent pas l'affichage du statut interne de l'API.
        var externalTask = RefreshExternalAsync();
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
            UpdateLiveMarketWorker(health.LiveMarketWorker);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HEALTH CHECK ERROR: {ex}");
            SetUnavailable("Unable to reach the health endpoint.");
        }
        finally
        {
            await externalTask;
            IsRefreshing = false;
        }
    }

    private async Task RefreshExternalAsync()
    {
        try
        {
            using var response = await _http.GetAsync("health/external");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _externalError = "Backend update required for source checks.";
                _yahoo = _twelve = null;
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var sources = await response.Content.ReadFromJsonAsync<ExternalHealthResponse>();
                _yahoo = sources?.Yahoo;
                _twelve = sources?.Twelve;
                _externalError = "Source status not provided by the backend.";
            }
        }
        catch (Exception)
        {
            _yahoo = _twelve = null;
            _externalError = "Unable to check sources through the backend.";
        }
        OnPropertyChanged(nameof(YahooStatus));
        OnPropertyChanged(nameof(TwelveStatus));
        OnPropertyChanged(nameof(YahooDetails));
        OnPropertyChanged(nameof(TwelveDetails));
        OnPropertyChanged(nameof(YahooColor));
        OnPropertyChanged(nameof(TwelveColor));
    }

    private static string SourceStatus(ExternalSourceHealth? source) => source?.Status switch
    {
        "connected" => "Connected",
        "rate_limited" => "Rate limited",
        "not_configured" => "Not configured",
        "authentication_failed" => "Access refused",
        "unavailable" => "Unavailable",
        _ => "Unknown"
    };

    private static string SourceColor(ExternalSourceHealth? source) => source?.Status switch
    {
        "connected" => "#278348",
        "unavailable" or "authentication_failed" => "#C63838",
        "rate_limited" or "not_configured" => "#A56400",
        _ => "#64748B"
    };

    private string SourceDetails(ExternalSourceHealth? source)
    {
        if (source is null) return _externalError;
        string date = source.CheckedAt?.ToLocalTime().ToString("dd.MM HH:mm:ss") ?? "unknown";
        return $"{source.Detail}\nChecked: {date} · cache: {source.CacheSeconds / 60} min";
    }

    private sealed class ExternalHealthResponse
    {
        [JsonPropertyName("yfinance")] public ExternalSourceHealth? Yahoo { get; set; }
        [JsonPropertyName("twelve_data")] public ExternalSourceHealth? Twelve { get; set; }
    }

    private sealed class ExternalSourceHealth
    {
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("detail")] public string? Detail { get; set; }
        [JsonPropertyName("checked_at")] public DateTimeOffset? CheckedAt { get; set; }
        [JsonPropertyName("cache_seconds")] public int CacheSeconds { get; set; }
    }

    private void SetUnavailable(string message)
    {
        IsHealthy = false;
        StatusLabel = "OFFLINE";
        Summary = message;
        DatabaseStatus = OrchestratorStatus = StrategistStatus = RssStatus = "Unavailable";
        StrategistDetails = RssDetails = "";
        UpdateLiveMarketWorker(null);
    }

    private void UpdateLiveMarketWorker(LiveMarketWorkerHealth? worker)
    {
        LiveMarketWorkerStatus = worker is null ? "unavailable" : WorkerStatus(worker);
        LiveMarketWorkerColor = LiveMarketWorkerStatus switch
        {
            "running" => "#278348",
            "degraded" => "#A56400",
            "down" => "#C63838",
            _ => "#64748B"
        };
        LiveMarketWorkerAssets = worker?.SubscribedAssets ?? 0;
        LiveMarketWorkerLivePrices = worker?.LivePrices ?? 0;
        LiveMarketWorkerDelayedPrices = worker?.DelayedPrices ?? 0;
        LiveMarketWorkerUnavailablePrices = worker?.UnavailablePrices ?? 0;
        LiveMarketWorkerHeartbeat = worker?.LastHeartbeatAt?.ToLocalTime().ToString("dd.MM HH:mm:ss") ?? "";
        HasLiveMarketWorkerHeartbeat = worker?.LastHeartbeatAt is not null;
        LiveMarketWorkerError = worker?.Error ?? "";
        HasLiveMarketWorkerError = !string.IsNullOrWhiteSpace(worker?.Error);
    }

    private static string WorkerStatus(LiveMarketWorkerHealth worker) => worker.Status?.ToLowerInvariant() switch
    {
        "running" => "running",
        "degraded" => "degraded",
        "down" => "down",
        "unavailable" => "unavailable",
        _ => worker.Healthy switch
        {
            true => "running",
            false => "down",
            _ => "unavailable"
        }
    };

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
        [JsonPropertyName("live_market_worker")] public LiveMarketWorkerHealth? LiveMarketWorker { get; set; }
    }

    private sealed class LiveMarketWorkerHealth
    {
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("healthy")] public bool? Healthy { get; set; }
        [JsonPropertyName("live_prices")] public int LivePrices { get; set; }
        [JsonPropertyName("delayed_prices")] public int DelayedPrices { get; set; }
        [JsonPropertyName("unavailable_prices")] public int UnavailablePrices { get; set; }
        [JsonPropertyName("subscribed_assets")] public int SubscribedAssets { get; set; }
        [JsonPropertyName("last_heartbeat_at")] public DateTimeOffset? LastHeartbeatAt { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
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
