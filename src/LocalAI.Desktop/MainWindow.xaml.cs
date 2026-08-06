using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalAI.Desktop.Services;
using LocalAI.Desktop.ViewModels;
using LocalAI.Infrastructure.Ollama;
using LocalAI.Infrastructure.Repositories;

namespace LocalAI.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(
            new OllamaClient(),
            new FolderPickerService(),
            new RepositoryInspector(),
            new RepositoryFileContextService());

        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        MessageInputTextBox.Focus();
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _viewModel.Dispose();
    }

    private void MessageInputTextBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        if (_viewModel.SendCommand.CanExecute(null))
        {
            _viewModel.SendCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void ConversationItems_LayoutUpdated(
        object? sender,
        EventArgs e)
    {
        ConversationScrollViewer.ScrollToEnd();
    }

    private void RepositoryTree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectedRepositoryItem =
            e.NewValue as RepositoryTreeItemViewModel;
    }
}
