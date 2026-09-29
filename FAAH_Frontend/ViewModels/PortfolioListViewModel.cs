using System.Collections.ObjectModel;
using System.Windows.Input;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.ViewModels;

public class PortfolioListViewModel : ViewModelBase
{
    public PortfolioListViewModel(ShellViewModel shell)
    {
        Portfolios = shell.Portfolios;
        CreateCommand = new RelayCommand(shell.ShowPortfolioCreate);
        OpenCommand = new RelayCommand(p =>
        {
            if (p is Portfolio portfolio) shell.ShowPortfolio(portfolio);
        });
    }

    public ICommand CreateCommand { get; }
    public ICommand OpenCommand { get; }

    public ObservableCollection<Portfolio> Portfolios { get; }
}
