using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LocalForge.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            MainWindow window = new();

            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            ReportFatalError("Application startup failed.", exception);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFatalError(
            "An unexpected application error occurred.",
            e.Exception);

        e.Handled = true;
    }

    private static void ReportFatalError(
        string message,
        Exception exception)
    {
        string details =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
            $"{message}{Environment.NewLine}" +
            $"{exception}{Environment.NewLine}" +
            $"{new string('-', 80)}{Environment.NewLine}";

        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "LocalForgeAI");

            Directory.CreateDirectory(directory);

            string logPath = Path.Combine(
                directory,
                "startup-error.log");

            File.AppendAllText(logPath, details);
        }
        catch
        {
            // Avoid hiding the original startup exception.
        }

        MessageBox.Show(
            $"{message}{Environment.NewLine}{Environment.NewLine}" +
            exception.Message,
            "LocalForge AI",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
