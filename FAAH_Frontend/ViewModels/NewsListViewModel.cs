using System.Collections.ObjectModel;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public class NewsListViewModel : ViewModelBase
{
    public ObservableCollection<NewsArticle> Articles { get; } = new()
    {
        new NewsArticle
        {
            Title = "Fed signals possible rate cut as inflation cools further",
            Summary = "Markets rallied on the news, with futures pointing to a stronger open across major indices.",
            Source = "Reuters", RelatedAssets = "ES-FUT, SPX",
            Sentiment = Sentiment.Bullish, Age = "12m ago"
        },
        new NewsArticle
        {
            Title = "Crude inventories rise unexpectedly, pressuring oil futures",
            Summary = "EIA data showed a build against forecasts of a drawdown, sending WTI lower in early trading.",
            Source = "Bloomberg", RelatedAssets = "CL-FUT",
            Sentiment = Sentiment.Bearish, Age = "34m ago"
        },
        new NewsArticle
        {
            Title = "Bitcoin consolidates near $96K as traders await CPI print",
            Summary = "Volume has thinned out ahead of tomorrow's inflation data, with volatility compressing across majors.",
            Source = "CoinDesk", RelatedAssets = "BTC-USD",
            Sentiment = Sentiment.Neutral, Age = "1h ago"
        },
        new NewsArticle
        {
            Title = "Ethereum network upgrade completes ahead of schedule",
            Summary = "The upgrade improves throughput and cuts fees, with developers reporting a smooth rollout.",
            Source = "The Block", RelatedAssets = "ETH-USD",
            Sentiment = Sentiment.Bullish, Age = "2h ago"
        }
    };
}
