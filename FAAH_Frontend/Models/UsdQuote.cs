namespace FAAH_Frontend.Models;

// Estimation envoyée par le backend ; les comptes et portefeuilles restent en USD.
public sealed class UsdQuote
{
    public required string Symbol { get; set; }
    public required string OriginalCurrency { get; set; }
    public required decimal OriginalPrice { get; set; }
    public required decimal PriceUsd { get; set; }
    public required decimal RateToUsd { get; set; }
    public required string RateDate { get; set; }
}
