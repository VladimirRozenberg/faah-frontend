using Avalonia.Controls;
using Avalonia.Input;
using FAAH_Frontend.ViewModels;

namespace FAAH_Frontend.Views;

public partial class UserListView : UserControl
{
    public UserListView()
    {
        InitializeComponent();
    }

    private void PageInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not UserListViewModel viewModel ||
            !viewModel.GoToPageCommand.CanExecute(viewModel.PageInput)) return;

        viewModel.GoToPageCommand.Execute(viewModel.PageInput);
        e.Handled = true;
    }
}
