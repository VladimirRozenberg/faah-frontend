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
