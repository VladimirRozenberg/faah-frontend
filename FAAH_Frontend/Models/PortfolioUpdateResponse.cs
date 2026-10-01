using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FAAH_Frontend.Models;

public sealed class PortfolioUpdateResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("strategy_type")]
    public string? StrategyType { get; set; }

    [JsonPropertyName("preferred_asset_types")]
    public List<string>? PreferredAssetTypes { get; set; }

    [JsonPropertyName("preferred_niche_ids")]
    public List<int>? PreferredNicheIds { get; set; }

    [JsonPropertyName("risk_tolerance")]
    public string RiskTolerance { get; set; } = string.Empty;

    [JsonPropertyName("max_position_size_pct")]
    public decimal? MaxPositionSizePct { get; set; }

    [JsonPropertyName("max_open_positions")]
    public int MaxOpenPositions { get; set; }

    [JsonPropertyName("base_currency")]
    public string BaseCurrency { get; set; } = "USD";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }
}
