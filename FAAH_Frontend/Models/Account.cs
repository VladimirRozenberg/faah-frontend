using System;
using System.Collections.Generic;
namespace FAAH_Frontend.Models;
public class AccountSnapshot
{
    public int? Id { get; set; }
    public decimal? Balance { get; set; }
    public decimal? AvailableBalance => Balance;
    public decimal? TotalCurrentValue { get; set; }
    public string BaseCurrency { get; set; } = "USD";
    public int PositionsCount { get; set; }
}
public class AccountTransaction
{
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Quantity { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset CreatedAt { get; set; }
    public string DisplayName => Type == "deposit" ? "Account top-up" : Name;
    public string Label => $"{CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} · {Type.ToUpperInvariant()} · {Symbol} · {Quantity:0.########} units · {Amount:N2} {Currency}";
}
public class AssetTransactionSummary
{
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public int TransactionCount { get; set; }
    public int BuyCount { get; set; }
    public int SellCount { get; set; }
    public string Label => $"{Symbol} — {TransactionCount} {(TransactionCount == 1 ? "transaction" : "transactions")} ({BuyCount} {(BuyCount == 1 ? "buy" : "buys")} / {SellCount} {(SellCount == 1 ? "sell" : "sells")})";
}
public class TransactionPage
{
    public List<AccountTransaction> Transactions { get; set; } = new();
    public List<AssetTransactionSummary> ByAsset { get; set; } = new();
}
public class Opportunity
{
    public int RecommendationId { get; set; }
    public int PortfolioId { get; set; }
    public string PortfolioName { get; set; } = "";
    public int RunId { get; set; }
    public int? AssetId { get; set; }
    public string AssetSymbol { get; set; } = "";
    public int? SignalId { get; set; }
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public int? Confidence { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int SigId => RecommendationId;
    public string SigAction => Action;
    public int? SigConfidence => Confidence;
    public string Label => string.IsNullOrWhiteSpace(PortfolioName) ? AssetSymbol : $"{AssetSymbol} — {PortfolioName}";
    public string Context => $"{CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} · Confidence: {Confidence?.ToString() ?? "unavailable"} · Status: {Status ?? "unknown"}";
    public string Explanation => string.IsNullOrWhiteSpace(Reason) ? "No explanation is available for this signal." : Reason;
}
public class OpportunityPage { public int Count { get; set; } public List<Opportunity> Items { get; set; } = new(); }
public class AssetValueResponse
{
    public string Currency { get; set; } = "USD";
    public bool ValuationComplete { get; set; }
    public List<string> MissingPriceSymbols { get; set; } = new();
    public decimal? TotalCurrentValue { get; set; }
    public decimal? TotalInvested { get; set; }
    public decimal? TotalProfitLoss { get; set; }
}
public class AvailableCashResponse
{
    public string Currency { get; set; } = "USD";
    public decimal? AvailableCash { get; set; }
}
