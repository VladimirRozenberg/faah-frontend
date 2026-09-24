using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FAAH_Frontend.Views;

public partial class AssetDetailView : UserControl
{
    public AssetDetailView() => InitializeComponent();

    private async void OpenArticle(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string url) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return;
        var window = TopLevel.GetTopLevel(this);
        if (window is null) return;
        try { await window.Launcher.LaunchUriAsync(uri); }
        catch (Exception) { button.Content = "Impossible d’ouvrir le lien"; }
    }
}
