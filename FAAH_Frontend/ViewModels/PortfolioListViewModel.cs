using System.Collections.ObjectModel;
using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public class PortfolioListViewModel : ViewModelBase
{
    public PortfolioListViewModel(ShellViewModel shell)
    {
        OpenCommand = new RelayCommand(p =>
        {
            if (p is Portfolio portfolio) shell.ShowPortfolio(portfolio);
        });
    }

    public ICommand OpenCommand { get; }

    public ObservableCollection<Portfolio> Portfolios { get; } = new()
    {
        new Portfolio
        {
            Name = "Momentum Alpha", Description = "Short-term futures momentum",
            Risk = RiskLevel.High, MaxPositions = 8, ReturnPercent = 18.4m,
            Status = PortfolioStatus.Active
        },
        new Portfolio
        {
            Name = "Risky Takes", Description = "High-leverage speculative play",
            Risk = RiskLevel.VeryHigh, MaxPositions = 12, ReturnPercent = -4.2m,
            Status = PortfolioStatus.Active
        },
        new Portfolio
        {
            Name = "Conservative Investment", Description = "Blue-chip long-only, capital preservation",
            Risk = RiskLevel.Low, MaxPositions = 4, ReturnPercent = 6.1m,
            Status = PortfolioStatus.Active
        },
        new Portfolio
        {
            Name = "Arb Core", Description = "Cross-exchange arbitrage sleeve",
            Risk = RiskLevel.Medium, MaxPositions = 15, ReturnPercent = 9.7m,
            Status = PortfolioStatus.Active
        },
        new Portfolio
        {
            Name = "Legacy Swing", Description = "Old swing strategy, paused",
            Risk = RiskLevel.Medium, MaxPositions = 5, ReturnPercent = 1.3m,
            Status = PortfolioStatus.Paused
        }
    };
}
