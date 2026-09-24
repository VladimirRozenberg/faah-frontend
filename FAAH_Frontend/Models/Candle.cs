using System;
using System.Collections.Generic;

namespace FAAH_Frontend.Models;

// Correspond a la reponse Python de /api/assets/{symbol}/candles.
public class Candle
{
    public required DateTimeOffset Timestamp { get; set; }
    public required decimal Open { get; set; }
    public required decimal High { get; set; }
    public required decimal Low { get; set; }
    public required decimal Close { get; set; }
    public decimal? Volume { get; set; }
}

public class CandleResponse
{
    public required string Symbol { get; set; }
    public required List<Candle> Candles { get; set; }
}
