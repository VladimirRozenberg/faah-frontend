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
    private List<Opportunity> _all = new();
    private bool _busy, _disposed, _failed;
    private string _action = "buy", _message = "", _accountMessage = "", _cash = "—", _assets = "—";
    private Opportunity? _selected;
    public ObservableCollection<Opportunity> Opportunities { get; } = new();
    public ICommand BuyCommand { get; }
    public ICommand SellCommand { get; }
    public ICommand DetailCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ProfileCommand => _shell.ShowProfileCommand;
    public bool IsBuy => _action == "buy";
    public bool IsSell => !IsBuy;
    public bool IsEmpty => !IsBusy && !_failed && Opportunities.Count == 0;
    public bool HasDetail => Selected != null;
    public Opportunity? Selected { get => _selected; private set { SetField(ref _selected, value); OnPropertyChanged(nameof(HasDetail)); } }
    public string Message { get => _message; private set => SetField(ref _message, value); }
    public string AccountMessage { get => _accountMessage; private set => SetField(ref _accountMessage, value); }
    public string Cash { get => _cash; private set => SetField(ref _cash, value); }
    public string Assets { get => _assets; private set => SetField(ref _assets, value); }
    public bool IsBusy { get => _busy; private set { SetField(ref _busy, value); OnPropertyChanged(nameof(IsEmpty)); ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged(); } }
    public DashboardViewModel(ShellViewModel shell)
    {
        _shell = shell;
        BuyCommand = new RelayCommand(() => Filter("buy"));
        SellCommand = new RelayCommand(() => Filter("sell"));
        DetailCommand = new RelayCommand(p => { if (p is Opportunity item) Selected = item; });
        RefreshCommand = new RelayCommand(p => { _ = LoadAsync(); }, p => !IsBusy && !_disposed);
        _ = LoadAsync();
    }
    private void Filter(string action)
    {
        _action = action; Selected = null; Opportunities.Clear();
        foreach (var item in _all
            .Where(x => string.Equals(x.SigAction, action, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.SigConfidence ?? -1)
            .ThenByDescending(x => x.SigCreatedAt)
            .ThenByDescending(x => x.SigId)
            .Take(10))
            Opportunities.Add(item);
        OnPropertyChanged(nameof(IsBuy)); OnPropertyChanged(nameof(IsSell)); OnPropertyChanged(nameof(IsEmpty));
    }
    public async Task LoadAsync()
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; Message = ""; AccountMessage = ""; _failed = false; _all.Clear(); Filter(_action);
        int? portfolioId = null;
        try {
            var cash = await _shell.Http.GetFromJsonAsync<AvailableCashResponse>($"api/users/{_shell.ProfileUserId}/available-cash", ShellViewModel.JsonOptions, _lifetime.Token);
            Cash = cash?.AvailableCash.HasValue == true ? $"{cash.AvailableCash:N2} {cash.Currency}" : "Unavailable";
        } catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException) {
            if (!_disposed) { Cash = "—"; AccountMessage = FriendlyError(ex, "Available cash"); }
        }
        try {
            var value = await _shell.Http.GetFromJsonAsync<AssetValueResponse>($"api/users/{_shell.ProfileUserId}/asset-value", ShellViewModel.JsonOptions, _lifetime.Token);
            if (value != null) {
                Assets = value.TotalCurrentValue.HasValue ? $"{value.TotalCurrentValue:N2} {value.Currency}" : "Prices unavailable";
                if (!value.ValuationComplete && value.MissingPriceSymbols.Count > 0)
                    AccountMessage = $"Prices unavailable for: {string.Join(", ", value.MissingPriceSymbols)}.";
            }
        } catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException) {
            if (!_disposed) { Assets = "—"; AccountMessage = FriendlyError(ex, "Asset value"); }
        }
        try {
            var account = await _shell.Http.GetFromJsonAsync<AccountSnapshot>($"api/users/{_shell.ProfileUserId}/portfolio", ShellViewModel.JsonOptions, _lifetime.Token);
            portfolioId = account?.Id;
        } catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException) {
            // portfolioId stays null; only needed to filter legacy trading-signals.
        }
        try {
            using var response = await _shell.Http.GetAsync("api/opportunities", _lifetime.Token);
            OpportunityPage? page;
            if (response.StatusCode == HttpStatusCode.NotFound) {
                // Compatibility with the currently deployed API; never include another portfolio.
                page = await _shell.Http.GetFromJsonAsync<OpportunityPage>("api/trading-signals", ShellViewModel.JsonOptions, _lifetime.Token);
                _all = (page?.Items ?? new()).Where(x =>
                    (x.SigPrtId == null || portfolioId.HasValue && x.SigPrtId == portfolioId) &&
                    string.Equals(x.SigStatus, "active", StringComparison.OrdinalIgnoreCase) &&
                    (x.SigExpiresAt == null || x.SigExpiresAt > DateTimeOffset.UtcNow))
                    .OrderByDescending(x => x.SigCreatedAt).ThenByDescending(x => x.SigId)
                    .GroupBy(x => x.AssetSymbol, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .Where(x => x.SigAction.Equals("buy", StringComparison.OrdinalIgnoreCase) || x.SigAction.Equals("sell", StringComparison.OrdinalIgnoreCase)).ToList();
            } else {
                response.EnsureSuccessStatusCode();
                page = await response.Content.ReadFromJsonAsync<OpportunityPage>(ShellViewModel.JsonOptions, _lifetime.Token);
                _all = page?.Items ?? new();
            }
            Filter(_action);
        } catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException) {
            if (!_disposed) { _failed = true; Message = FriendlyError(ex, "Opportunities"); }
        } finally { IsBusy = false; }
    }
    private static string FriendlyError(Exception error, string section) => error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } ? "Your session has expired. Please sign in again." : $"{section} could not be loaded. Please refresh or try again later.";
    public void Dispose() { _disposed = true; _lifetime.Cancel(); }
}
