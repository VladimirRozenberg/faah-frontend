using System;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public sealed class NewsListViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private int _currentPage = 1, _totalCount;
    private bool _busy, _disposed;
    private string _error = "", _updated = "Not loaded yet";

    public const int PageSize = 20;

    public NewsListViewModel(HttpClient http)
    {
        _http = http;
        PreviousPageCommand = new RelayCommand(_ => { _ = LoadPageAsync(_currentPage - 1); }, _ => !_disposed && !IsBusy && _currentPage > 1);
        NextPageCommand = new RelayCommand(_ => { _ = LoadPageAsync(_currentPage + 1); }, _ => !_disposed && !IsBusy && _currentPage < PageCount);
        RefreshCommand = new RelayCommand(_ => { _ = LoadPageAsync(_currentPage); }, _ => !_disposed && !IsBusy);
        _timer.Tick += OnTimerTick;
    }

    public ObservableCollection<NewsArticle> Articles { get; } = new();
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public int CurrentPage => _currentPage;
    public int PageCount => Math.Max(1, (_totalCount + PageSize - 1) / PageSize);
    public string PageLabel => $"Page {CurrentPage} of {PageCount} · {_totalCount} news";
    public bool HasError => Error.Length > 0;
    public bool IsEmpty => !IsBusy && !HasError && _totalCount == 0;

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            SetField(ref _busy, value);
            RefreshCommands();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public string Error
    {
        get => _error;
        private set
        {
            SetField(ref _error, value);
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public string Updated { get => _updated; private set => SetField(ref _updated, value); }

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
        _ = LoadPageAsync(1);
    }

    public Task RefreshAsync() => LoadPageAsync(_currentPage);

    private void OnTimerTick(object? sender, EventArgs e) => _ = LoadPageAsync(_currentPage);

    private async Task LoadPageAsync(int page)
    {
        if (_disposed || IsBusy || page < 1) return;
        IsBusy = true;
        Error = "";

        try
        {
            using var response = await _http.GetAsync($"api/data-sources?page={page}&page_size={PageSize}", _lifetime.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Session expired. Please sign in again.",
                HttpStatusCode.Forbidden => "You do not have access to the news.",
                HttpStatusCode.NotFound => "The news service is unavailable on this server.",
                _ => $"News could not be updated (HTTP {(int)response.StatusCode}). Please retry.",
            });

            var data = await response.Content.ReadFromJsonAsync<NewsResponse>(cancellationToken: _lifetime.Token);
            if (data?.Items is null) throw new JsonException();

            _currentPage = data.Page;
            _totalCount = data.Count;
            Articles.Clear();

            for (var index = 0; index < data.Items.Count; index++)
            {
                var article = data.Items[index];
                article.IsAlternateRow = index % 2 == 1;
                Articles.Add(article);
            }

            Updated = $"Last updated: {DateTime.Now:HH:mm:ss} · Refreshes every 15 minutes";
            NotifyPage();
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (OperationCanceledException) { Error = "The news request timed out. Please retry."; }
        catch (HttpRequestException) { Error = "Cannot reach the news server. Please retry."; }
        catch (JsonException) { Error = "The server returned an unexpected news format."; }
        catch (InvalidOperationException error) { Error = error.Message; }
        finally { if (!_disposed) IsBusy = false; }
    }

    private void NotifyPage()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(IsEmpty));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _lifetime.Cancel();
    }
}
