using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Threading;
using LocalForge.Core.Interfaces;
using LocalForge.Desktop.Commands;

namespace LocalForge.Desktop.ViewModels;

public sealed class MainWindowViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly IOllamaClient _ollamaClient;
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _elapsedTimer;

    private string? _selectedModel;
    private string _prompt = string.Empty;
    private string _response = string.Empty;
    private string _statusText = "Starting...";
    private string _elapsedText = "00:00.0";
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

        ClearResponseCommand = new RelayCommand(
            ClearResponse,
            () => !IsBusy &&
                  !string.IsNullOrEmpty(Response));

        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        _elapsedTimer.Tick += OnElapsedTimerTick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Models { get; } = [];

    public AsyncRelayCommand RefreshModelsCommand { get; }

    public AsyncRelayCommand SendCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ClearResponseCommand { get; }

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
        private set
        {
            if (SetField(ref _response, value))
            {
                ClearResponseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetField(ref _elapsedText, value);
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

            OnPropertyChanged(nameof(IsNotBusy));

            RefreshModelsCommand.NotifyCanExecuteChanged();
            SendCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            ClearResponseCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsNotBusy => !IsBusy;

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

        string model = SelectedModel!;
        string prompt = Prompt.Trim();

        IsBusy = true;
        Response = string.Empty;
        ElapsedText = "00:00.0";
        StatusText = $"Generating with {model}...";

        _stopwatch.Restart();
        _elapsedTimer.Start();

        StringBuilder responseBuilder = new();

        try
        {
            await foreach (string chunk in
                _ollamaClient.StreamGenerateAsync(
                    model,
                    prompt,
                    _requestCancellation.Token))
            {
                responseBuilder.Append(chunk);
                Response = responseBuilder.ToString();
            }

            StatusText = "Response completed.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Request cancelled.";
        }
        catch (HttpRequestException exception)
        {
            StatusText = "Connection to Ollama was lost.";
            Response =
                $"Ollama connection error:{Environment.NewLine}" +
                exception.Message;
        }
        catch (Exception exception)
        {
            StatusText = "Generation failed.";
            Response =
                $"Error:{Environment.NewLine}{exception.Message}";
        }
        finally
        {
            _stopwatch.Stop();
            _elapsedTimer.Stop();
            UpdateElapsedText();

            IsBusy = false;

            _requestCancellation.Dispose();
            _requestCancellation = null;
        }
    }

    private void Cancel()
    {
        if (_requestCancellation is null)
        {
            return;
        }

        StatusText = "Cancelling request...";
        _requestCancellation.Cancel();
    }

    private void ClearResponse()
    {
        Response = string.Empty;
        ElapsedText = "00:00.0";
        StatusText = "Response cleared.";
    }

    private void OnElapsedTimerTick(
        object? sender,
        EventArgs e)
    {
        UpdateElapsedText();
    }

    private void UpdateElapsedText()
    {
        ElapsedText =
            _stopwatch.Elapsed.ToString(@"mm\:ss\.f");
    }

    public void Dispose()
    {
        _elapsedTimer.Stop();
        _elapsedTimer.Tick -= OnElapsedTimerTick;

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
        OnPropertyChanged(propertyName);

        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

