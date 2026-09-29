using System.Collections.Generic;

namespace FAAH_Frontend.Models;

// Seuls les champs utiles de la reponse portfolio.schemas.PortfolioResponse.
public class TradePortfolioResponse
{
    public required int Id { get; set; }
    public required int UserId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public string BaseCurrency { get; set; } = "";
    public List<string> PreferredAssetTypes { get; set; } = new();
    public required List<TradePosition> Positions { get; set; }
}

public class TradePortfolioListResponse
{
    public required List<TradePortfolioResponse> Items { get; set; }
}

public class TradePosition
{
    public required string Symbol { get; set; }
    public required decimal Quantity { get; set; }
}
