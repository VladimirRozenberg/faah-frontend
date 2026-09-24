using System;
using System.Collections.Generic;
using System.Globalization;
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
    public decimal? Price { get; set; }
    public decimal? ChangePercent { get; set; }
    public long? MarketVolume { get; set; }
    private bool _loadingPrice;
    public bool IsLoadingPrice { get => _loadingPrice; set { if (SetField(ref _loadingPrice, value)) RefreshQuoteDisplay(); } }
    public void RefreshQuoteDisplay()
    {
        foreach (var name in new[] { nameof(PriceDisplay), nameof(ChangeDisplay), nameof(Volume), nameof(IsUp), nameof(IsDown) }) OnPropertyChanged(name);
    }
    private bool _favorite;
    public bool IsFavorite { get => _favorite; set { if (SetField(ref _favorite,value)) OnPropertyChanged(nameof(StarGlyph)); } }
    public string IconText => Symbol.Length > 2 ? Symbol[..2] : Symbol;
    public string IconColor => "#4B5A78";
    public string PriceDisplay => IsLoadingPrice ? "Loading…" : Price is null ? "Unavailable" : Price.Value.ToString("N2",CultureInfo.CurrentCulture) + " " + Currency;
    public string ChangeDisplay => IsLoadingPrice ? "Loading…" : ChangePercent?.ToString("+0.00;-0.00;0.00",CultureInfo.CurrentCulture) + (ChangePercent is null ? "Unavailable" : "%");
    public string Volume => IsLoadingPrice ? "Loading…" : MarketVolume?.ToString("N0",CultureInfo.CurrentCulture) ?? "Unavailable";
    public bool IsUp => ChangePercent > 0;
    public bool IsDown => ChangePercent < 0;
    public string StarGlyph => IsFavorite ? "★" : "☆";
}
public sealed class AssetResponse { public required List<Asset> Items { get; set; } }
public sealed class FavoriteResponse { public required List<int> AssetIds { get; set; } }
public sealed class MarketResponse { public required List<MarketQuote> Items { get; set; } }
public sealed class MarketQuote
{
    public required string Symbol { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? ChangePercent { get; set; }
    public long? Volume { get; set; }
    public string? Currency { get; set; }
}
