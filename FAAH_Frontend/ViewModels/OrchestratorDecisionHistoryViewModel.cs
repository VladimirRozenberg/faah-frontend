using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public sealed class OrchestratorDecisionHistoryViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private CancellationTokenSource? _activeRequest;
    private bool _busy;
    private bool _disposed;
    private bool _loaded;
    private string _error = "";
    private int _count;
    private int _currentPage = 1;
    private int _pageCount = 1;
    private int _selectedPageSize = 20;
    private string _pageInput = "1";
    private long _requestVersion;

    public OrchestratorDecisionHistoryViewModel(HttpClient http)
    {
        _http = http;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !_disposed);
        FirstPageCommand = new RelayCommand(_ => GoToPage(1), _ => !_disposed && HasPreviousPage);
        PreviousPageCommand = new RelayCommand(_ => GoToPage(CurrentPage - 1), _ => !_disposed && HasPreviousPage);
        NextPageCommand = new RelayCommand(_ => GoToPage(CurrentPage + 1), _ => !_disposed && HasNextPage);
        LastPageCommand = new RelayCommand(_ => GoToPage(PageCount), _ => !_disposed && HasNextPage);
        GoToPageCommand = new RelayCommand(GoToPage, _ => !_disposed);
        ToggleCycleCommand = new RelayCommand(ToggleCycle, _ => !_disposed);
    }

    public ObservableCollection<OrchestratorDecisionCycle> Cycles { get; } = new();
    public ObservableCollection<int> PageNumbers { get; } = new();
    public int[] PageSizeOptions { get; } = Enumerable.Range(1, 100).ToArray();
    public ICommand RefreshCommand { get; }
    public ICommand FirstPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand GoToPageCommand { get; }
    public ICommand ToggleCycleCommand { get; }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!SetField(ref _busy, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
            ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
        }
    }

    public string Error
    {
        get => _error;
        private set
        {
            if (!SetField(ref _error, value)) return;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsEmpty => _loaded && !IsBusy && !HasError && Cycles.Count == 0;
    public int Count { get => _count; private set { if (SetField(ref _count, value)) OnPropertyChanged(nameof(CountDisplay)); } }
    public string CountDisplay => $"{Count} total cycles";
    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetField(ref _currentPage, value)) return;
            PageInput = value.ToString();
            OnPropertyChanged(nameof(PageLabel));
            OnPropertyChanged(nameof(HasPreviousPage));
            OnPropertyChanged(nameof(HasNextPage));
            UpdatePageNumbers();
            RaisePageCommandState();
        }
    }
    public int PageCount
    {
        get => _pageCount;
        private set
        {
            if (!SetField(ref _pageCount, value)) return;
            OnPropertyChanged(nameof(PageLabel));
            UpdatePageNumbers();
            RaisePageCommandState();
        }
    }
    public int SelectedPageSize
    {
        get => _selectedPageSize;
        set
        {
            if (value is < 1 or > 100 || !SetField(ref _selectedPageSize, value)) return;
            CurrentPage = 1;
            PageCount = Math.Max(1, (int)Math.Ceiling(Count / (double)value));
            _ = LoadPageAsync(1);
        }
    }
    public string PageInput { get => _pageInput; set => SetField(ref _pageInput, value); }
    public string PageLabel => $"Page {CurrentPage} of {PageCount}";
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < PageCount;

    public void Start() => _ = LoadPageAsync(CurrentPage);
    public Task RefreshAsync() => LoadPageAsync(CurrentPage);

    private async Task LoadPageAsync(int page)
    {
        if (_disposed) return;
        _activeRequest?.Cancel();
        _activeRequest?.Dispose();
        var request = new CancellationTokenSource();
        _activeRequest = request;
        var version = ++_requestVersion;
        IsBusy = true;
        Error = "";
        Cycles.Clear();

        try
        {
            using var response = await _http.GetAsync(
                $"api/orchestrator/decisions?page={page}&page_size={SelectedPageSize}", request.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Session expired. Please sign in again.",
                    HttpStatusCode.Forbidden => "Administrator access is required to view orchestrator decisions.",
                    HttpStatusCode.NotFound => "Decision history is unavailable. Apply the backend migration, then retry.",
                    _ => $"Decision history could not be loaded (HTTP {(int)response.StatusCode}). Please retry."
                });

            var data = await response.Content.ReadFromJsonAsync<OrchestratorDecisionHistoryResponse>(ShellViewModel.JsonOptions, request.Token);
            if (_disposed || version != _requestVersion) return;
            if (data?.Items is null || data.Items.Any(item => item is null)) throw new JsonException();

            Count = Math.Max(0, data.Count);
            if (data.PageSize is >= 1 and <= 100 && _selectedPageSize != data.PageSize)
            {
                _selectedPageSize = data.PageSize;
                OnPropertyChanged(nameof(SelectedPageSize));
            }
            PageCount = Math.Max(1, (int)Math.Ceiling(Count / (double)SelectedPageSize));
            CurrentPage = Math.Clamp(data.Page > 0 ? data.Page : page, 1, PageCount);
            foreach (var cycle in data.Items) Cycles.Add(cycle);
            _loaded = true;
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (OperationCanceledException) when (_disposed || version != _requestVersion) { }
        catch (OperationCanceledException) when (!request.IsCancellationRequested)
        {
            Error = "The decision history request timed out. Please retry.";
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { if (version == _requestVersion) Error = "Cannot reach the server. Check your connection and retry."; }
        catch (JsonException) { if (version == _requestVersion) Error = "The server returned an unexpected decision-history format."; }
        catch (InvalidOperationException ex) { if (version == _requestVersion) Error = ex.Message; }
        finally
        {
            if (version == _requestVersion)
            {
                IsBusy = false;
                if (ReferenceEquals(_activeRequest, request)) _activeRequest = null;
                request.Dispose();
            }
        }
    }

    private void GoToPage(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), out var page) || page < 1 || page > PageCount || page == CurrentPage) return;
        CurrentPage = page;
        _ = LoadPageAsync(page);
    }

    private void ToggleCycle(object? parameter)
    {
        if (parameter is OrchestratorDecisionCycle cycle) cycle.IsExpanded = !cycle.IsExpanded;
    }

    private void UpdatePageNumbers()
    {
        const int windowSize = 9;
        int maxStart = Math.Max(1, PageCount - windowSize + 1);
        int start = Math.Clamp(CurrentPage - 2, 1, maxStart);
        PageNumbers.Clear();
        for (int page = start; page < start + windowSize && page <= PageCount; page++) PageNumbers.Add(page);
    }

    private void RaisePageCommandState()
    {
        ((RelayCommand)FirstPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)LastPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)GoToPageCommand).RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_requestVersion;
        _activeRequest?.Cancel();
        _activeRequest?.Dispose();
        _activeRequest = null;
    }
}