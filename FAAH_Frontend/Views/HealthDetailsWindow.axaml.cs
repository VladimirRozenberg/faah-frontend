using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FAAH_Frontend.Controls;

namespace FAAH_Frontend.Views;

public partial class HealthDetailsWindow : Window
{
    private PixelPoint _restorePosition;
    private double _restoreWidth;
    private double _restoreHeight;
    private bool _isMaximized;

    public HealthDetailsWindow()
    {
        InitializeComponent();
        MiddleButtonAutoScroll.Attach(this);
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
        var button = this.FindControl<Button>("MaximizeRestoreButton");
        if (button is not null)
            button.Content = _isMaximized ? "\u2750" : "\u25A1";
    }
}
