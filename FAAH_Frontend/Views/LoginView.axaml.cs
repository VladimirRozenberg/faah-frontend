using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using FAAH_Frontend.ViewModels;

namespace FAAH_Frontend.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void PasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ShellViewModel shell) return;
        e.Handled = true;
        if (shell.LoginCommand.CanExecute(null))
            shell.LoginCommand.Execute(null);
    }
}
