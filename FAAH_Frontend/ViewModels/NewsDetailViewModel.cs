using System;
using System.Collections.Generic;
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

public sealed class NewsDetailViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _http;
    private readonly Action _goBack;
    private readonly Action<Asset>? _openAsset;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy, _disposed;
    private string _error = "";
    private NewsSourceDetailResponse? _detail;

    public NewsDetailViewModel(HttpClient http, int articleId, Action goBack, Action<Asset>? openAsset = null)
    {
        _http = http;
        ArticleId = articleId;
        _goBack = goBack;
        _openAsset = openAsset;
        BackCommand = new RelayCommand(_ => _goBack(), _ => !_disposed);
        RefreshCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => !_disposed && !IsBusy);
        OpenAssetCommand = new RelayCommand(OpenAsset, CanOpenAsset);
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
            OnPropertyChanged(nameof(RelatedAssets));
            OnPropertyChanged(nameof(HasRelatedAssets));
            OnPropertyChanged(nameof(Signals));
            OnPropertyChanged(nameof(HasSignals));
        }
    }
    public ICommand BackCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenAssetCommand { get; }
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
    public IReadOnlyList<ClassificationAsset> RelatedAssets
    {
        get
        {
            if (Detail is null) return Array.Empty<ClassificationAsset>();
            return Detail.Classifications
                .Where(classification => classification.Assets is not null)
                .SelectMany(classification => classification.Assets)
                .Where(asset => !string.IsNullOrWhiteSpace(asset.Symbol))
                .DistinctBy(asset => asset.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
    public bool HasRelatedAssets => RelatedAssets.Count > 0;
    public IReadOnlyList<NewsSignal> Signals
    {
        get
        {
            if (Detail is null) return Array.Empty<NewsSignal>();
            return (Detail.Signals ?? new List<NewsSignal>())
                .Concat(Detail.Analyses
                    .Where(analysis => analysis.Signals is not null)
                    .SelectMany(analysis => analysis.Signals!))
                .Where(signal => signal is not null && signal.Id > 0)
                .DistinctBy(signal => signal.Id)
                .ToList();
        }
    }
    public bool HasSignals => Signals.Count > 0;

    private void OpenAsset(object? parameter)
    {
        int assetId;
        string? symbol;
        string? name;
        switch (parameter)
        {
            case NewsSignal signal:
                assetId = signal.AssetId;
                symbol = signal.AssetSymbol;
                name = signal.AssetName;
                break;
            case ClassificationAsset asset:
                assetId = asset.Id;
                symbol = asset.Symbol;
                name = asset.Name;
                break;
            default:
                return;
        }

        if (string.IsNullOrWhiteSpace(symbol)) return;
        symbol = symbol.Trim();
        _openAsset?.Invoke(new Asset
        {
            Id = assetId,
            Symbol = symbol,
            Name = name,
            LogoUrl = $"/api/assets/{Uri.EscapeDataString(symbol)}/logo"
        });
    }

    private bool CanOpenAsset(object? parameter) => !_disposed && _openAsset is not null && (parameter switch
    {
        NewsSignal signal => signal.AssetId > 0 && !string.IsNullOrWhiteSpace(signal.AssetSymbol),
        ClassificationAsset asset => !string.IsNullOrWhiteSpace(asset.Symbol),
        _ => false
    });

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
