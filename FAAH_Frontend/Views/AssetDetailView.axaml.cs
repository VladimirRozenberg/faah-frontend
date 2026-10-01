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
        // Le graphique reste en dehors de ces grilles : sa largeur ne change pas.
        bool wide = Bounds.Width >= 950;
        OverviewGrid.ColumnDefinitions[1].Width = wide ? new GridLength(360) : new GridLength(0);
        Grid.SetColumn(TradingPanel, wide ? 1 : 0);
        Grid.SetRow(TradingPanel, wide ? 0 : 1);
        TradingPanel.Margin = wide ? new Thickness(24, 0, 0, 0) : new Thickness(0, 20, 0, 0);
        PageContent.Margin = Bounds.Width < 700 ? new Thickness(20) : new Thickness(42, 26);

        // 4 colonnes sur grand écran, 2 sur moyen écran, 1 sur petit écran.
        int columns = Bounds.Width >= 1200 ? 4 : Bounds.Width >= 700 ? 2 : 1;
        Control[] sections = { OrderIdentity, OrderQuantity, OrderSummary, OrderActions };
        for (int i = 0; i < sections.Length; i++)
        {
            OrderGrid.ColumnDefinitions[i].Width = i < columns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            int column = i % columns;
            int row = i / columns;
            Grid.SetColumn(sections[i], column);
            Grid.SetRow(sections[i], row);
            sections[i].Margin = new Thickness(column == 0 ? 0 : 24, row == 0 ? 0 : 18, 0, 0);
        }
    }
}
