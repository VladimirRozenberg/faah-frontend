using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FAAH_Frontend.Views;

public partial class HealthDetailsWindow : Window
{
    public HealthDetailsWindow()
    {
        InitializeComponent();
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();
}
