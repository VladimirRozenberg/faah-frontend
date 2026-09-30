using Avalonia;
using Avalonia.Controls;

namespace FAAH_Frontend.Views;

public partial class AssetDetailView : UserControl
{
    public AssetDetailView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutForWidth();
    }

    private void UpdateLayoutForWidth()
    {
        // Seul le résumé change de disposition. Le graphique garde toute la largeur.
        bool wide = Bounds.Width >= 1050;
        OverviewGrid.ColumnDefinitions[1].Width = wide ? new GridLength(440) : new GridLength(0);
        Grid.SetColumn(TradingPanel, wide ? 1 : 0);
        Grid.SetRow(TradingPanel, wide ? 0 : 1);
        TradingPanel.Margin = wide ? new Thickness(28, 0, 0, 0) : new Thickness(0, 20, 0, 0);
    }
}
