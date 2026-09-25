using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAAH_Frontend.Models;

public sealed class NewsSourceDetailResponse
{
    [JsonPropertyName("source")] public NewsSourceDetail Source { get; set; } = new();
    [JsonPropertyName("overview")] public NewsSourceOverview Overview { get; set; } = new();
    [JsonPropertyName("classifications")] public List<NewsClassification> Classifications { get; set; } = new();
    [JsonPropertyName("analyses")] public List<NewsAnalysis> Analyses { get; set; } = new();
}

public sealed class NewsSourceDetail
{
    [JsonPropertyName("src_id")] public int Id { get; set; }
    [JsonPropertyName("src_type")] public string? Type { get; set; }
    [JsonPropertyName("src_title")] public string? Title { get; set; }
    [JsonPropertyName("src_content")] public string? Content { get; set; }
    [JsonPropertyName("src_original_url")] public string? OriginalUrl { get; set; }
    [JsonPropertyName("src_published_at")] public DateTime? PublishedAt { get; set; }
    [JsonPropertyName("src_is_processed")] public bool? IsProcessed { get; set; }
    [JsonPropertyName("src_created_at")] public DateTime? CreatedAt { get; set; }
    public string TypeDisplay => NewsDisplay.Capitalize(Type);
}

public sealed class NewsSourceOverview
{
    [JsonPropertyName("classification_count")] public int ClassificationCount { get; set; }
    [JsonPropertyName("analysis_count")] public int AnalysisCount { get; set; }
    [JsonPropertyName("signal_count")] public int SignalCount { get; set; }
    [JsonPropertyName("detected_asset_count")] public int DetectedAssetCount { get; set; }
}

public sealed class NewsClassification
{
    [JsonPropertyName("cls_category")] public string? Category { get; set; }
    [JsonPropertyName("cls_importance")] public string? Importance { get; set; }
    [JsonPropertyName("cls_sentiment")] public string? Sentiment { get; set; }
    [JsonPropertyName("cls_should_trigger")] public bool? ShouldTrigger { get; set; }
    [JsonPropertyName("cls_reason")] public string? Reason { get; set; }
    [JsonPropertyName("assets")] public List<ClassificationAsset> Assets { get; set; } = new();
    [JsonPropertyName("niches")] public List<NewsNiche> Niches { get; set; } = new();
    public string CategoryDisplay => NewsDisplay.Capitalize(Category);
    public string ImportanceDisplay => NewsDisplay.Capitalize(Importance);
    public string SentimentDisplay => NewsDisplay.Capitalize(Sentiment);
}

public sealed class ClassificationAsset
{
    [JsonPropertyName("ast_id")] public int Id { get; set; }
    [JsonPropertyName("ast_symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("ast_name")] public string? Name { get; set; }
    [JsonPropertyName("ast_type")] public string? Type { get; set; }
    [JsonPropertyName("ast_exchange")] public string? Exchange { get; set; }
    [JsonPropertyName("ast_currency")] public string? Currency { get; set; }
    [JsonPropertyName("ast_country")] public string? Country { get; set; }
    [JsonPropertyName("cla_relevance_confidence")] public decimal? RelevanceConfidence { get; set; }
    [JsonPropertyName("cla_reason")] public string? Reason { get; set; }
}

public sealed class NewsNiche
{
    [JsonPropertyName("nic_id")] public int Id { get; set; }
    [JsonPropertyName("nic_name")] public string? Name { get; set; }
    [JsonPropertyName("nic_category")] public string? Category { get; set; }
    [JsonPropertyName("nic_description")] public string? Description { get; set; }
}

public sealed class NewsAnalysis
{
    [JsonPropertyName("anl_summary")] public string? Summary { get; set; }
    [JsonPropertyName("anl_response_text")] public string? ResponseText { get; set; }
    [JsonPropertyName("anl_direction")] public string? Direction { get; set; }
    [JsonPropertyName("anl_market_sentiment")] public string? MarketSentiment { get; set; }
    [JsonPropertyName("anl_confidence")] public decimal? Confidence { get; set; }
    [JsonPropertyName("anl_risk_level")] public string? RiskLevel { get; set; }
    [JsonPropertyName("anl_timeframe")] public string? Timeframe { get; set; }
    [JsonPropertyName("anl_trigger_type")] public string? TriggerType { get; set; }
    [JsonPropertyName("anl_trigger_reason")] public string? TriggerReason { get; set; }
    [JsonPropertyName("anl_created_at")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("relationships")] public List<string> Relationships { get; set; } = new();
    [JsonPropertyName("assets")] public List<AnalysisAsset> Assets { get; set; } = new();
    [JsonPropertyName("supporting_sources")] public List<NewsSourceDetail> SupportingSources { get; set; } = new();
    public string DirectionDisplay => NewsDisplay.Capitalize(Direction);
    public string MarketSentimentDisplay => NewsDisplay.Capitalize(MarketSentiment);
    public string RiskLevelDisplay => NewsDisplay.Capitalize(RiskLevel);
    public string TimeframeDisplay => NewsDisplay.Capitalize(Timeframe);
    public string TriggerTypeDisplay => NewsDisplay.Capitalize(TriggerType);
    public string ConfidenceDisplay => Confidence is null ? "Confidence unavailable" : $"Confidence: {Confidence:0}%";
    public bool IsHighConfidence => Confidence >= 70;
    public bool IsMediumConfidence => Confidence >= 40 && Confidence < 70;
    public bool IsLowConfidence => Confidence is not null && Confidence < 40;
}

public sealed class AnalysisAsset
{
    [JsonPropertyName("asset_symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("asset_name")] public string? Name { get; set; }
    [JsonPropertyName("asset_type")] public string? Type { get; set; }
    [JsonPropertyName("aas_direction")] public string? Direction { get; set; }
    [JsonPropertyName("aas_confidence")] public decimal? Confidence { get; set; }
    [JsonPropertyName("aas_timeframe")] public string? Timeframe { get; set; }
    [JsonPropertyName("aas_reason")] public string? Reason { get; set; }
    [JsonPropertyName("aas_price_context")] public JsonElement? PriceContext { get; set; }
}

internal static class NewsDisplay
{
    public static string Capitalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unavailable";
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
