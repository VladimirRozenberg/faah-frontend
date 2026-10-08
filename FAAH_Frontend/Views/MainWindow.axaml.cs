using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;

namespace FAAH_Frontend.Views;

public partial class MainWindow : Window
{
    private PixelPoint _restorePosition;
    private double _restoreWidth;
    private double _restoreHeight;
    private bool _isMaximized;

    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeButton();
    }

    private void TitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            MaximizeRestoreClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
    }

    private void MinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (_isMaximized)
        {
            WindowState = WindowState.Normal;
            Position = _restorePosition;
            Width = _restoreWidth;
            Height = _restoreHeight;
            _isMaximized = false;
        }
        else
        {
            _restorePosition = Position;
            _restoreWidth = Bounds.Width;
            _restoreHeight = Bounds.Height;

            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is not null)
            {
                WindowState = WindowState.Normal;
                Position = screen.WorkingArea.Position;
                Width = screen.WorkingArea.Width / screen.Scaling;
                Height = screen.WorkingArea.Height / screen.Scaling;
                _isMaximized = true;
            }
        }

        UpdateMaximizeButton();
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeButton()
    {
        var icon = this.FindControl<Avalonia.Controls.Shapes.Path>("MaximizeRestoreIcon");
        if (icon is not null)
            icon.Data = Avalonia.Media.Geometry.Parse(_isMaximized
                ? "M2.5,2.5 L2.5,0.5 L9.5,0.5 L9.5,7.5 L7.5,7.5 M0.5,2.5 L7.5,2.5 L7.5,9.5 L0.5,9.5 Z"
                : "M0.5,0.5 L9.5,0.5 L9.5,9.5 L0.5,9.5 Z");
    }
}
