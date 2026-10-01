using System;
using System.Collections.Generic;
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
    private const string NullableStrategyTypeOption = "Not set";
    private readonly ShellViewModel _shell;
    private readonly RelayCommand _submitCommand;
    private readonly Portfolio? _portfolio;
    private string[] _originalPreferredAssetTypes;
    private int[] _originalPreferredNicheIds;
    private bool _isLoadingSettings, _settingsLoaded;
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

    public PortfolioCreateViewModel(ShellViewModel shell, Portfolio? portfolio = null)
    {
        _shell = shell;
        _portfolio = portfolio;
        StrategyTypes = IsEditMode
            ? new[] { NullableStrategyTypeOption, "conservative", "income", "balanced", "growth", "aggressive", "custom" }
            : new[] { "conservative", "income", "balanced", "growth", "aggressive", "custom" };
        _originalPreferredAssetTypes = portfolio?.PreferredAssetTypes.ToArray() ?? Array.Empty<string>();
        _originalPreferredNicheIds = portfolio?.PreferredNicheIds.ToArray() ?? Array.Empty<int>();
        _submitCommand = new RelayCommand(async _ => await SubmitAsync(), _ => CanSubmit);
        SubmitCommand = _submitCommand;
        CancelCommand = new RelayCommand(_ =>
        {
            if (_portfolio is null) _shell.ShowPortfolios();
            else _shell.ShowPortfolio(_portfolio);
        });
        Initialization = InitializeAsync();
    }

    public bool IsEditMode => _portfolio is not null;
    public string PageTitle => IsEditMode ? "Edit portfolio" : "Create portfolio";
    public string SubmitButtonText => IsEditMode ? "Save changes" : "Create portfolio";

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
    public bool IsSubmitting
    {
        get => _isSubmitting;
        private set
        {
            if (!SetField(ref _isSubmitting, value)) return;
            OnPropertyChanged(nameof(CanEditSettings));
            OnPropertyChanged(nameof(CanSubmit));
            _submitCommand.RaiseCanExecuteChanged();
        }
    }
    public bool CanSubmit => CanEditSettings && !IsLoadingNiches && !string.IsNullOrWhiteSpace(Name);
    public bool CanEditSettings => !IsSubmitting && !IsLoadingSettings && (!IsEditMode || _settingsLoaded);
    public bool IsLoadingSettings
    {
        get => _isLoadingSettings;
        private set
        {
            SetField(ref _isLoadingSettings, value);
            OnPropertyChanged(nameof(CanEditSettings));
            OnPropertyChanged(nameof(CanSubmit));
            _submitCommand.RaiseCanExecuteChanged();
        }
    }
    public System.Threading.Tasks.Task Initialization { get; }

    private async System.Threading.Tasks.Task InitializeAsync()
    {
        if (_portfolio is not null)
        {
            IsLoadingSettings = true;
            try
            {
                // La liste contient seulement un résumé : relire le détail avant d'éditer.
                var detail = await _shell.Http.GetFromJsonAsync<PortfolioUpdateResponse>(
                    $"api/users/{_shell.ProfileUserId}/portfolios/{_portfolio.Id}", ShellViewModel.JsonOptions);
                if (detail is null || detail.Id != _portfolio.Id ||
                    detail.UserId.ToString(CultureInfo.InvariantCulture) != _shell.ProfileUserId ||
                    detail.PreferredAssetTypes is null || detail.PreferredNicheIds is null)
                    throw new JsonException("Incomplete portfolio settings.");
                _shell.ApplyPortfolioUpdate(_portfolio, detail);
                _originalPreferredAssetTypes = _portfolio.PreferredAssetTypes.ToArray();
                _originalPreferredNicheIds = _portfolio.PreferredNicheIds.ToArray();
                InitializeFromPortfolio(_portfolio);
                _settingsLoaded = true;
            }
            catch (Exception)
            {
                ErrorMessage = "Unable to load saved settings. Cancel and reopen this page to retry.";
                OnPropertyChanged(nameof(HasError));
                return; // Ne jamais enregistrer des valeurs par défaut après un échec de lecture.
            }
            finally { IsLoadingSettings = false; }
        }
        await LoadNichesAsync();
        OnPropertyChanged(nameof(CanSubmit));
        _submitCommand.RaiseCanExecuteChanged();
    }
    public bool IsLoadingNiches { get => _isLoadingNiches; private set => SetField(ref _isLoadingNiches, value); }
    public string? NicheLoadError { get => _nicheLoadError; private set => SetField(ref _nicheLoadError, value); }
    public bool HasNicheLoadError => !string.IsNullOrWhiteSpace(NicheLoadError);

    public string[] StrategyTypes { get; }
    public string[] RiskTolerances { get; } = { "low", "medium", "high" };
    public ICommand SubmitCommand { get; }
    public ICommand CancelCommand { get; }

    private void InitializeFromPortfolio(Portfolio portfolio)
    {
        Name = portfolio.Name;
        Description = portfolio.Description ?? string.Empty;
        StrategyType = string.IsNullOrWhiteSpace(portfolio.StrategyType) ? NullableStrategyTypeOption : portfolio.StrategyType;
        RiskTolerance = portfolio.RiskTolerance;
        MaxPositionSizePct = portfolio.MaxPositionSizePct?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        MaxOpenPositions = portfolio.MaxPositions.ToString(CultureInfo.InvariantCulture);
        foreach (var option in AssetTypes)
            option.IsSelected = portfolio.PreferredAssetTypes.Contains(option.Value, StringComparer.Ordinal);
    }

    private async System.Threading.Tasks.Task SubmitAsync()
    {
        if (!CanSubmit) return;
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
            if (_portfolio is not null)
            {
                var patch = BuildPatchPayload();
                if (patch.Count == 0)
                {
                    _shell.ShowPortfolio(_portfolio);
                    return;
                }

                using var updateResponse = await _shell.Http.PatchAsJsonAsync(
                    $"api/users/{userId}/portfolios/{_portfolio.Id}", patch, ShellViewModel.JsonOptions);
                if (!updateResponse.IsSuccessStatusCode)
                {
                    await SetBackendErrorsAsync(updateResponse);
                    return;
                }

                var updatedPortfolio = await updateResponse.Content.ReadFromJsonAsync<PortfolioUpdateResponse>(ShellViewModel.JsonOptions);
                if (updatedPortfolio is null || updatedPortfolio.Id != _portfolio.Id || updatedPortfolio.UserId != userId)
                {
                    ErrorMessage = "The server returned an invalid portfolio response.";
                    OnPropertyChanged(nameof(HasError));
                    return;
                }

                _shell.ApplyPortfolioUpdate(_portfolio, updatedPortfolio);
                _shell.ShowPortfolio(_portfolio);
                return;
            }

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
                var created = await response.Content.ReadFromJsonAsync<PortfolioUpdateResponse>(ShellViewModel.JsonOptions);
                if (created is null || created.Id <= 0 || created.UserId != userId)
                {
                    ErrorMessage = "The server returned an empty portfolio.";
                    OnPropertyChanged(nameof(HasError));
                    return;
                }

                // POST renvoie "id" et "is_active", contrairement au résumé de la liste.
                var portfolio = new Portfolio { Id = created.Id };
                _shell.ApplyPortfolioUpdate(portfolio, created);
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
        if (!(IsEditMode && StrategyType == NullableStrategyTypeOption) && Array.IndexOf(StrategyTypes, StrategyType) < 0)
        { StrategyTypeError = "Select a valid strategy."; valid = false; }
        if (Array.IndexOf(RiskTolerances, RiskTolerance) < 0) { RiskToleranceError = "Select low, medium, or high."; valid = false; }
        if ((!IsEditMode || !string.IsNullOrWhiteSpace(MaxPositionSizePct)) &&
            (!decimal.TryParse(MaxPositionSizePct, NumberStyles.Number, CultureInfo.InvariantCulture, out var positionSize) || positionSize <= 0 || positionSize > 100))
        { MaxPositionSizePctError = "Enter a value greater than 0 and at most 100."; valid = false; }
        if (!int.TryParse(MaxOpenPositions, NumberStyles.None, CultureInfo.InvariantCulture, out var openPositions) || openPositions < 1 || openPositions > 1000)
        { MaxOpenPositionsError = "Enter a whole number from 1 to 1,000."; valid = false; }
        return valid;
    }

    private Dictionary<string, object?> BuildPatchPayload()
    {
        var patch = new Dictionary<string, object?>();
        if (_portfolio is null) return patch;

        var name = Name.Trim();
        if (!string.Equals(name, _portfolio.Name.Trim(), StringComparison.Ordinal))
            patch["name"] = name;

        var description = NormalizeOptionalText(Description);
        if (!string.Equals(description, NormalizeOptionalText(_portfolio.Description), StringComparison.Ordinal))
            patch["description"] = description;

        var strategyType = StrategyType == NullableStrategyTypeOption ? null : StrategyType;
        if (!string.Equals(strategyType, _portfolio.StrategyType, StringComparison.Ordinal))
            patch["strategy_type"] = strategyType;

        if (!string.Equals(RiskTolerance, _portfolio.RiskTolerance, StringComparison.Ordinal))
            patch["risk_tolerance"] = RiskTolerance;

        decimal? maxPositionSize = string.IsNullOrWhiteSpace(MaxPositionSizePct)
            ? null
            : decimal.Parse(MaxPositionSizePct, CultureInfo.InvariantCulture);
        if (maxPositionSize != _portfolio.MaxPositionSizePct)
            patch["max_position_size_pct"] = maxPositionSize;

        var maxOpenPositions = int.Parse(MaxOpenPositions, CultureInfo.InvariantCulture);
        if (maxOpenPositions != _portfolio.MaxPositions)
            patch["max_open_positions"] = maxOpenPositions;

        var preferredAssetTypes = AssetTypes.Where(option => option.IsSelected)
            .Select(option => option.Value).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var originalAssetTypes = _originalPreferredAssetTypes.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!preferredAssetTypes.SequenceEqual(originalAssetTypes, StringComparer.Ordinal))
            patch["preferred_asset_types"] = preferredAssetTypes;

        if (!IsLoadingNiches && !HasNicheLoadError && _originalPreferredNicheIds.All(id => Niches.Any(niche => niche.Id == id)))
        {
            var preferredNicheIds = Niches.Where(niche => niche.IsSelected)
                .Select(niche => niche.Id).Distinct().OrderBy(id => id).ToArray();
            var originalNicheIds = _originalPreferredNicheIds.Distinct().OrderBy(id => id).ToArray();
            if (!preferredNicheIds.SequenceEqual(originalNicheIds))
                patch["preferred_niche_ids"] = preferredNicheIds;
        }

        return patch;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
            {
                if (Niches.Any(existing => existing.Id == niche.Id)) continue;
                if (_portfolio is not null)
                    niche.IsSelected = _portfolio.PreferredNicheIds.Contains(niche.Id);
                Niches.Add(niche);
            }
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

        if (!HasAnyError()) ErrorMessage = $"Portfolio {(IsEditMode ? "update" : "creation")} failed ({(int)response.StatusCode}).";
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
        public string? StrategyType { get; set; }
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

    public sealed class AssetTypeOption : ViewModelBase
    {
        public AssetTypeOption(string value, string displayName) { Value = value; DisplayName = displayName; }
        public string Value { get; }
        public string DisplayName { get; }
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
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
