using System.Windows;
using System.Windows.Controls;
using LocalForge.Desktop.Services;
using LocalForge.Desktop.ViewModels;
using LocalForge.Infrastructure.Ollama;
using LocalForge.Infrastructure.Repositories;

namespace LocalForge.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(
            new OllamaClient(),
            new FolderPickerService(),
            new RepositoryInspector());

        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _viewModel.Dispose();
    }

    private void ResponseTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        ResponseTextBox.CaretIndex =
            ResponseTextBox.Text.Length;

        ResponseTextBox.ScrollToEnd();
    }
}
