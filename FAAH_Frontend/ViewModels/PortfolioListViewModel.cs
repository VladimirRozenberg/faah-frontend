using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public class PortfolioListViewModel : ViewModelBase
{
    public PortfolioListViewModel(ShellViewModel shell)
    {
        Portfolios = shell.Portfolios;
        Portfolios.CollectionChanged += OnPortfoliosChanged;
        UpdateAlternateRows();
        CreateCommand = new RelayCommand(shell.ShowPortfolioCreate);
        ToggleStatusCommand = new RelayCommand(p =>
        {
            if (p is Portfolio portfolio)
                _ = shell.UpdatePortfolioActiveStateAsync(portfolio, portfolio.IsActive, !portfolio.IsActive);
        }, p => p is Portfolio portfolio && !portfolio.IsStatusUpdating);
        OpenCommand = new RelayCommand(p =>
        {
            if (p is Portfolio portfolio) shell.ShowPortfolio(portfolio);
        });
    }

    public ICommand CreateCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand ToggleStatusCommand { get; }

    public ObservableCollection<Portfolio> Portfolios { get; }

    private void OnPortfoliosChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateAlternateRows();

    private void UpdateAlternateRows()
    {
        for (var index = 0; index < Portfolios.Count; index++)
            Portfolios[index].IsAlternateRow = index % 2 == 1;
    }
}
