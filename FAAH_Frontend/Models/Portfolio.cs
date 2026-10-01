using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace FAAH_Frontend.Models;

public enum RiskLevel { Low, Medium, High, VeryHigh }

public enum PortfolioStatus { Active, Paused }

public class Portfolio
{
    [JsonPropertyName("portfolio_id")]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public RiskLevel Risk { get; set; }

    [JsonPropertyName("risk_tolerance")]
    public string RiskTolerance { get; set; } = string.Empty;

    [JsonPropertyName("strategy_type")]
    public string StrategyType { get; set; } = string.Empty;

    [JsonPropertyName("preferred_asset_types")]
    public List<string> PreferredAssetTypes { get; set; } = new();

    [JsonPropertyName("preferred_niche_ids")]
    public List<int> PreferredNicheIds { get; set; } = new();

    [JsonPropertyName("max_position_size_pct")]
    public decimal MaxPositionSizePct { get; set; }

    [JsonPropertyName("max_open_positions")]
    public int MaxPositions { get; set; }

    [JsonPropertyName("return_pct")]
    public decimal? ReturnPercent { get; set; }

    [JsonPropertyName("base_currency")]
    public string BaseCurrency { get; set; } = "USD";
    public string Currency { get => BaseCurrency; set => BaseCurrency = value; }

    [JsonPropertyName("status")]
    public string StatusText { get; set; } = string.Empty;

    [JsonIgnore]
    public PortfolioStatus Status
    {
        get => string.Equals(StatusText, "paused", StringComparison.OrdinalIgnoreCase) ? PortfolioStatus.Paused : PortfolioStatus.Active;
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

    public string StatusDisplay => Status == PortfolioStatus.Active ? "ACTIVE" : "PAUSED";

    public bool IsActive => Status == PortfolioStatus.Active;
}
