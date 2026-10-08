using Avalonia.Controls;
using Avalonia.Input;
using FAAH_Frontend.ViewModels;

namespace FAAH_Frontend.Views;

public partial class PersonalInformationView : UserControl
{
    public PersonalInformationView()
    {
        InitializeComponent();
    }

    private void PageInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not PersonalInformationViewModel viewModel ||
            !viewModel.GoToPageCommand.CanExecute(viewModel.PageInput)) return;

        viewModel.GoToPageCommand.Execute(viewModel.PageInput);
        e.Handled = true;
    }
}
