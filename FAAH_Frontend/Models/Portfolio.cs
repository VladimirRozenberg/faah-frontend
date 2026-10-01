using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;

namespace FAAH_Frontend.Models;

public enum RiskLevel { Low, Medium, High, VeryHigh }

public enum PortfolioStatus { Active, Paused }

public class Portfolio : INotifyPropertyChanged
{
    private string _statusText = string.Empty;
    private bool _isStatusUpdating;
    private string? _statusErrorMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("portfolio_id")]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; } = string.Empty;

    public RiskLevel Risk { get; set; }

    [JsonPropertyName("risk_tolerance")]
    public string RiskTolerance { get; set; } = string.Empty;

    [JsonPropertyName("strategy_type")]
    public string? StrategyType { get; set; } = string.Empty;

    [JsonPropertyName("preferred_asset_types")]
    public List<string> PreferredAssetTypes { get; set; } = new();

    [JsonPropertyName("preferred_niche_ids")]
    public List<int> PreferredNicheIds { get; set; } = new();

    [JsonPropertyName("max_position_size_pct")]
    public decimal? MaxPositionSizePct { get; set; }

    [JsonPropertyName("max_open_positions")]
    public int MaxPositions { get; set; }

    [JsonPropertyName("return_pct")]
    public decimal? ReturnPercent { get; set; }

    [JsonPropertyName("base_currency")]
    public string BaseCurrency { get; set; } = "USD";
    public string Currency { get => BaseCurrency; set => BaseCurrency = value; }

    [JsonPropertyName("status")]
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(StatusDisplay));
        }
    }

    [JsonIgnore]
    public bool IsStatusUpdating
    {
        get => _isStatusUpdating;
        set
        {
            if (_isStatusUpdating == value) return;
            _isStatusUpdating = value;
            OnPropertyChanged(nameof(IsStatusUpdating));
            OnPropertyChanged(nameof(CanToggleStatus));
        }
    }

    [JsonIgnore]
    public bool CanToggleStatus => !IsStatusUpdating;

    [JsonIgnore]
    public string? StatusErrorMessage
    {
        get => _statusErrorMessage;
        set
        {
            if (_statusErrorMessage == value) return;
            _statusErrorMessage = value;
            OnPropertyChanged(nameof(StatusErrorMessage));
            OnPropertyChanged(nameof(HasStatusError));
        }
    }

    [JsonIgnore]
    public bool HasStatusError => !string.IsNullOrWhiteSpace(StatusErrorMessage);

    [JsonIgnore]
    public PortfolioStatus Status
    {
        get => IsActive ? PortfolioStatus.Active : PortfolioStatus.Paused;
        set => StatusText = value == PortfolioStatus.Paused ? "paused" : "active";
    }

    // ---- Propriétés d'affichage ----

    public string RiskDisplay => Risk switch
    {
        _ when !string.IsNullOrWhiteSpace(RiskTolerance) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(RiskTolerance),
        RiskLevel.Low => "Low",
        RiskLevel.Medium => "Medium",
        RiskLevel.High => "High",
        RiskLevel.VeryHigh => "Very High",
        _ => string.Empty
    };

    public string ReturnDisplay => ReturnPercent.HasValue
        ? (ReturnPercent.Value >= 0 ? "+" : "−") +
          Math.Abs(ReturnPercent.Value).ToString("0.0", CultureInfo.InvariantCulture) + "%"
        : "—";

    public bool IsUp => ReturnPercent.HasValue && ReturnPercent.Value >= 0;

    public bool IsDown => ReturnPercent.HasValue && ReturnPercent.Value < 0;

    public string StatusDisplay => IsActive ? "ACTIVE" : "PAUSED";

    public bool IsActive
    {
        get => string.Equals(StatusText, "active", StringComparison.OrdinalIgnoreCase);
        set => StatusText = value ? "active" : "paused";
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
