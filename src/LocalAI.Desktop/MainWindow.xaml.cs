using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalAI.Desktop.Services;
using LocalAI.Desktop.ViewModels;
using LocalAI.Infrastructure.Ollama;
using LocalAI.Infrastructure.Repositories;
using LocalAI.Infrastructure.Verification;

namespace LocalAI.Desktop;

public partial class MainWindow : Window
{
    private const double ConversationBottomTolerance = 2d;

    private readonly MainWindowViewModel _viewModel;
    private bool _conversationAutoScroll = true;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(
            new OllamaClient(),
            new FolderPickerService(),
            new RepositoryInspector(),
            new RepositoryFileContextService(),
            new VerificationToolRunner());

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

    private void ConversationScrollViewer_ScrollChanged(
        object sender,
        ScrollChangedEventArgs e)
    {
        if (ConversationScrollViewer.ScrollableHeight <=
            ConversationBottomTolerance)
        {
            _conversationAutoScroll = true;
            return;
        }

        if (e.ExtentHeightChange == 0)
        {
            double distanceFromBottom =
                ConversationScrollViewer.ScrollableHeight -
                ConversationScrollViewer.VerticalOffset;

            _conversationAutoScroll =
                distanceFromBottom <= ConversationBottomTolerance;

            return;
        }

        if (_conversationAutoScroll)
        {
            ConversationScrollViewer.ScrollToEnd();
        }
    }

    private void RepositoryTree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectedRepositoryItem =
            e.NewValue as RepositoryTreeItemViewModel;
    }
}
