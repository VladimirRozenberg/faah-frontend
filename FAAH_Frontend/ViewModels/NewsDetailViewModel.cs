using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public sealed class NewsDetailViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly Action _goBack;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy, _disposed;
    private string _error = "";
    private NewsSourceDetailResponse? _detail;

    public NewsDetailViewModel(HttpClient http, int articleId, Action goBack)
    {
        _http = http;
        ArticleId = articleId;
        _goBack = goBack;
        BackCommand = new RelayCommand(_ => _goBack(), _ => !_disposed);
        RefreshCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => !_disposed && !IsBusy);
    }

    public int ArticleId { get; }
    public NewsSourceDetailResponse? Detail
    {
        get => _detail;
        private set
        {
            if (!SetField(ref _detail, value)) return;
            OnPropertyChanged(nameof(ContentPreview));
            OnPropertyChanged(nameof(PublishedDisplay));
        }
    }
    public ICommand BackCommand { get; }
    public ICommand RefreshCommand { get; }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!SetField(ref _busy, value)) return;
            ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }
    public string Error
    {
        get => _error;
        private set
        {
            if (!SetField(ref _error, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool HasDetail => Detail is not null;
    public string ContentPreview
    {
        get
        {
            var content = Detail?.Source.Content;
            if (string.IsNullOrWhiteSpace(content)) return "No source content available.";
            return content.Length > 700 ? content[..700] + "…" : content;
        }
    }
    public string PublishedDisplay => Detail?.Source.PublishedAt?.ToString("yyyy-MM-dd HH:mm") ?? "Date unavailable";

    public void Start() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (_disposed || IsBusy) return;
        IsBusy = true;
        Error = "";
        try
        {
            using var response = await _http.GetAsync($"api/data-sources/{ArticleId}", _lifetime.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Session expired. Please sign in again.",
                    HttpStatusCode.Forbidden => "You do not have access to this news source.",
                    HttpStatusCode.NotFound => "This news source could not be found.",
                    _ => $"The news source could not be loaded (HTTP {(int)response.StatusCode}). Please retry."
                });

            var detail = await response.Content.ReadFromJsonAsync<NewsSourceDetailResponse>(cancellationToken: _lifetime.Token);
            if (detail?.Source is null) throw new JsonException();
            Detail = detail;
            OnPropertyChanged(nameof(HasDetail));
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (OperationCanceledException) { Error = "The article request timed out. Please retry."; }
        catch (HttpRequestException) { Error = "Cannot reach the news server. Check your connection and retry."; }
        catch (JsonException) { Error = "The server returned an unexpected source-analysis format."; }
        catch (InvalidOperationException ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
