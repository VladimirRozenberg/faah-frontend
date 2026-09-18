using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using FAAH_Frontend.Models;
namespace FAAH_Frontend.ViewModels;
public sealed class NewsListViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private List<NewsArticle> _snapshot = new();
    private bool _busy, _disposed, _alphabetical, _loaded;
    private string _error = "", _updated = "Not loaded yet";
    public NewsListViewModel(HttpClient http)
    {
        _http = http;
        RefreshCommand = new RelayCommand(parameter => { _ = RefreshAsync(); }, parameter => !IsBusy && !_disposed);
        LatestCommand = new RelayCommand(() => { IsAlphabetical = false; ApplySort(); });
        AlphabeticalCommand = new RelayCommand(() => { IsAlphabetical = true; ApplySort(); });
        _timer.Tick += OnTick;
    }
    public ObservableCollection<NewsArticle> Articles { get; } = new();
    public ICommand RefreshCommand { get; }
    public ICommand LatestCommand { get; }
    public ICommand AlphabeticalCommand { get; }
    public bool IsBusy { get => _busy; private set { SetField(ref _busy, value); NotifyState(); ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged(); } }
    public bool IsAlphabetical { get => _alphabetical; private set { SetField(ref _alphabetical, value); OnPropertyChanged(nameof(IsLatest)); } }
    public bool IsLatest => !IsAlphabetical;
    public string Error { get => _error; private set { SetField(ref _error, value); NotifyState(); } }
    public string Updated { get => _updated; private set => SetField(ref _updated, value); }
    public bool HasError => Error.Length > 0;
    public bool IsEmpty => _loaded && !IsBusy && !HasError && Articles.Count == 0;
    public void Start() { if (_disposed) return; _timer.Start(); _ = RefreshAsync(); }
    private void OnTick(object? sender, EventArgs e) => _ = RefreshAsync();
    private void NotifyState() { OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(IsEmpty)); }
    private void ApplySort()
    {
        var ordered = IsAlphabetical ? _snapshot.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Id)
            : _snapshot.OrderByDescending(a => a.SortDate).ThenByDescending(a => a.Id);
        Articles.Clear(); foreach (var article in ordered) Articles.Add(article);
        NotifyState();
    }
    public async Task RefreshAsync()
    {
        if (_disposed || IsBusy) return;
        IsBusy = true; Error = "";
        try
        {
            using var response = await _http.GetAsync("api/data-sources", _lifetime.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Session expired. Please sign in again.",
                    HttpStatusCode.Forbidden => "You do not have access to the news.",
                    HttpStatusCode.NotFound => "The news service is unavailable on this server.",
                    _ => $"News could not be updated (HTTP {(int)response.StatusCode}). Please retry."
                });
            var data = await response.Content.ReadFromJsonAsync<NewsResponse>(cancellationToken: _lifetime.Token);
            if (_disposed) return;
            if (data?.Items is null || data.Items.Any(a => a is null)) throw new JsonException();
            _snapshot = data.Items; _loaded = true; ApplySort();
            Updated = "Last updated: " + DateTime.Now.ToString("HH:mm:ss") + " · Auto-refresh: 60 s";
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (OperationCanceledException) { Error = "The news request timed out. Please retry."; }
        catch (HttpRequestException) { Error = "Cannot reach the news server. Check your connection and retry."; }
        catch (JsonException) { Error = "The server returned an unexpected news format."; }
        catch (InvalidOperationException ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _timer.Tick -= OnTick; _lifetime.Cancel();
    }
}
