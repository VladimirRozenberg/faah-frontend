using System;
using System.Globalization;

namespace FAAH_Frontend.Models;

public enum RiskLevel { Low, Medium, High, VeryHigh }

public enum PortfolioStatus { Active, Paused }

public class Portfolio
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public RiskLevel Risk { get; set; }

    public int MaxPositions { get; set; }

    public decimal ReturnPercent { get; set; }

    public string Currency { get; set; } = "USD";

    public PortfolioStatus Status { get; set; }

    // ---- Propriétés d'affichage ----

    public string RiskDisplay => Risk switch
    {
        RiskLevel.Low => "Low",
        RiskLevel.Medium => "Medium",
        RiskLevel.High => "High",
        RiskLevel.VeryHigh => "Very High",
        _ => string.Empty
    };

    public string ReturnDisplay =>
        (ReturnPercent >= 0 ? "+" : "−") +
        Math.Abs(ReturnPercent).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public bool IsUp => ReturnPercent >= 0;

    public bool IsDown => !IsUp;

    public string StatusDisplay => Status == PortfolioStatus.Active ? "ACTIVE" : "PAUSED";

    public bool IsActive => Status == PortfolioStatus.Active;
}
