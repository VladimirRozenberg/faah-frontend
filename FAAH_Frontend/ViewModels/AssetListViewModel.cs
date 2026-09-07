using System.Collections.ObjectModel;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public class AssetListViewModel : ViewModelBase
{
    public ObservableCollection<Asset> Assets { get; } = new()
    {
        new Asset
        {
            Symbol = "BTC-USD", IconText = "\u20BF", IconColor = "#F7931A",
            Price = 96412m, PriceDecimals = 0, ChangePercent = 2.14m,
            Volume = "4.2B", IsFavorite = true
        },
        new Asset
        {
            Symbol = "ETH-USD", IconText = "\u039E", IconColor = "#4B5A78",
            Price = 3284m, PriceDecimals = 0, ChangePercent = -0.68m,
            Volume = "1.8B", IsFavorite = true
        },
        new Asset
        {
            Symbol = "ES-FUT", IconText = "ES", IconColor = "#0C3765",
            Price = 5921.50m, PriceDecimals = 2, ChangePercent = 0.31m,
            Volume = "612K", IsFavorite = true
        },
        new Asset
        {
            Symbol = "CL-FUT", IconText = "CL", IconColor = "#2B2B2B",
            Price = 71.84m, PriceDecimals = 2, ChangePercent = -1.02m,
            Volume = "203K", IsFavorite = false
        }
    };

    public string PageLabel => "Page 1 of 10";
}
