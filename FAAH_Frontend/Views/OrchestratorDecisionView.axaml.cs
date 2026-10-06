using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using FAAH_Frontend.ViewModels;

namespace FAAH_Frontend.Views;

public partial class OrchestratorDecisionView : UserControl
{
    public OrchestratorDecisionView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void PageInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not OrchestratorDecisionHistoryViewModel viewModel ||
            !viewModel.GoToPageCommand.CanExecute(viewModel.PageInput)) return;

        viewModel.GoToPageCommand.Execute(viewModel.PageInput);
        e.Handled = true;
    }
}
