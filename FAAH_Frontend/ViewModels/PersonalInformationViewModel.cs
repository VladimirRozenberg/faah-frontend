using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FAAH_Frontend.Models;
namespace FAAH_Frontend.ViewModels;
public class PersonalInformationViewModel : ViewModelBase, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy, _disposed, _supportsTopUp;
    private string _message = "", _cash = "—", _assets = "—", _depositAmount = "", _pageInput = "1";
    private int _currentPage = 1, _pageCount = 1, _totalCount;
    private int _depositPage = 1, _depositPageCount = 1, _depositTotal;
    private string _depositsMessage = "";
    public User User { get; }
    public bool IsAdmin => _shell.IsAdmin;
    public string Username => User.Username;
    public string Email => User.Email ?? "Not provided";
    public int UserId => User.UserId;
    public bool CanManage => IsAdmin && _shell.ProfileUserId != UserId.ToString();
    public bool IsOwnProfile => _shell.ProfileUserId == UserId.ToString();
    public string Cash { get => _cash; private set => SetField(ref _cash, value); }
    public string Assets { get => _assets; private set => SetField(ref _assets, value); }
    public string Message { get => _message; private set => SetField(ref _message, value); }
    public string DepositAmount { get => _depositAmount; set => SetField(ref _depositAmount, value); }
    public bool IsBusy { get => _busy; private set { SetField(ref _busy, value); foreach (var c in new[] { RefreshCommand, ChangeRoleCommand, ToggleStatusCommand, DepositCommand }) ((RelayCommand)c).RaiseCanExecuteChanged(); } }
    public ObservableCollection<AccountTransaction> Transactions { get; } = new();
    public ObservableCollection<int> PageNumbers { get; } = new();
    public string PageInput { get => _pageInput; set => SetField(ref _pageInput, value); }
    public string PageLabel => $"Page {_currentPage} of {_pageCount} · {_totalCount} transactions";
    public ObservableCollection<AccountDeposit> Deposits { get; } = new();
    public string DepositsMessage { get => _depositsMessage; private set { if (SetField(ref _depositsMessage, value)) OnPropertyChanged(nameof(HasDepositsMessage)); } }
    public bool HasDepositsMessage => _depositsMessage.Length > 0;
    public string DepositPageLabel => $"Page {_depositPage} of {_depositPageCount} · {_depositTotal} deposits";
    public ICommand FirstDepositPageCommand { get; }
    public ICommand PreviousDepositPageCommand { get; }
    public ICommand NextDepositPageCommand { get; }
    public ICommand LastDepositPageCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ChangeRoleCommand { get; }
    public ICommand ToggleStatusCommand { get; }
    public ICommand DepositCommand { get; }
    public ICommand FirstPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand GoToPageCommand { get; }
    public PersonalInformationViewModel(User user, ShellViewModel shell)
    {
        User = user; _shell = shell;
        BackCommand = new RelayCommand(() => { if (shell.IsAdmin) shell.ShowUsers(); else shell.ShowDashboard(); });
        RefreshCommand = new RelayCommand(p => { _ = LoadAsync(); }, p => !IsBusy && !_disposed);
        ChangeRoleCommand = new RelayCommand(p => { _ = UpdateAsync(true); }, p => CanManage && !IsBusy && !_disposed && (User.Role == "admin" || User.Role == "employe"));
        ToggleStatusCommand = new RelayCommand(p => { _ = UpdateAsync(false); }, p => CanManage && !IsBusy && !_disposed && User.IsActive.HasValue);
        DepositCommand = new RelayCommand(p => { _ = DepositAsync(); }, p => IsAdmin && !IsBusy && !_disposed && User.IsActive != false);
        FirstPageCommand = new RelayCommand(p => GoToPage(1), p => !IsBusy && !_disposed && _currentPage > 1);
        PreviousPageCommand = new RelayCommand(p => GoToPage(_currentPage - 1), p => !IsBusy && !_disposed && _currentPage > 1);
        NextPageCommand = new RelayCommand(p => GoToPage(_currentPage + 1), p => !IsBusy && !_disposed && _currentPage < _pageCount);
        LastPageCommand = new RelayCommand(p => GoToPage(_pageCount), p => !IsBusy && !_disposed && _currentPage < _pageCount);
        GoToPageCommand = new RelayCommand(p => GoToPage(p), p => !IsBusy && !_disposed);
        FirstDepositPageCommand = new RelayCommand(p => GoToDepositPage(1), p => !IsBusy && !_disposed && _depositPage > 1);
        PreviousDepositPageCommand = new RelayCommand(p => GoToDepositPage(_depositPage - 1), p => !IsBusy && !_disposed && _depositPage > 1);
        NextDepositPageCommand = new RelayCommand(p => GoToDepositPage(_depositPage + 1), p => !IsBusy && !_disposed && _depositPage < _depositPageCount);
        LastDepositPageCommand = new RelayCommand(p => GoToDepositPage(_depositPageCount), p => !IsBusy && !_disposed && _depositPage < _depositPageCount);
        _ = LoadAsync();
    }
    private static Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return Task.CompletedTask;
        var message = response.StatusCode switch {
            System.Net.HttpStatusCode.Unauthorized => "Session expired. Please sign in again.",
            System.Net.HttpStatusCode.Forbidden => "Access denied: insufficient permissions.",
            System.Net.HttpStatusCode.NotFound => "This account feature is not available yet.",
            System.Net.HttpStatusCode.BadRequest => "The requested account change was refused.",
            _ => $"Request refused (HTTP {(int)response.StatusCode})." };
        throw new InvalidOperationException(message);
    }
    private async Task ReadAccountAsync()
    {
        if (!IsOwnProfile)
        {
            Cash = "Not available"; Assets = "Not available";
            Transactions.Clear(); Deposits.Clear();
            return;
        }
        // Cash is account-level (not multiplied by portfolios).
        using var cashResponse = await _shell.Http.GetAsync("api/users/me/available-cash", _lifetime.Token);
        await EnsureAsync(cashResponse);
        var cash = await cashResponse.Content.ReadFromJsonAsync<AvailableCashResponse>(ShellViewModel.JsonOptions, _lifetime.Token);
        _supportsTopUp = cash?.AvailableCash.HasValue == true;
        Cash = cash?.AvailableCash.HasValue == true ? $"{cash.AvailableCash:N2} {cash.Currency}" : "Unavailable";
        using var valueResponse = await _shell.Http.GetAsync("api/users/me/asset-value", _lifetime.Token);
        await EnsureAsync(valueResponse);
        var value = await valueResponse.Content.ReadFromJsonAsync<AssetValueResponse>(ShellViewModel.JsonOptions, _lifetime.Token);
        Assets = value?.TotalCurrentValue.HasValue == true ? $"{value.TotalCurrentValue:N2} {value.Currency}" : "Prices unavailable";
        await ReadTransactionsAsync(_currentPage);
        await ReadDepositsAsync(_depositPage);
    }
    private async Task ReadDepositsAsync(int page)
    {
        try
        {
            using var response = await _shell.Http.GetAsync($"api/users/me/deposits?page={page}", _lifetime.Token);
            await EnsureAsync(response);
            var result = await response.Content.ReadFromJsonAsync<DepositPage>(ShellViewModel.JsonOptions, _lifetime.Token);
            Deposits.Clear();
            foreach (var item in result?.Deposits ?? new()) { item.IsAlternateRow = Deposits.Count % 2 == 1; Deposits.Add(item); }
            _depositPageCount = Math.Max(1, result?.TotalPages ?? 1);
            _depositPage = Math.Clamp(result?.Page > 0 ? result.Page : page, 1, _depositPageCount);
            _depositTotal = result?.Count ?? Deposits.Count;
            DepositsMessage = Deposits.Count == 0 ? "No deposits yet." : "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_lifetime.IsCancellationRequested)
        {
            Deposits.Clear();
            DepositsMessage = "Unable to load deposits: " + FriendlyError(ex);
        }
        OnPropertyChanged(nameof(DepositPageLabel));
    }
    private void GoToDepositPage(int page)
    {
        if (IsBusy || _disposed) return;
        page = Math.Clamp(page, 1, _depositPageCount);
        if (page == _depositPage) return;
        _ = LoadDepositPageAsync(page);
    }
    private async Task LoadDepositPageAsync(int page)
    {
        IsBusy = true;
        try { await ReadDepositsAsync(page); }
        finally { IsBusy = false; RaisePageCommandState(); }
    }
    private async Task ReadTransactionsAsync(int page)
    {
        using var historyResponse = await _shell.Http.GetAsync($"api/users/me/transactions?page={page}", _lifetime.Token);
        await EnsureAsync(historyResponse);
        var history = await historyResponse.Content.ReadFromJsonAsync<TransactionPage>(ShellViewModel.JsonOptions, _lifetime.Token);
        Transactions.Clear();
        foreach (var item in history?.Transactions ?? new()) { item.IsAlternateRow = Transactions.Count % 2 == 1; Transactions.Add(item); }
        _pageCount = Math.Max(1, history?.TotalPages ?? 1);
        _currentPage = Math.Clamp(history?.Page > 0 ? history.Page : page, 1, _pageCount);
        _totalCount = history?.Count ?? Transactions.Count;
        PageInput = _currentPage.ToString();
        UpdatePageNumbers();
        OnPropertyChanged(nameof(PageLabel));
    }
    private void UpdatePageNumbers()
    {
        const int windowSize = 9;
        int maxStart = Math.Max(1, _pageCount - windowSize + 1);
        int start = Math.Clamp(_currentPage - 2, 1, maxStart);
        PageNumbers.Clear();
        for (int page = start; page < start + windowSize && page <= _pageCount; page++) PageNumbers.Add(page);
    }
    private void RaisePageCommandState()
    {
        foreach (var c in new[] { FirstPageCommand, PreviousPageCommand, NextPageCommand, LastPageCommand, GoToPageCommand, FirstDepositPageCommand, PreviousDepositPageCommand, NextDepositPageCommand, LastDepositPageCommand }) ((RelayCommand)c).RaiseCanExecuteChanged();
    }
    private void GoToPage(object? parameter)
    {
        if (IsBusy || _disposed || !int.TryParse(Convert.ToString(parameter, CultureInfo.InvariantCulture), out var page)) { PageInput = _currentPage.ToString(); return; }
        page = Math.Clamp(page, 1, _pageCount);
        if (page == _currentPage) { PageInput = page.ToString(); return; }
        _ = LoadPageAsync(page);
    }
    private async Task LoadPageAsync(int page)
    {
        IsBusy = true; Message = "";
        try { await ReadTransactionsAsync(page); }
        catch (Exception ex) { if (!_disposed) Message = "Unable to load transactions: " + FriendlyError(ex); }
        finally { IsBusy = false; RaisePageCommandState(); }
    }
    public async Task LoadAsync()
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; Message = "";
        try { await ReadAccountAsync(); Message = !IsOwnProfile ? "Balance, transactions and deposits are only available for your own profile." : !_supportsTopUp ? "Cash balances and top-ups are not available from the account service yet." : Transactions.Count == 0 ? "No transactions yet." : ""; }
        catch (Exception ex) { if (!_disposed) Message = "Unable to load account: " + FriendlyError(ex); }
        finally { IsBusy = false; RaisePageCommandState(); }
    }
    private async Task UpdateAsync(bool role)
    {
        if (!CanManage || IsBusy || _disposed) return;
        IsBusy = true;
        try {
            object body = role ? new { role = User.IsAdminRole ? "employe" : "admin" } : new { is_active = User.IsActive != true };
            using var response = await _shell.Http.PutAsJsonAsync($"admin/utilisateurs/{UserId}/{(role ? "role" : "statut")}", body, ShellViewModel.JsonOptions, _lifetime.Token);
            await EnsureAsync(response);
            var updated = await response.Content.ReadFromJsonAsync<User>(ShellViewModel.JsonOptions, _lifetime.Token) ?? throw new InvalidOperationException("Empty response.");
            User.Role = updated.Role; User.IsActive = updated.IsActive; Message = "Account updated.";
        } catch (Exception ex) { if (!_disposed) Message = FriendlyError(ex); }
        finally { IsBusy = false; }
    }
    private async Task DepositAsync()
    {
        if (!IsAdmin || User.IsActive == false || IsBusy || _disposed) return;
        if (!decimal.TryParse(DepositAmount.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || amount > 1000000000 || decimal.Round(amount, 2) != amount) { Message = "Enter a positive USD amount with up to two decimal places."; return; }
        IsBusy = true;
        try {
            using var response = await _shell.Http.PostAsJsonAsync($"admin/utilisateurs/{UserId}/deposit", new { amount }, ShellViewModel.JsonOptions, _lifetime.Token);
            await EnsureAsync(response);
            DepositAmount = ""; Message = "Simulated funds added.";
            try { await ReadAccountAsync(); } catch { Message = "Simulated funds added. Refresh to see the updated balance."; }
        } catch (Exception ex) { if (!_disposed) Message = "Top-up failed: " + FriendlyError(ex); }
        finally { IsBusy = false; }
    }
    private static string FriendlyError(Exception error) => error switch {
        InvalidOperationException => error.Message,
        OperationCanceledException => "The request timed out. Please try again.",
        HttpRequestException => "The server could not be reached.",
        _ => "An unexpected response was received. Please refresh." };
    public void Dispose() { _disposed = true; _lifetime.Cancel(); }
}
