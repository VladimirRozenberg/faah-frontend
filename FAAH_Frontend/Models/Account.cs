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
    public int SigId { get; set; }
    public int? SigPrtId { get; set; }
    public string? SigStatus { get; set; }
    public DateTimeOffset? SigExpiresAt { get; set; }
    public string AssetSymbol { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string SigAction { get; set; } = "";
    public string? AnalysisSummary { get; set; }
    public string? SigTimeframe { get; set; }
    public int? SigConfidence { get; set; }
    public DateTimeOffset SigCreatedAt { get; set; }
    public string Label => $"{AssetSymbol} — {AssetName}";
    public string Context => $"Analysis dated {SigCreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} · Confidence: {SigConfidence?.ToString() ?? "unavailable"} · Timeframe: {SigTimeframe ?? "not specified"}";
    public string Explanation => string.IsNullOrWhiteSpace(AnalysisSummary) ? "No explanation is available for this signal." : AnalysisSummary;
}
public class OpportunityPage { public List<Opportunity> Items { get; set; } = new(); }
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
