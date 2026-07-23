using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalForge.Core.Interfaces;
using LocalForge.Desktop.Commands;

namespace LocalForge.Desktop.ViewModels;

public sealed class MainWindowViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly IOllamaClient _ollamaClient;

    private string? _selectedModel;
    private string _prompt = string.Empty;
    private string _response = string.Empty;
    private string _statusText = "Starting...";
    private bool _isBusy;
    private CancellationTokenSource? _requestCancellation;

    public MainWindowViewModel(IOllamaClient ollamaClient)
    {
        _ollamaClient = ollamaClient ??
            throw new ArgumentNullException(nameof(ollamaClient));

        RefreshModelsCommand = new AsyncRelayCommand(
            RefreshModelsAsync,
            () => !IsBusy);

        SendCommand = new AsyncRelayCommand(
            SendAsync,
            CanSend);

        CancelCommand = new RelayCommand(
            Cancel,
            () => IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Models { get; } = [];

    public AsyncRelayCommand RefreshModelsCommand { get; }

    public AsyncRelayCommand SendCommand { get; }

    public RelayCommand CancelCommand { get; }

    public string? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetField(ref _selectedModel, value))
            {
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Prompt
    {
        get => _prompt;
        set
        {
            if (SetField(ref _prompt, value))
            {
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Response
    {
        get => _response;
        private set => SetField(ref _response, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            RefreshModelsCommand.NotifyCanExecuteChanged();
            SendCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    public Task InitializeAsync()
    {
        return RefreshModelsAsync();
    }

    private async Task RefreshModelsAsync()
    {
        IsBusy = true;
        StatusText = "Connecting to Ollama...";

        try
        {
            bool available =
                await _ollamaClient.IsAvailableAsync();

            if (!available)
            {
                Models.Clear();
                SelectedModel = null;
                StatusText =
                    "Ollama is not available at 127.0.0.1:11434.";
                return;
            }

            IReadOnlyList<string> models =
                await _ollamaClient.GetModelsAsync();

            string? previousSelection = SelectedModel;

            Models.Clear();

            foreach (string model in models)
            {
                Models.Add(model);
            }

            SelectedModel =
                previousSelection is not null &&
                Models.Contains(previousSelection)
                    ? previousSelection
                    : Models.FirstOrDefault();

            StatusText = Models.Count == 0
                ? "Connected, but no local models were found."
                : $"Connected. {Models.Count} model(s) available.";
        }
        catch (Exception exception)
        {
            Models.Clear();
            SelectedModel = null;
            StatusText = $"Connection failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSend()
    {
        return !IsBusy &&
               !string.IsNullOrWhiteSpace(SelectedModel) &&
               !string.IsNullOrWhiteSpace(Prompt);
    }

    private async Task SendAsync()
    {
        if (!CanSend())
        {
            return;
        }

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();

        IsBusy = true;
        Response = string.Empty;
        StatusText = $"Generating with {SelectedModel}...";

        try
        {
            Response = await _ollamaClient.GenerateAsync(
                SelectedModel!,
                Prompt.Trim(),
                _requestCancellation.Token);

            StatusText = "Response completed.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Request cancelled.";
        }
        catch (Exception exception)
        {
            Response = $"Error: {exception.Message}";
            StatusText = "Generation failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        _requestCancellation?.Cancel();
    }

    public void Dispose()
    {
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();

        if (_ollamaClient is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));

        return true;
    }
}
