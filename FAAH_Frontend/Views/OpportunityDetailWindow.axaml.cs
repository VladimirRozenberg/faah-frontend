using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FAAH_Frontend.Views;

public partial class OpportunityDetailWindow : Window
{
    public OpportunityDetailWindow()
    {
        InitializeComponent();
    }

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}
