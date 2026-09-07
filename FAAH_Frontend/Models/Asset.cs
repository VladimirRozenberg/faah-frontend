using System;
using System.Globalization;

namespace FAAH_Frontend.Models;

public class Asset
{
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Texte de la pastille : "₿", "Ξ", "ES", "CL"…</summary>
    public string IconText { get; set; } = string.Empty;

    /// <summary>Couleur de la pastille, au format "#RRGGBB".</summary>
    public string IconColor { get; set; } = "#4B5A78";

    public decimal Price { get; set; }

    /// <summary>Nombre de décimales à afficher pour le prix (0 pour BTC, 2 pour ES-FUT).</summary>
    public int PriceDecimals { get; set; }

    public decimal ChangePercent { get; set; }

    public string Volume { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }

    // ---- Propriétés d'affichage consommées par la vue ----

    public string PriceDisplay =>
        "$" + Price.ToString("N" + PriceDecimals, CultureInfo.InvariantCulture);

    public string ChangeDisplay =>
        (ChangePercent >= 0 ? "+" : "−") +
        Math.Abs(ChangePercent).ToString("0.00", CultureInfo.InvariantCulture) + "%";

    public bool IsUp => ChangePercent >= 0;

    public bool IsDown => !IsUp;

    public string StarGlyph => IsFavorite ? "★" : "☆";
}
