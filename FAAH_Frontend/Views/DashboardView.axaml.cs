using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private async void OpenOpportunityDetails(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Opportunity opportunity } button) return;

        var details = new OpportunityDetailWindow { DataContext = opportunity };
        var owner = TopLevel.GetTopLevel(button) as Window;
        if (owner != null) await details.ShowDialog(owner);
        else details.Show();
    }
}
