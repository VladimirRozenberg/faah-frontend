using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using FAAH_Frontend.ViewModels;
namespace FAAH_Frontend.Models;
public sealed class Asset : ViewModelBase
{
    public required int Id { get; set; }
    public required string Symbol { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Exchange { get; set; }
    public string? Country { get; set; }
    public string? Sector { get; set; }
    public string? Industry { get; set; }
    public string? Currency { get; set; }
    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; set; }

    // Logo est l'image affichée par Avalonia, pas une donnée JSON du backend.
    private Bitmap? _logo;
    [JsonIgnore]
    public Bitmap? Logo
    {
        get => _logo;
        set
        {
            if (SetField(ref _logo, value)) OnPropertyChanged(nameof(HasLogo));
        }
    }
    [JsonIgnore]
    public bool HasLogo => Logo is not null;
    public decimal? Price { get; set; }
    public decimal? ChangePercent { get; set; }
    public long? MarketVolume { get; set; }
    public AssetListMarket? Market { get; set; }
    private bool _loadingPrice;
    public bool IsLoadingPrice { get => _loadingPrice; set { if (SetField(ref _loadingPrice, value)) RefreshQuoteDisplay(); } }
    public void RefreshQuoteDisplay()
    {
        foreach (var name in new[] { nameof(PriceDisplay), nameof(ChangeDisplay), nameof(Volume), nameof(IsUp), nameof(IsDown), nameof(IsFlat), nameof(MarketStatus) }) OnPropertyChanged(name);
    }
    private bool _favorite;
    public bool IsFavorite { get => _favorite; set { if (SetField(ref _favorite,value)) OnPropertyChanged(nameof(StarGlyph)); } }
    public string IconText => Symbol.Length > 2 ? Symbol[..2] : Symbol;
    public string IconColor => "#4B5A78";
    public string PriceDisplay => Market?.LastPrice is null ? "Market data unavailable" : Market.LastPrice.Value.ToString("N2", CultureInfo.CurrentCulture) + " " + (Market.Currency ?? Currency);
    public string ChangeDisplay => Market?.ChangePercent is null ? "Market data unavailable" : Market.ChangePercent.Value.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture) + "%";
    public string ChangeAmountDisplay => Market?.Change?.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture) ?? "Market data unavailable";
    public string PreviousCloseDisplay => Market?.PreviousClose?.ToString("N2", CultureInfo.CurrentCulture) ?? "Market data unavailable";
    public string Volume => Market is null ? "Market data unavailable" : FormatCompact(Market.Volume);
    public bool IsUp => Market?.ChangePercent > 0;
    public bool IsDown => Market?.ChangePercent < 0;
    public bool IsFlat => Market is not null && Market.ChangePercent == 0;
    public string MarketStatus => Market?.RetrievedAtDisplay ?? "Market data unavailable";
    private static string FormatCompact(long? value)
    {
        if (value is null) return "Unavailable";
        decimal number = value.Value;
        string suffix = "";
        decimal divisor = 1;
        if (Math.Abs(number) >= 1_000_000_000) { divisor = 1_000_000_000; suffix = "B"; }
        else if (Math.Abs(number) >= 1_000_000) { divisor = 1_000_000; suffix = "M"; }
        else if (Math.Abs(number) >= 1_000) { divisor = 1_000; suffix = "K"; }
        return (number / divisor).ToString(divisor == 1 ? "N0" : "0.##", CultureInfo.CurrentCulture) + suffix;
    }
    public string StarGlyph => IsFavorite ? "★" : "☆";
}
public sealed class AssetResponse
{
    public int Count { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public required List<Asset> Items { get; set; }
}
public sealed class FavoriteResponse { public required List<int> AssetIds { get; set; } }
public sealed class MarketResponse { public required List<MarketQuote> Items { get; set; } }
public sealed class AssetListMarket
{
    public string? Symbol { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? LogoUrl { get; set; }
    public string? Exchange { get; set; }
    public string? Currency { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? PreviousClose { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }
    public long? Volume { get; set; }
    public DateTimeOffset? RetrievedAt { get; set; }
    public string? Source { get; set; }
    public string RetrievedAtDisplay => RetrievedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "";
}
public sealed class MarketQuote
{
    public required string Symbol { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? ChangePercent { get; set; }
    public long? Volume { get; set; }
    public string? Currency { get; set; }
}
