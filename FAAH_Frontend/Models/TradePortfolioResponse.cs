using System.Collections.Generic;

namespace FAAH_Frontend.Models;

// Seuls les champs utiles de la reponse portfolio.schemas.PortfolioResponse.
public class TradePortfolioResponse
{
    public required int UserId { get; set; }
    public required List<TradePosition> Positions { get; set; }
}

public class TradePosition
{
    public required string Symbol { get; set; }
    public required decimal Quantity { get; set; }
}
