using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly CancellationTokenSource _lifetime = new();
    private List<RecentRecommendation> _pageItems = new();
    private bool _busy, _disposed, _failed, _recentOnly = true;
    private int _page = 1, _pageCount = 1, _totalCount, _selectedPageSize = 10;
    private string _action = "all", _selectedKind = "All";
    private string _message = "", _accountMessage = "", _cash = "—", _assets = "—";

    public ObservableCollection<RecentRecommendation> Recommendations { get; } = new();
    public ObservableCollection<int> PageNumbers { get; } = new();
    public string[] KindOptions { get; } = { "All", "opportunity", "holding_assessment", "targeted_conclusion" };
    public int[] PageSizeOptions { get; } = { 10, 50, 100 };
    public ICommand RefreshCommand { get; }
    public ICommand FirstPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand GoToPageCommand { get; }
    public ICommand ProfileCommand => _shell.ShowProfileCommand;

    public bool IsEmpty => !IsBusy && !_failed && Recommendations.Count == 0;
    public int PageCount => _pageCount;
    public int TotalCount => _totalCount;
    public string PageLabel => $"Page {_page} of {_pageCount} · {_totalCount} recommendations";

    public string SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (_disposed || !SetField(ref _selectedKind, value ?? "All")) return;
            ResetPageAndLoad();
        }
    }

    public bool RecentOnly
    {
        get => _recentOnly;
        set
        {
            if (_disposed || !SetField(ref _recentOnly, value)) return;
            ResetPageAndLoad();
        }
    }

    public int SelectedPageSize
    {
        get => _selectedPageSize;
        set
        {
            if (_disposed || !PageSizeOptions.Contains(value) || !SetField(ref _selectedPageSize, value)) return;
            ResetPageAndLoad();
        }
    }

    public string Message { get => _message; private set => SetField(ref _message, value); }
    public string AccountMessage { get => _accountMessage; private set => SetField(ref _accountMessage, value); }
    public string Cash { get => _cash; private set => SetField(ref _cash, value); }
    public string Assets { get => _assets; private set => SetField(ref _assets, value); }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!SetField(ref _busy, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
            RaiseCommandStates();
        }
    }

    public DashboardViewModel(ShellViewModel shell)
    {
        _shell = shell;
        RefreshCommand = new RelayCommand(_ => { _ = LoadAsync(); }, _ => !IsBusy && !_disposed);
        FirstPageCommand = new RelayCommand(_ => GoToPage(1), _ => !IsBusy && !_disposed && _page > 1);
        PreviousPageCommand = new RelayCommand(_ => GoToPage(_page - 1), _ => !IsBusy && !_disposed && _page > 1);
        NextPageCommand = new RelayCommand(_ => GoToPage(_page + 1), _ => !IsBusy && !_disposed && _page < _pageCount);
        LastPageCommand = new RelayCommand(_ => GoToPage(_pageCount), _ => !IsBusy && !_disposed && _page < _pageCount);
        GoToPageCommand = new RelayCommand(GoToPage, parameter => !IsBusy && !_disposed
            && int.TryParse(parameter?.ToString(), out var page) && page >= 1 && page <= _pageCount);
        _ = LoadAsync();
    }

    private void FilterAction(string action)
    {
        if (_disposed) return;
        _action = action;
        Recommendations.Clear();
        var visibleItems = _pageItems.Where(item => action == "all"
            || string.Equals(item.Action, action, StringComparison.OrdinalIgnoreCase)).ToList();
        for (var index = 0; index < visibleItems.Count; index++)
        {
            visibleItems[index].IsAlternateRow = index % 2 == 1;
            Recommendations.Add(visibleItems[index]);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    public Task LoadAsync() => LoadPageAsync(refreshAccount: true);
    private async Task LoadPageAsync(bool refreshAccount)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        Message = "";
        if (refreshAccount) AccountMessage = "";
        _failed = false;
        _pageItems.Clear();
        FilterAction(_action);

        try
        {
            if (refreshAccount) await LoadAccountAsync();
            if (_disposed) return;

            var userId = _shell.ProfileUserId;
            var query = new List<string> { $"page={_page}", $"page_size={_selectedPageSize}" };
            if (RecentOnly) query.Add("within=1h");
            if (SelectedKind != "All") query.Add("kind=" + Uri.EscapeDataString(SelectedKind));

            var response = await _shell.Http.GetFromJsonAsync<RecentRecommendationResponse>(
                $"api/users/me/recommendations?{string.Join("&", query)}", ShellViewModel.JsonOptions, _lifetime.Token);
            if (_disposed) return;

            _pageItems = response?.Items ?? new List<RecentRecommendation>();
            _totalCount = response?.Count ?? 0;
            if (response?.PageSize > 0) _selectedPageSize = response.PageSize;
            _pageCount = Math.Max(1, (int)Math.Ceiling(_totalCount / (double)_selectedPageSize));
            _page = response?.Page > 0 ? response.Page : _page;
            _page = Math.Clamp(_page, 1, _pageCount);
            UpdatePageNumbers();
            OnPropertyChanged(nameof(SelectedPageSize));
            OnPropertyChanged(nameof(PageCount));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(PageLabel));
            FilterAction(_action);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!_disposed)
            {
                _failed = true;
                Message = FriendlyError(ex, "Recommendations");
            }
        }
        finally
        {
            if (!_disposed) IsBusy = false;
        }
    }

    private async Task LoadAccountAsync()
    {
        try
        {
            var cash = await _shell.Http.GetFromJsonAsync<AvailableCashResponse>(
                "api/users/me/available-cash", ShellViewModel.JsonOptions, _lifetime.Token);
            if (!_disposed) Cash = cash?.AvailableCash.HasValue == true ? $"{cash.AvailableCash:N2} {cash.Currency}" : "Unavailable";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!_disposed) { Cash = "—"; AccountMessage = FriendlyError(ex, "Available cash"); }
        }

        try
        {
            var value = await _shell.Http.GetFromJsonAsync<AssetValueResponse>(
                "api/users/me/asset-value", ShellViewModel.JsonOptions, _lifetime.Token);
            if (!_disposed && value is not null)
            {
                Assets = value.TotalCurrentValue.HasValue ? $"{value.TotalCurrentValue:N2} {value.Currency}" : "Prices unavailable";
                if (!value.ValuationComplete && value.MissingPriceSymbols.Count > 0)
                    AccountMessage = $"Prices unavailable for: {string.Join(", ", value.MissingPriceSymbols)}.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!_disposed) { Assets = "—"; AccountMessage = FriendlyError(ex, "Asset value"); }
        }
    }

    private void ResetPageAndLoad()
    {
        _page = 1;
        OnPropertyChanged(nameof(PageLabel));
        RaiseCommandStates();
        _ = LoadPageAsync(refreshAccount: false);
    }

    private void GoToPage(object? parameter)
    {
        if (int.TryParse(parameter?.ToString(), out var page)) GoToPage(page);
    }

    private void UpdatePageNumbers()
    {
        const int windowSize = 5;
        var start = Math.Clamp(_page - 2, 1, Math.Max(1, _pageCount - windowSize + 1));
        PageNumbers.Clear();
        for (var page = start; page < start + windowSize && page <= _pageCount; page++)
            PageNumbers.Add(page);
    }

    private void GoToPage(int page)
    {
        if (IsBusy || _disposed || page < 1 || page > _pageCount) return;
        _page = page;
        OnPropertyChanged(nameof(PageLabel));
        RaiseCommandStates();
        _ = LoadPageAsync(refreshAccount: false);
    }

    private void RaiseCommandStates()
    {
        ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((RelayCommand)FirstPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)LastPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)GoToPageCommand).RaiseCanExecuteChanged();
    }

    private static string FriendlyError(Exception error, string section) => error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }
        ? "Your session has expired. Please sign in again."
        : $"{section} could not be loaded. Please refresh or try again later.";

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
    }
}