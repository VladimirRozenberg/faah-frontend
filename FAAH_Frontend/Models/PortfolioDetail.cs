using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace FAAH_Frontend.Models;

public sealed class PortfolioDetail
{
    public int Id { get; set; }
    public int UserId { get; set; }
    [JsonPropertyName("is_active")]
    public bool? IsActive { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int PositionsCount { get; set; }
    public decimal? Balance { get; set; }
    public decimal? TotalInvested { get; set; }
    public decimal? TotalCurrentValue { get; set; }
    public decimal? TotalProfitLoss { get; set; }
    public List<PortfolioPosition> Positions { get; set; } = new();
}

public sealed class PortfolioPosition
{
    public int AssetId { get; set; }
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal? AveragePurchasePrice { get; set; }
    public decimal? InvestedAmount { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal? CurrentValue { get; set; }
    public decimal? ProfitLoss { get; set; }
    public decimal? ProfitLossPercent { get; set; }
    [JsonIgnore] public bool IsAlternateRow { get; set; }

    public string QuantityDisplay => Quantity.ToString("0.########", CultureInfo.InvariantCulture);
    public string AveragePurchasePriceDisplay => FormatAmount(AveragePurchasePrice);
    public string CurrentPriceDisplay => FormatAmount(CurrentPrice);
    public string CurrentValueDisplay => FormatAmount(CurrentValue);
    public string ProfitLossDisplay => ProfitLoss.HasValue
        ? $"{(ProfitLoss.Value >= 0 ? "+" : "−")}{Math.Abs(ProfitLoss.Value):N2}"
        : "—";
    public string ProfitLossPercentDisplay => ProfitLossPercent.HasValue
        ? $"{(ProfitLossPercent.Value >= 0 ? "+" : "−")}{Math.Abs(ProfitLossPercent.Value):0.##}%"
        : "—";
    public bool IsProfit => ProfitLoss.HasValue && ProfitLoss.Value >= 0;
    public bool IsLoss => ProfitLoss.HasValue && ProfitLoss.Value < 0;

    private static string FormatAmount(decimal? value) => value?.ToString("N2", CultureInfo.InvariantCulture) ?? "—";
}

public sealed class PortfolioTransactionsResponse
{
    public int Count { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<PortfolioTransaction> Transactions { get; set; } = new();
}

public sealed class PortfolioTransaction
{
    public int Id { get; set; }
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal? Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    [JsonIgnore] public bool IsAlternateRow { get; set; }

    public string QuantityDisplay => Quantity.ToString("0.########", CultureInfo.InvariantCulture);
    public string PriceDisplay => FormatAmount(Price);
    public string AmountDisplay => FormatAmount(Amount);
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd MMM yyyy · HH:mm");
    public string TypeDisplay => Type.ToUpperInvariant();
    public bool IsBuy => string.Equals(Type, "buy", StringComparison.OrdinalIgnoreCase);
    public bool IsSell => string.Equals(Type, "sell", StringComparison.OrdinalIgnoreCase);

    private static string FormatAmount(decimal? value) => value?.ToString("N2", CultureInfo.InvariantCulture) ?? "—";
}
