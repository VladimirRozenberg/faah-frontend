using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using FAAH_Frontend.Models;
namespace FAAH_Frontend.ViewModels;
public sealed class NewsListViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly Action<int>? _openArticle;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private List<NewsArticle> _snapshot = new();
    private bool _busy, _disposed, _loaded;
    private string _error = "", _updated = "Not loaded yet";
    private int _currentPage = 1, _pageCount = 1;
    private string _pageInput = "1";
    private string _searchQuery = "", _processedFilter = "All", _assetSymbol = "", _importanceFilter = "All", _sentimentFilter = "All", _triggerFilter = "All", _sortBy = "Published", _sortOrder = "Newest first";
    private DateTimeOffset? _publishedFromDate, _publishedToDate;
    private CancellationTokenSource _filterDebounce = new();
    public const int PageSize = 20;
    public NewsListViewModel(HttpClient http, Action<int>? openArticle = null)
    {
        _http = http;
        _openArticle = openArticle;
        RefreshCommand = new RelayCommand(parameter => { _ = RefreshAsync(); }, parameter => !IsBusy && !_disposed);
        OpenArticleCommand = new RelayCommand(OpenArticle, parameter => !_disposed && _openArticle is not null);
        GoToPageCommand = new RelayCommand(GoToPage, parameter => !_disposed && !IsBusy && TryGetPage(parameter, out _));
        _timer.Tick += OnTick;
    }
    public ObservableCollection<NewsArticle> Articles { get; } = new();
    public ObservableCollection<int> PageNumbers { get; } = new();
    public ICommand RefreshCommand { get; }
    public ICommand OpenArticleCommand { get; }
    public ICommand GoToPageCommand { get; }
    public string[] ProcessedOptions { get; } = { "All", "Processed", "Unprocessed" };
    public string[] ImportanceOptions { get; } = { "All", "Low", "Medium", "High" };
    public string[] SentimentOptions { get; } = { "All", "Negative", "Neutral", "Positive" };
    public string[] TriggerOptions { get; } = { "All", "Triggered", "Not triggered" };
    public string[] SortByOptions { get; } = { "Published", "Created" };
    public string[] SortOrderOptions { get; } = { "Newest first", "Oldest first" };
    public string SearchQuery { get => _searchQuery; set { if (!SetField(ref _searchQuery, value)) return; DebounceFilterReload(); } }
    public string ProcessedFilter { get => _processedFilter; set { if (!SetField(ref _processedFilter, value)) return; ReloadFiltered(); } }
    public string AssetSymbol { get => _assetSymbol; set { if (!SetField(ref _assetSymbol, value)) return; DebounceFilterReload(); } }
    public string ImportanceFilter { get => _importanceFilter; set { if (!SetField(ref _importanceFilter, value)) return; ReloadFiltered(); } }
    public string SentimentFilter { get => _sentimentFilter; set { if (!SetField(ref _sentimentFilter, value)) return; ReloadFiltered(); } }
    public string TriggerFilter { get => _triggerFilter; set { if (!SetField(ref _triggerFilter, value)) return; ReloadFiltered(); } }
    public DateTimeOffset? PublishedFromDate { get => _publishedFromDate; set { if (!SetField(ref _publishedFromDate, value)) return; ReloadFiltered(); } }
    public DateTimeOffset? PublishedToDate { get => _publishedToDate; set { if (!SetField(ref _publishedToDate, value)) return; ReloadFiltered(); } }
    public string SortBy { get => _sortBy; set { if (!SetField(ref _sortBy, value)) return; ReloadFiltered(); } }
    public string SortOrder { get => _sortOrder; set { if (!SetField(ref _sortOrder, value)) return; ReloadFiltered(); } }
    public bool IsBusy { get => _busy; private set { SetField(ref _busy, value); NotifyState(); RaisePageCommandState(); } }
    public string Error { get => _error; private set { SetField(ref _error, value); NotifyState(); } }
    public string Updated { get => _updated; private set => SetField(ref _updated, value); }
    public bool HasError => Error.Length > 0;
    public bool IsEmpty => _loaded && !IsBusy && !HasError && Articles.Count == 0;
    public int CurrentPage { get => _currentPage; private set { if (!SetField(ref _currentPage, value)) return; PageInput = value.ToString(); OnPropertyChanged(nameof(PageLabel)); OnPropertyChanged(nameof(HasPreviousPage)); OnPropertyChanged(nameof(HasNextPage)); UpdatePageNumbers(); RaisePageCommandState(); } }
    public int PageCount { get => _pageCount; private set { if (!SetField(ref _pageCount, value)) return; OnPropertyChanged(nameof(PageLabel)); UpdatePageNumbers(); RaisePageCommandState(); } }
    public string PageInput { get => _pageInput; set => SetField(ref _pageInput, value); }
    public string PageLabel => $"Page {CurrentPage} of {PageCount}";
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < PageCount;
    private void UpdatePageNumbers()
    {
        const int windowSize = 9;
        int maxStart = Math.Max(1, PageCount - windowSize + 1);
        int start = Math.Clamp(CurrentPage - 2, 1, maxStart);
        PageNumbers.Clear();
        for (int page = start; page < start + windowSize && page <= PageCount; page++) PageNumbers.Add(page);
    }
    private void OpenArticle(object? parameter)
    {
        if (parameter is int id) _openArticle?.Invoke(id);
    }
    private void DebounceFilterReload()
    {
        _filterDebounce.Cancel();
        _filterDebounce.Dispose();
        _filterDebounce = new CancellationTokenSource();
        _ = DebounceFilterReloadAsync();
    }
    private async Task DebounceFilterReloadAsync()
    {
        try
        {
            await Task.Delay(400, _filterDebounce.Token);
            ReloadFiltered();
        }
        catch (OperationCanceledException) { }
    }
    private void ReloadFiltered()
    {
        if (_disposed) return;
        CurrentPage = 1;
        _ = LoadPageAsync(1);
    }
    public void Start() { if (_disposed) return; _timer.Start(); _ = RefreshAsync(); }
    private void OnTick(object? sender, EventArgs e) => _ = RefreshAsync();
    private void GoToPage(object? parameter)
    {
        if (!TryGetPage(parameter, out var page) || page < 1 || page > PageCount) return;
        _ = LoadPageAsync(page);
    }
    private static bool TryGetPage(object? parameter, out int page)
    {
        if (parameter is int integer) { page = integer; return true; }
        return int.TryParse(parameter?.ToString(), out page);
    }
    private void RaisePageCommandState()
    {
        ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((RelayCommand)GoToPageCommand).RaiseCanExecuteChanged();
    }
    private void NotifyState() { OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(IsEmpty)); }
    private void ApplySort()
    {
        Articles.Clear(); foreach (var article in _snapshot) Articles.Add(article);
        NotifyState();
    }
    public Task RefreshAsync() => LoadPageAsync(CurrentPage);
    private async Task LoadPageAsync(int page)
    {
        if (_disposed || IsBusy) return;
        IsBusy = true; Error = "";
        try
        {
            if (!TryBuildQuery(page, out var query)) return;
            using var response = await _http.GetAsync($"api/data-sources?{query}", _lifetime.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Session expired. Please sign in again.",
                    HttpStatusCode.Forbidden => "You do not have access to the news.",
                    HttpStatusCode.NotFound => "The news service is unavailable on this server.",
                    HttpStatusCode.UnprocessableEntity => "The selected date range is not valid.",
                    _ => $"News could not be updated (HTTP {(int)response.StatusCode}). Please retry."
                });
            var data = await response.Content.ReadFromJsonAsync<NewsResponse>(cancellationToken: _lifetime.Token);
            if (_disposed) return;
            if (data?.Items is null || data.Items.Any(a => a is null)) throw new JsonException();
            _snapshot = data.Items;
            CurrentPage = data.Page > 0 ? data.Page : page;
            PageCount = Math.Max(1, (int)Math.Ceiling(data.Count / (double)PageSize));
            _loaded = true; ApplySort();
            Updated = "Last updated: " + DateTime.Now.ToString("HH:mm:ss") + " · Auto-refresh: 60 s";
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (OperationCanceledException) { Error = "The news request timed out. Please retry."; }
        catch (HttpRequestException) { Error = "Cannot reach the news server. Check your connection and retry."; }
        catch (JsonException) { Error = "The server returned an unexpected news format."; }
        catch (InvalidOperationException ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
    private bool TryBuildQuery(int page, out string query)
    {
        query = $"page={page}&page_size={PageSize}";
        var parameters = new List<string>();
        Add("q", SearchQuery);
        Add("asset_symbol", AssetSymbol);
        if (ProcessedFilter == "Processed") Add("is_processed", "true");
        if (ProcessedFilter == "Unprocessed") Add("is_processed", "false");
        if (ImportanceFilter != "All") Add("importance", ImportanceFilter.ToLowerInvariant());
        if (SentimentFilter != "All") Add("sentiment", SentimentFilter.ToLowerInvariant());
        if (TriggerFilter != "All") Add("should_trigger", TriggerFilter == "Triggered" ? "true" : "false");
        DateTimeOffset? from = PublishedFromDate.HasValue ? new DateTimeOffset(PublishedFromDate.Value.Date, TimeSpan.Zero) : null;
        DateTimeOffset? to = PublishedToDate.HasValue ? new DateTimeOffset(PublishedToDate.Value.Date.AddDays(1).AddTicks(-1), TimeSpan.Zero) : null;
        if (from.HasValue && to.HasValue && from > to) { Error = "Published from date must not be later than the to date."; return false; }
        if (from.HasValue) Add("published_from", from.Value.ToString("O", CultureInfo.InvariantCulture));
        if (to.HasValue) Add("published_to", to.Value.ToString("O", CultureInfo.InvariantCulture));
        Add("sort_by", SortBy == "Created" ? "created_at" : "published_at");
        Add("sort_order", SortOrder == "Oldest first" ? "asc" : "desc");
        query += "&" + string.Join("&", parameters);
        return true;
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) parameters.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _timer.Tick -= OnTick; _lifetime.Cancel();
        _filterDebounce.Cancel(); _filterDebounce.Dispose();
    }
}
