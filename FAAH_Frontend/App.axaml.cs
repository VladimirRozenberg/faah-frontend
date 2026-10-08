using Avalonia;
using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FAAH_Frontend.Controls;
using FAAH_Frontend.ViewModels;
using FAAH_Frontend.Views;

namespace FAAH_Frontend;

public partial class App : Application
{
    public override void Initialize()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = new ShellViewModel()
            };
            MiddleButtonAutoScroll.Attach(mainWindow);
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
