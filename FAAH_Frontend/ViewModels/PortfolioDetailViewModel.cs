using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public sealed class PortfolioDetailViewModel : ViewModelBase, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly CancellationTokenSource _lifetime = new();
    private int _currentPage = 1, _pageCount = 1, _totalCount;
    private int _transactionPage = 1, _transactionPageCount = 1;
    private string _selectedKind = "All", _pageInput = "1";
    private bool _isLoading, _isLoadingPositions, _isLoadingTransactions, _disposed;
    private bool _favoritesLoaded;
    private readonly HashSet<int> _favoriteUpdates = new();
    private string _errorMessage = "", _positionsErrorMessage = "", _transactionsErrorMessage = "", _favoritesErrorMessage = "";

    public const int PageSize = 10;
    public const int TransactionPageSize = 14;
    public Portfolio Portfolio { get; }
    public ObservableCollection<RecentRecommendation> Recommendations { get; } = new();
    public ObservableCollection<PortfolioPosition> Positions { get; } = new();
    public ObservableCollection<PortfolioTransaction> Transactions { get; } = new();
    public ObservableCollection<int> PageNumbers { get; } = new();
    public ObservableCollection<int> TransactionPageNumbers { get; } = new();
    public string[] KindOptions { get; } = { "All types", "Opportunity", "Holding assessment", "Targeted conclusion" };
    private static readonly string[] KindValues = { "All", "opportunity", "holding_assessment", "targeted_conclusion" };

    public RelayCommand RefreshCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand OpenAssetCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand ToggleStatusCommand { get; }
    public RelayCommand EditPortfolioCommand { get; }
    public RelayCommand FirstPageCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand LastPageCommand { get; }
    public RelayCommand GoToPageCommand { get; }
    public RelayCommand FirstTransactionPageCommand { get; }
    public RelayCommand PreviousTransactionPageCommand { get; }
    public RelayCommand NextTransactionPageCommand { get; }
    public RelayCommand LastTransactionPageCommand { get; }
    public RelayCommand GoToTransactionPageCommand { get; }

    public bool HasRecommendations => Recommendations.Count > 0;
    public bool IsEmpty => !IsLoading && !HasError && !HasRecommendations;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasPositions => Positions.Count > 0;
    public string TotalPositionValueDisplay
    {
        get
        {
            if (Positions.Count == 0) return "—";
            decimal total = 0;
            foreach (var position in Positions)
            {
                if (position.CurrentValue is not decimal value) return "Value unavailable";
                total += value;
            }
            return $"{total:N2} {Portfolio.BaseCurrency}";
        }
    }
    public bool IsPositionsEmpty => !IsLoadingPositions && string.IsNullOrEmpty(PositionsErrorMessage) && !HasPositions;
    public bool HasPositionsError => !string.IsNullOrEmpty(PositionsErrorMessage);
    public string FavoritesErrorMessage
    {
        get => _favoritesErrorMessage;
        private set
        {
            if (!SetField(ref _favoritesErrorMessage, value)) return;
            OnPropertyChanged(nameof(HasFavoritesError));
        }
    }
    public bool HasFavoritesError => !string.IsNullOrEmpty(FavoritesErrorMessage);
    public bool HasTransactions => Transactions.Count > 0;
    public bool IsTransactionsEmpty => !IsLoadingTransactions && string.IsNullOrEmpty(TransactionsErrorMessage) && !HasTransactions;
    public bool HasTransactionsError => !string.IsNullOrEmpty(TransactionsErrorMessage);
    public int TransactionCount { get; private set; }
    public string TransactionPageLabel => $"Page {_transactionPage} of {_transactionPageCount} · {TransactionCount} transactions";
    public bool HasMultipleTransactionPages => _transactionPageCount > 1;
    public int CurrentPage => _currentPage;
    public int PageCount => _pageCount;
    public int TotalCount => _totalCount;
    public string PageInput { get => _pageInput; set => SetField(ref _pageInput, value); }
    public string PageLabel => $"Page {_currentPage} of {_pageCount} · {_totalCount} recommendations";

    public string SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (_isLoading || !SetField(ref _selectedKind, value ?? "All")) return;
            OnPropertyChanged(nameof(SelectedKindDisplay));
            _currentPage = 1;
            _ = LoadAsync();
        }
    }

    public string SelectedKindDisplay
    {
        get
        {
            var index = Array.IndexOf(KindValues, SelectedKind);
            return KindOptions[index >= 0 ? index : 0];
        }
        set
        {
            var index = Array.IndexOf(KindOptions, value);
            if (index >= 0) SelectedKind = KindValues[index];
        }
    }

    public bool IsLoadingTransactions
    {
        get => _isLoadingTransactions;
        private set
        {
            if (!SetField(ref _isLoadingTransactions, value)) return;
            OnPropertyChanged(nameof(IsTransactionsEmpty));
            RefreshCommand.RaiseCanExecuteChanged();
            RaiseTransactionPageCommandStates();
        }
    }

    public string TransactionsErrorMessage
    {
        get => _transactionsErrorMessage;
        private set
        {
            if (!SetField(ref _transactionsErrorMessage, value)) return;
            OnPropertyChanged(nameof(HasTransactionsError));
            OnPropertyChanged(nameof(IsTransactionsEmpty));
        }
    }

    public bool IsLoadingPositions
    {
        get => _isLoadingPositions;
        private set
        {
            if (!SetField(ref _isLoadingPositions, value)) return;
            OnPropertyChanged(nameof(IsPositionsEmpty));
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    public string PositionsErrorMessage
    {
        get => _positionsErrorMessage;
        private set
        {
            if (!SetField(ref _positionsErrorMessage, value)) return;
            OnPropertyChanged(nameof(HasPositionsError));
            OnPropertyChanged(nameof(IsPositionsEmpty));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetField(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
            RaiseCommandStates();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetField(ref _errorMessage, value)) return;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public PortfolioDetailViewModel(ShellViewModel shell, Portfolio portfolio)
    {
        _shell = shell;
        Portfolio = portfolio;
        BackCommand = new RelayCommand(_ => _shell.ShowPortfolios(), _ => !_disposed);
        EditPortfolioCommand = new RelayCommand(_ => _shell.ShowPortfolioEdit(Portfolio));
        ToggleStatusCommand = new RelayCommand(parameter =>
        {
            if (parameter is Portfolio selectedPortfolio)
                _ = _shell.UpdatePortfolioActiveStateAsync(selectedPortfolio, selectedPortfolio.IsActive, !selectedPortfolio.IsActive);
        }, parameter => parameter is Portfolio selectedPortfolio && !selectedPortfolio.IsStatusUpdating);
        OpenAssetCommand = new RelayCommand(parameter =>
        {
            if (parameter is PortfolioPosition position)
                _shell.ShowAssetDetail(new Asset
                {
                    Id = position.AssetId,
                    Symbol = position.Symbol,
                    Name = position.Name,
                    Type = position.Type,
                    Currency = Portfolio.BaseCurrency,
                    LogoUrl = $"/api/assets/{Uri.EscapeDataString(position.Symbol)}/logo"
                }, Portfolio);
            else if (parameter is RecentRecommendation recommendation && !string.IsNullOrWhiteSpace(recommendation.AssetSymbol))
                _shell.ShowAssetDetail(new Asset
                {
                    Id = recommendation.AssetId ?? 0,
                    Symbol = recommendation.AssetSymbol,
                    Currency = Portfolio.BaseCurrency,
                    LogoUrl = $"/api/assets/{Uri.EscapeDataString(recommendation.AssetSymbol)}/logo"
                }, Portfolio);
        }, parameter => !_disposed && (parameter is PortfolioPosition
            || parameter is RecentRecommendation { AssetSymbol: not null and not "" }));
        ToggleFavoriteCommand = new RelayCommand(parameter =>
        {
            if (parameter is PortfolioPosition position) _ = ToggleFavoriteAsync(position);
        }, parameter => !_disposed && _favoritesLoaded && parameter is PortfolioPosition position
            && !_favoriteUpdates.Contains(position.AssetId));
        Recommendations.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRecommendations));
            OnPropertyChanged(nameof(IsEmpty));
        };
        Positions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPositions));
            OnPropertyChanged(nameof(TotalPositionValueDisplay));
            OnPropertyChanged(nameof(IsPositionsEmpty));
        };
        Transactions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTransactions));
            OnPropertyChanged(nameof(IsTransactionsEmpty));
        };
        RefreshCommand = new RelayCommand(_ => _ = RefreshAllAsync(), _ => !IsLoading && !IsLoadingPositions && !IsLoadingTransactions && !_disposed);
        FirstPageCommand = new RelayCommand(_ => GoToPage(1), _ => !_disposed && !IsLoading && _currentPage > 1);
        PreviousPageCommand = new RelayCommand(_ => GoToPage(_currentPage - 1), _ => !_disposed && !IsLoading && _currentPage > 1);
        NextPageCommand = new RelayCommand(_ => GoToPage(_currentPage + 1), _ => !_disposed && !IsLoading && _currentPage < _pageCount);
        LastPageCommand = new RelayCommand(_ => GoToPage(_pageCount), _ => !_disposed && !IsLoading && _currentPage < _pageCount);
        GoToPageCommand = new RelayCommand(GoToPage, parameter => !_disposed && !IsLoading && TryGetPage(parameter, out var page) && page >= 1 && page <= _pageCount);
        FirstTransactionPageCommand = new RelayCommand(_ => GoToTransactionPage(1), _ => !_disposed && !IsLoadingTransactions && _transactionPage > 1);
        PreviousTransactionPageCommand = new RelayCommand(_ => GoToTransactionPage(_transactionPage - 1), _ => !_disposed && !IsLoadingTransactions && _transactionPage > 1);
        NextTransactionPageCommand = new RelayCommand(_ => GoToTransactionPage(_transactionPage + 1), _ => !_disposed && !IsLoadingTransactions && _transactionPage < _transactionPageCount);
        LastTransactionPageCommand = new RelayCommand(_ => GoToTransactionPage(_transactionPageCount), _ => !_disposed && !IsLoadingTransactions && _transactionPage < _transactionPageCount);
        GoToTransactionPageCommand = new RelayCommand(parameter =>
        {
            if (TryGetPage(parameter, out var page)) GoToTransactionPage(page);
        }, parameter => !_disposed && !IsLoadingTransactions && TryGetPage(parameter, out var page) && page >= 1 && page <= _transactionPageCount);
        _ = RefreshAllAsync();
    }

    private Task RefreshAllAsync() => Task.WhenAll(LoadAsync(), LoadPortfolioDetailsAsync(), LoadTransactionsAsync());

    public async Task LoadAsync()
    {
        if (_disposed || IsLoading) return;
        IsLoading = true;
        ErrorMessage = "";
        Recommendations.Clear();
        try
        {
            var query = $"page={_currentPage}&page_size={PageSize}";
            if (_selectedKind != "All") query += $"&kind={Uri.EscapeDataString(_selectedKind)}";
            var response = await _shell.Http.GetFromJsonAsync<RecentRecommendationResponse>(
                $"api/users/me/portfolios/{Portfolio.Id}/recommendations?{query}", ShellViewModel.JsonOptions, _lifetime.Token);

            _totalCount = response?.Count ?? 0;
            _pageCount = Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));
            _currentPage = response?.Page > 0 ? response.Page : _currentPage;
            _currentPage = Math.Clamp(_currentPage, 1, _pageCount);
            PageInput = _currentPage.ToString();
            var recommendationIndex = 0;
            foreach (var recommendation in response?.Items ?? new())
            {
                recommendation.IsAlternateRow = recommendationIndex++ % 2 == 1;
                Recommendations.Add(recommendation);
            }
            UpdatePageNumbers();
            NotifyPaging();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            if (!_disposed) ErrorMessage = "Recommendations were not found for this portfolio.";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!_disposed) ErrorMessage = "Could not load recommendations. Try refreshing.";
        }
        finally
        {
            if (!_disposed) IsLoading = false;
        }
    }

    private async Task LoadPortfolioDetailsAsync()
    {
        if (_disposed || IsLoadingPositions) return;
        IsLoadingPositions = true;
        PositionsErrorMessage = "";
        Positions.Clear();
        _favoritesLoaded = false;
        FavoritesErrorMessage = "";
        ToggleFavoriteCommand.RaiseCanExecuteChanged();
        try
        {
            var detail = await _shell.Http.GetFromJsonAsync<PortfolioDetail>(
                $"api/users/me/portfolios/{Portfolio.Id}", ShellViewModel.JsonOptions, _lifetime.Token);
            if (detail?.IsActive is bool isActive && !Portfolio.IsStatusUpdating)
                Portfolio.IsActive = isActive;
            var positionIndex = 0;
            foreach (var position in detail?.Positions ?? new())
            {
                position.IsAlternateRow = positionIndex++ % 2 == 1;
                Positions.Add(position);
            }
            await LoadFavoritesAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            if (!_disposed) PositionsErrorMessage = "Portfolio details were not found.";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!_disposed) PositionsErrorMessage = "Could not load portfolio positions. Try refreshing.";
        }
        finally
        {
            if (!_disposed) IsLoadingPositions = false;
        }
    }

    private async Task LoadFavoritesAsync()
    {
        try
        {
            var favorites = await _shell.Http.GetFromJsonAsync<FavoriteResponse>(
                "api/favorites", ShellViewModel.JsonOptions, _lifetime.Token)
                ?? throw new System.Text.Json.JsonException();
            if (_disposed) return;
            var favoriteIds = new HashSet<int>(favorites.AssetIds);
            foreach (var position in Positions)
                position.IsFavorite = favoriteIds.Contains(position.AssetId);
            _favoritesLoaded = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!_disposed) FavoritesErrorMessage = "Could not load favorites. Try refreshing.";
        }
        finally
        {
            if (!_disposed) ToggleFavoriteCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task ToggleFavoriteAsync(PortfolioPosition position)
    {
        if (_disposed || !_favoritesLoaded || !_favoriteUpdates.Add(position.AssetId)) return;
        ToggleFavoriteCommand.RaiseCanExecuteChanged();
        FavoritesErrorMessage = "";
        try
        {
            var add = !position.IsFavorite;
            using var request = new HttpRequestMessage(add ? HttpMethod.Put : HttpMethod.Delete, $"api/favorites/{position.AssetId}");
            using var response = await _shell.Http.SendAsync(request, _lifetime.Token);
            response.EnsureSuccessStatusCode();
            if (!_disposed) position.IsFavorite = add;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (!_disposed) FavoritesErrorMessage = "Could not update favorite. Try again.";
        }
        finally
        {
            _favoriteUpdates.Remove(position.AssetId);
            if (!_disposed) ToggleFavoriteCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task LoadTransactionsAsync()
    {
        if (_disposed || IsLoadingTransactions) return;
        IsLoadingTransactions = true;
        TransactionsErrorMessage = "";
        Transactions.Clear();
        try
        {
            var response = await _shell.Http.GetFromJsonAsync<PortfolioTransactionsResponse>(
                $"api/users/me/portfolios/{Portfolio.Id}/transactions?page={_transactionPage}&page_size={TransactionPageSize}", ShellViewModel.JsonOptions, _lifetime.Token);
            TransactionCount = response?.Count ?? 0;
            _transactionPage = Math.Max(1, response?.Page ?? _transactionPage);
            _transactionPageCount = Math.Max(1, response?.TotalPages ?? (int)Math.Ceiling(TransactionCount / (double)TransactionPageSize));
            OnPropertyChanged(nameof(TransactionCount));
            OnPropertyChanged(nameof(TransactionPageLabel));
            OnPropertyChanged(nameof(HasMultipleTransactionPages));
            UpdateTransactionPageNumbers();
            var transactionIndex = 0;
            foreach (var transaction in response?.Transactions ?? new())
            {
                transaction.IsAlternateRow = transactionIndex++ % 2 == 1;
                Transactions.Add(transaction);
            }
            RaiseTransactionPageCommandStates();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            if (!_disposed) TransactionsErrorMessage = "Transactions were not found for this portfolio.";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!_disposed) TransactionsErrorMessage = "Could not load portfolio transactions. Try refreshing.";
        }
        finally
        {
            if (!_disposed) IsLoadingTransactions = false;
        }
    }

    private void GoToTransactionPage(int page)
    {
        if (IsLoadingTransactions || _disposed || page < 1 || page > _transactionPageCount || page == _transactionPage) return;
        _transactionPage = page;
        _ = LoadTransactionsAsync();
    }

    private void UpdateTransactionPageNumbers()
    {
        const int windowSize = 5;
        var start = Math.Clamp(_transactionPage - 2, 1, Math.Max(1, _transactionPageCount - windowSize + 1));
        TransactionPageNumbers.Clear();
        for (var page = start; page < start + windowSize && page <= _transactionPageCount; page++)
            TransactionPageNumbers.Add(page);
    }

    private void RaiseTransactionPageCommandStates()
    {
        foreach (var command in new[] { FirstTransactionPageCommand, PreviousTransactionPageCommand, NextTransactionPageCommand, LastTransactionPageCommand, GoToTransactionPageCommand })
            command?.RaiseCanExecuteChanged();
    }

    private void GoToPage(object? parameter)
    {
        if (!TryGetPage(parameter, out var page) || page < 1 || page > _pageCount || page == _currentPage) return;
        _currentPage = page;
        _ = LoadAsync();
    }

    private static bool TryGetPage(object? parameter, out int page)
    {
        if (parameter is int value) { page = value; return true; }
        return int.TryParse(parameter?.ToString(), out page);
    }

    private void UpdatePageNumbers()
    {
        const int windowSize = 5;
        var start = Math.Clamp(_currentPage - 2, 1, Math.Max(1, _pageCount - windowSize + 1));
        PageNumbers.Clear();
        for (var page = start; page < start + windowSize && page <= _pageCount; page++) PageNumbers.Add(page);
    }

    private void NotifyPaging()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PageLabel));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        if (RefreshCommand is not null) RefreshCommand.RaiseCanExecuteChanged();
        if (FirstPageCommand is not null) FirstPageCommand.RaiseCanExecuteChanged();
        if (PreviousPageCommand is not null) PreviousPageCommand.RaiseCanExecuteChanged();
        if (NextPageCommand is not null) NextPageCommand.RaiseCanExecuteChanged();
        if (LastPageCommand is not null) LastPageCommand.RaiseCanExecuteChanged();
        if (GoToPageCommand is not null) GoToPageCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
