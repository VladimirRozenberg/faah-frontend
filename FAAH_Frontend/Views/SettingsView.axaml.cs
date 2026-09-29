using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FAAH_Frontend.ViewModels;

namespace FAAH_Frontend.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void ConfirmSave(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewPassword.Text) || string.IsNullOrWhiteSpace(ConfirmPassword.Text))
        {
            SaveStatus.Text = "Enter and confirm a new password.";
            return;
        }

        if (NewPassword.Text != ConfirmPassword.Text)
        {
            SaveStatus.Text = "The two passwords do not match.";
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var dialog = new Window
        {
            Title = "Confirm changes",
            Width = 380,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var confirm = new Button { Content = "Confirm", MinWidth = 90 };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = "Do you want to confirm these changes?", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, confirm }
                }
            }
        };

        if (!await dialog.ShowDialog<bool>(owner)) return;
        if (DataContext is not ShellViewModel shell) return;

        try
        {
            using var response = await shell.Http.PutAsJsonAsync(
                "auth/me/password",
                new { new_password = NewPassword.Text },
                ShellViewModel.JsonOptions);

            if (!response.IsSuccessStatusCode)
            {
                SaveStatus.Text = "Password update failed.";
                return;
            }

            NewPassword.Text = string.Empty;
            ConfirmPassword.Text = string.Empty;
            SaveStatus.Text = "Password updated successfully.";
        }
        catch
        {
            SaveStatus.Text = "Unable to reach the server.";
        }
    }
}
