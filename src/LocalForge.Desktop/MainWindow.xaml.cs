using System.Windows;
using LocalForge.Desktop.ViewModels;
using LocalForge.Infrastructure.Ollama;

namespace LocalForge.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(
            new OllamaClient());

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
}
