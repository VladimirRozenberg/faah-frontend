using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public class PortfolioCreateViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly RelayCommand _submitCommand;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _strategyType = "balanced";
    private string _riskTolerance = "medium";
    private string _maxPositionSizePct = "5";
    private string _maxOpenPositions = "10";
    private string? _nameError;
    private string? _descriptionError;
    private string? _strategyTypeError;
    private string? _riskToleranceError;
    private string? _maxPositionSizePctError;
    private string? _maxOpenPositionsError;
    private string? _errorMessage;
    private bool _isSubmitting;
    private bool _isLoadingNiches;
    private string? _nicheLoadError;

    public PortfolioCreateViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _submitCommand = new RelayCommand(async _ => await SubmitAsync(), _ => CanSubmit);
        SubmitCommand = _submitCommand;
        CancelCommand = new RelayCommand(_ => _shell.ShowPortfolios());
        _ = LoadNichesAsync();
    }

    public string Name
    {
        get => _name;
        set
        {
            if (!SetField(ref _name, value)) return;
            ClearError(ref _nameError, nameof(NameError));
            OnPropertyChanged(nameof(CanSubmit));
            _submitCommand.RaiseCanExecuteChanged();
        }
    }
    public string Description { get => _description; set { SetField(ref _description, value); ClearError(ref _descriptionError, nameof(DescriptionError)); } }
    public string StrategyType { get => _strategyType; set { SetField(ref _strategyType, value); ClearError(ref _strategyTypeError, nameof(StrategyTypeError)); } }
    public string RiskTolerance { get => _riskTolerance; set { SetField(ref _riskTolerance, value); ClearError(ref _riskToleranceError, nameof(RiskToleranceError)); } }
    public string MaxPositionSizePct { get => _maxPositionSizePct; set { SetField(ref _maxPositionSizePct, value); ClearError(ref _maxPositionSizePctError, nameof(MaxPositionSizePctError)); } }
    public string MaxOpenPositions { get => _maxOpenPositions; set { SetField(ref _maxOpenPositions, value); ClearError(ref _maxOpenPositionsError, nameof(MaxOpenPositionsError)); } }

    public ObservableCollection<AssetTypeOption> AssetTypes { get; } = new()
    {
        new("stock", "Stocks"),
        new("crypto", "Crypto"),
        new("forex", "Forex"),
        new("future", "Futures")
    };

    public ObservableCollection<NicheOption> Niches { get; } = new();

    public string? NameError { get => _nameError; private set => SetField(ref _nameError, value); }
    public string? DescriptionError { get => _descriptionError; private set => SetField(ref _descriptionError, value); }
    public string? StrategyTypeError { get => _strategyTypeError; private set => SetField(ref _strategyTypeError, value); }
    public string? RiskToleranceError { get => _riskToleranceError; private set => SetField(ref _riskToleranceError, value); }
    public string? MaxPositionSizePctError { get => _maxPositionSizePctError; private set => SetField(ref _maxPositionSizePctError, value); }
    public string? MaxOpenPositionsError { get => _maxOpenPositionsError; private set => SetField(ref _maxOpenPositionsError, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsSubmitting { get => _isSubmitting; private set { if (SetField(ref _isSubmitting, value)) _submitCommand.RaiseCanExecuteChanged(); } }
    public bool CanSubmit => !IsSubmitting && !string.IsNullOrWhiteSpace(Name);
    public bool IsLoadingNiches { get => _isLoadingNiches; private set => SetField(ref _isLoadingNiches, value); }
    public string? NicheLoadError { get => _nicheLoadError; private set => SetField(ref _nicheLoadError, value); }
    public bool HasNicheLoadError => !string.IsNullOrWhiteSpace(NicheLoadError);

    public string[] StrategyTypes { get; } = { "conservative", "income", "balanced", "growth", "aggressive", "custom" };
    public string[] RiskTolerances { get; } = { "low", "medium", "high" };
    public ICommand SubmitCommand { get; }
    public ICommand CancelCommand { get; }

    private async System.Threading.Tasks.Task SubmitAsync()
    {
        ClearErrors();
        if (!Validate()) return;
        if (!int.TryParse(_shell.ProfileUserId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
        {
            ErrorMessage = "The logged-in user ID is unavailable. Please sign in again.";
            OnPropertyChanged(nameof(HasError));
            return;
        }

        IsSubmitting = true;
        try
        {
            var request = new PortfolioCreateRequest
            {
                Name = Name.Trim(),
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                StrategyType = StrategyType,
                RiskTolerance = RiskTolerance,
                MaxPositionSizePct = decimal.Parse(MaxPositionSizePct, CultureInfo.InvariantCulture),
                MaxOpenPositions = int.Parse(MaxOpenPositions, CultureInfo.InvariantCulture),
                PreferredAssetTypes = AssetTypes.Where(option => option.IsSelected).Select(option => option.Value).Distinct(StringComparer.Ordinal).ToArray(),
                PreferredNicheIds = Niches.Where(niche => niche.IsSelected).Select(niche => niche.Id).Distinct().ToArray()
            };

            using var response = await _shell.Http.PostAsJsonAsync(
                $"api/users/{userId}/portfolio/create", request, ShellViewModel.JsonOptions);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var portfolio = await response.Content.ReadFromJsonAsync<Portfolio>(ShellViewModel.JsonOptions);
                if (portfolio is null)
                {
                    ErrorMessage = "The server returned an empty portfolio.";
                    OnPropertyChanged(nameof(HasError));
                    return;
                }

                _shell.Portfolios.Add(portfolio);
                _shell.ShowPortfolio(portfolio);
                return;
            }

            await SetBackendErrorsAsync(response);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PORTFOLIO CREATE ERROR: {ex}");
            ErrorMessage = "Unable to contact the FAAH server.";
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private bool Validate()
    {
        var valid = true;
        if (string.IsNullOrWhiteSpace(Name)) { NameError = "Name is required."; valid = false; }
        else if (Name.Trim().Length > 200) { NameError = "Name must be 200 characters or fewer."; valid = false; }
        if (Description.Length > 2000) { DescriptionError = "Description must be 2,000 characters or fewer."; valid = false; }
        if (Array.IndexOf(StrategyTypes, StrategyType) < 0) { StrategyTypeError = "Select a valid strategy."; valid = false; }
        if (Array.IndexOf(RiskTolerances, RiskTolerance) < 0) { RiskToleranceError = "Select low, medium, or high."; valid = false; }
        if (!decimal.TryParse(MaxPositionSizePct, NumberStyles.Number, CultureInfo.InvariantCulture, out var positionSize) || positionSize <= 0 || positionSize > 100)
        { MaxPositionSizePctError = "Enter a value greater than 0 and at most 100."; valid = false; }
        if (!int.TryParse(MaxOpenPositions, NumberStyles.None, CultureInfo.InvariantCulture, out var openPositions) || openPositions < 1 || openPositions > 1000)
        { MaxOpenPositionsError = "Enter a whole number from 1 to 1,000."; valid = false; }
        return valid;
    }

    private async System.Threading.Tasks.Task LoadNichesAsync()
    {
        IsLoadingNiches = true;
        NicheLoadError = null;
        OnPropertyChanged(nameof(HasNicheLoadError));
        try
        {
            using var response = await _shell.Http.GetAsync("api/niches");
            if (!response.IsSuccessStatusCode)
            {
                NicheLoadError = response.StatusCode == HttpStatusCode.NotFound
                    ? "Niche choices are not available from the server."
                    : $"Unable to load niches ({(int)response.StatusCode}).";
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<NicheResponse>(ShellViewModel.JsonOptions);
            Niches.Clear();
            foreach (var niche in result?.Items ?? Enumerable.Empty<NicheOption>())
                if (!Niches.Any(existing => existing.Id == niche.Id)) Niches.Add(niche);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PORTFOLIO NICHES ERROR: {ex}");
            NicheLoadError = "Unable to load niche choices.";
        }
        finally
        {
            IsLoadingNiches = false;
            OnPropertyChanged(nameof(HasNicheLoadError));
        }
    }

    private async System.Threading.Tasks.Task SetBackendErrorsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("detail", out var detail))
            {
                if (detail.ValueKind == JsonValueKind.String)
                {
                    ErrorMessage = detail.GetString();
                }
                else if (detail.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in detail.EnumerateArray())
                    {
                        var message = item.TryGetProperty("msg", out var msg) ? msg.GetString() : "Validation error.";
                        var field = item.TryGetProperty("loc", out var loc) ? GetFieldName(loc) : null;
                        SetFieldError(field, message ?? "Validation error.");
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Fall back to a status-specific message when the response is not JSON.
        }

        if (!HasAnyError()) ErrorMessage = $"Portfolio creation failed ({(int)response.StatusCode}).";
        OnPropertyChanged(nameof(HasError));
    }

    private static string? GetFieldName(JsonElement location)
    {
        if (location.ValueKind != JsonValueKind.Array) return null;
        string? field = null;
        foreach (var part in location.EnumerateArray())
            if (part.ValueKind == JsonValueKind.String) field = part.GetString();
        return field;
    }

    private void SetFieldError(string? field, string message)
    {
        switch (field)
        {
            case "name": NameError = message; break;
            case "description": DescriptionError = message; break;
            case "strategy_type": StrategyTypeError = message; break;
            case "risk_tolerance": RiskToleranceError = message; break;
            case "max_position_size_pct": MaxPositionSizePctError = message; break;
            case "max_open_positions": MaxOpenPositionsError = message; break;
            default: ErrorMessage = message; break;
        }
    }

    private bool HasAnyError() => !string.IsNullOrWhiteSpace(NameError) || !string.IsNullOrWhiteSpace(DescriptionError) ||
        !string.IsNullOrWhiteSpace(StrategyTypeError) || !string.IsNullOrWhiteSpace(RiskToleranceError) ||
        !string.IsNullOrWhiteSpace(MaxPositionSizePctError) || !string.IsNullOrWhiteSpace(MaxOpenPositionsError) ||
        !string.IsNullOrWhiteSpace(ErrorMessage);

    private void ClearErrors()
    {
        NameError = DescriptionError = StrategyTypeError = RiskToleranceError = MaxPositionSizePctError = MaxOpenPositionsError = null;
        ErrorMessage = null;
        OnPropertyChanged(nameof(HasError));
    }

    private void ClearError(ref string? field, string propertyName)
    {
        if (field is null) return;
        field = null;
        OnPropertyChanged(propertyName);
    }

    private sealed class PortfolioCreateRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string StrategyType { get; set; } = string.Empty;
        public string RiskTolerance { get; set; } = string.Empty;
        public decimal MaxPositionSizePct { get; set; }
        public int MaxOpenPositions { get; set; }
        public string[] PreferredAssetTypes { get; set; } = Array.Empty<string>();
        public int[] PreferredNicheIds { get; set; } = Array.Empty<int>();
    }

    private sealed class NicheResponse
    {
        public int Count { get; set; }
        public NicheOption[] Items { get; set; } = Array.Empty<NicheOption>();
    }

    public sealed class AssetTypeOption
    {
        public AssetTypeOption(string value, string displayName) { Value = value; DisplayName = displayName; }
        public string Value { get; }
        public string DisplayName { get; }
        public bool IsSelected { get; set; }
    }

    public sealed class NicheOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }
}
