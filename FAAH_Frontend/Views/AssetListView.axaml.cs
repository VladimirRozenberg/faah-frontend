using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using FAAH_Frontend.ViewModels;

namespace FAAH_Frontend.Views;

public partial class AssetListView : UserControl
{
    public AssetListView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateFiltersLayout();
    }

    private bool? _wideLayout;
    private void UpdateFiltersLayout()
    {
        bool wide = Bounds.Width >= 1050;
        if (_wideLayout == wide) return;
        _wideLayout = wide;
        // Le panneau reste visible : à droite en grand écran, au-dessus en petite fenêtre.
        CatalogGrid.ColumnDefinitions[1].Width = new GridLength(wide ? 270 : 0);
        Grid.SetColumn(FiltersPanel, wide ? 1 : 0);
        Grid.SetRowSpan(FiltersPanel, wide ? 3 : 1);
        FiltersPanel.Margin = wide ? new Thickness(16, 12, 0, 0) : new Thickness(0, 12, 0, 0);
        FilterScroll.MaxHeight = wide ? 560 : 220;
    }

    private void PageInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not AssetListViewModel viewModel ||
            !viewModel.GoToPageCommand.CanExecute(viewModel.PageInput)) return;

        viewModel.GoToPageCommand.Execute(viewModel.PageInput);
        e.Handled = true;
    }

    // Entrée déclenche exactement la même commande que le bouton Search.
    private void SearchInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not AssetListViewModel viewModel ||
            !viewModel.SearchCommand.CanExecute(null)) return;
        viewModel.SearchCommand.Execute(null);
        e.Handled = true;
    }
}
