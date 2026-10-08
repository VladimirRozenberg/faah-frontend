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
    private string _message = "", _cash = "—", _assets = "—", _depositAmount = "";
    public User User { get; }
    public bool IsAdmin => _shell.IsAdmin;
    public string Username => User.Username;
    public string Email => User.Email ?? "Not provided";
    public int UserId => User.UserId;
    public bool CanManage => IsAdmin && _shell.ProfileUserId != UserId.ToString();
    public string Cash { get => _cash; private set => SetField(ref _cash, value); }
    public string Assets { get => _assets; private set => SetField(ref _assets, value); }
    public string Message { get => _message; private set => SetField(ref _message, value); }
    public string DepositAmount { get => _depositAmount; set => SetField(ref _depositAmount, value); }
    public bool IsBusy { get => _busy; private set { SetField(ref _busy, value); foreach (var c in new[] { RefreshCommand, ChangeRoleCommand, ToggleStatusCommand, DepositCommand }) ((RelayCommand)c).RaiseCanExecuteChanged(); } }
    public ObservableCollection<AccountTransaction> Transactions { get; } = new();
    public ObservableCollection<AssetTransactionSummary> TransactionSummary { get; } = new();
    public ICommand BackCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ChangeRoleCommand { get; }
    public ICommand ToggleStatusCommand { get; }
    public ICommand DepositCommand { get; }
    public PersonalInformationViewModel(User user, ShellViewModel shell)
    {
        User = user; _shell = shell;
        BackCommand = new RelayCommand(() => { if (shell.IsAdmin) shell.ShowUsers(); else shell.ShowDashboard(); });
        RefreshCommand = new RelayCommand(p => { _ = LoadAsync(); }, p => !IsBusy && !_disposed);
        ChangeRoleCommand = new RelayCommand(p => { _ = UpdateAsync(true); }, p => CanManage && !IsBusy && !_disposed && (User.Role == "admin" || User.Role == "employe"));
        ToggleStatusCommand = new RelayCommand(p => { _ = UpdateAsync(false); }, p => CanManage && !IsBusy && !_disposed && User.IsActive.HasValue);
        DepositCommand = new RelayCommand(p => { _ = DepositAsync(); }, p => IsAdmin && !IsBusy && !_disposed && User.IsActive != false);
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
        var path = "api/users/me/portfolio";
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
        using var historyResponse = await _shell.Http.GetAsync(path + "/transactions", _lifetime.Token);
        await EnsureAsync(historyResponse);
        var history = await historyResponse.Content.ReadFromJsonAsync<TransactionPage>(ShellViewModel.JsonOptions, _lifetime.Token);
        Transactions.Clear();
        foreach (var item in history?.Transactions ?? new())
            if (item.Type == "buy" || item.Type == "sell") Transactions.Add(item);
        TransactionSummary.Clear();
        var summary = history?.ByAsset;
        if (summary == null || summary.Count == 0)
            summary = Transactions.GroupBy(t => t.Symbol).Select(g => new AssetTransactionSummary {
                Symbol=g.Key, Name=g.First().Name, TransactionCount=g.Count(),
                BuyCount=g.Count(t => t.Type == "buy"), SellCount=g.Count(t => t.Type == "sell") }).ToList();
        foreach (var item in summary) TransactionSummary.Add(item);
    }
    public async Task LoadAsync()
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; Message = "";
        try { await ReadAccountAsync(); Message = !_supportsTopUp ? "Cash balances and top-ups are not available from the account service yet." : Transactions.Count == 0 ? "No transactions yet." : ""; }
        catch (Exception ex) { if (!_disposed) Message = "Unable to load account: " + FriendlyError(ex); }
        finally { IsBusy = false; }
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
