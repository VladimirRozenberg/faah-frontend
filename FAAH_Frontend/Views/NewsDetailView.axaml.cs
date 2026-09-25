using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FAAH_Frontend.Views;

public partial class NewsDetailView : UserControl
{
    public NewsDetailView()
    {
        InitializeComponent();
    }

    private void OpenOriginalArticle(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string url ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;

        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
    }
}
