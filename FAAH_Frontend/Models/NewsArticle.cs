namespace FAAH_Frontend.Models;

public enum Sentiment { Bullish, Bearish, Neutral }

public class NewsArticle
{
    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    /// <summary>Actifs liés, par ex. "ES-FUT, SPX".</summary>
    public string RelatedAssets { get; set; } = string.Empty;

    public Sentiment Sentiment { get; set; }

    /// <summary>Ancienneté déjà formatée ("12m ago"). À calculer depuis la date côté service.</summary>
    public string Age { get; set; } = string.Empty;

    // ---- Propriétés d'affichage ----

    public string SentimentDisplay => Sentiment.ToString().ToUpperInvariant();

    public string MetaDisplay => $"Source : {Source} · Actifs : {RelatedAssets}";

    public bool IsBullish => Sentiment == Sentiment.Bullish;

    public bool IsBearish => Sentiment == Sentiment.Bearish;
}
