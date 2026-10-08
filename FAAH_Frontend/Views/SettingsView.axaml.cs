using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
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
            SetSaveStatus("Enter and confirm a new password.", Avalonia.Media.Brushes.Red);
            return;
        }

        if (NewPassword.Text != ConfirmPassword.Text)
        {
            SetSaveStatus("The two passwords do not match.", Avalonia.Media.Brushes.Red);
            return;
        }

        var password = NewPassword.Text;
        if (password.Length < 8 || !password.Any(char.IsDigit) || Encoding.UTF8.GetByteCount(password) > 72)
        {
            SetSaveStatus("Password must be at least 8 characters, include at least one digit (0–9), and be no more than 72 UTF-8 bytes.", Avalonia.Media.Brushes.Red);
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var dialog = new Window
        {
            Title = "Confirm changes",
            Width = 420,
            Height = 240,
            MinWidth = 420,
            MinHeight = 240,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.White,
            WindowDecorations = WindowDecorations.None,
        };

        var cancel = new Button { Content = "Cancel", Width = 100, Height = 36, Classes = { "ghost" } };
        var confirm = new Button { Content = "Confirm", Width = 100, Height = 36, Classes = { "primary" } };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);

        var spacer = new Border { Background = Avalonia.Media.Brushes.Transparent };
        Grid.SetColumn(spacer, 1);
        var titleBar = new Border
        {
            Classes = { "windowTitleBar" },
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0B2B4C")),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Spacing = 8,
                        Margin = new Thickness(14, 0),
                        Children =
                        {
                            new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FAAH_Frontend/Assets/faah-logo.png"))), Width = 26, Height = 26, Stretch = Avalonia.Media.Stretch.Uniform },
                            new TextBlock { Text = "FAAH", FontSize = 12, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.White, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                            new TextBlock { Text = "CONFIRM PASSWORD", FontSize = 9, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AFC0D0")), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                        }
                    },
                    spacer
                }
            }
        };

        var heading = new TextBlock { Text = "Confirm password change", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold };
        var messageCard = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(10),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F5F6F8")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D9DEE5")),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "Do you want to confirm this password change?", TextWrapping = Avalonia.Media.TextWrapping.Wrap }
        };
        Grid.SetRow(messageCard, 1);
        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { cancel, confirm }
        };
        Grid.SetRow(actions, 2);
        var body = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 16,
            Children = { heading, messageCard, actions }
        };
        Grid.SetRow(body, 1);

        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("38,*"),
            Children = { titleBar, body }
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
                SetSaveStatus(response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity
                    ? "The password was rejected. Use at least 8 characters, include a digit (0–9), and stay within 72 UTF-8 bytes."
                    : $"Password update failed (HTTP {(int)response.StatusCode}).", Avalonia.Media.Brushes.Red);
                return;
            }

            NewPassword.Text = string.Empty;
            ConfirmPassword.Text = string.Empty;
            SetSaveStatus("Password updated successfully.", Avalonia.Media.Brushes.Green);
        }
        catch
        {
            SetSaveStatus("Unable to reach the server.", Avalonia.Media.Brushes.Red);
        }
    }

    private void SetSaveStatus(string message, Avalonia.Media.IBrush color)
    {
        SaveStatus.Text = message;
        SaveStatus.Foreground = color;
    }

}
