using System;
using System.Collections.Generic;

namespace FAAH_Frontend.Models;

public sealed class RecentRecommendation
{
    public int RecommendationId { get; set; }
    public int PortfolioId { get; set; }
    public string PortfolioName { get; set; } = "";
    public int RunId { get; set; }
    public string Kind { get; set; } = "";
    public int? AssetId { get; set; }
    public string? AssetSymbol { get; set; }
    public int? SignalId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public decimal? Confidence { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public string AssetSymbolDisplay => string.IsNullOrWhiteSpace(AssetSymbol) ? "General recommendation" : AssetSymbol;
    public string ConfidenceDisplay => Confidence.HasValue ? $"{Confidence.Value:0.#}% confidence" : "Confidence unavailable";
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd MMM yyyy · HH:mm");
    public bool HasSignalId => SignalId.HasValue;
    public string SignalDisplay => SignalId.HasValue ? $"Signal #{SignalId.Value}" : "";
}

public sealed class RecentRecommendationResponse
{
    public int Count { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<RecentRecommendation> Items { get; set; } = new();
}
