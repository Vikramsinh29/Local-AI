using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Threading;
using LocalForge.Core.Interfaces;
using LocalForge.Core.Models;
using LocalForge.Desktop.Commands;

namespace LocalForge.Desktop.ViewModels;

public sealed class MainWindowViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly IOllamaClient _ollamaClient;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IRepositoryInspector _repositoryInspector;
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _elapsedTimer;

    private string? _selectedModel;
    private string _messageInput = string.Empty;
    private string _statusText = "Starting...";
    private string _elapsedText = string.Empty;
    private string _repositoryName = "No repository";
    private string _repositoryPath = string.Empty;
    private string _repositorySummary =
        "Select a repository to add project context.";
    private bool _isBusy;
    private CancellationTokenSource? _requestCancellation;

    public MainWindowViewModel(
        IOllamaClient ollamaClient,
        IFolderPickerService folderPickerService,
        IRepositoryInspector repositoryInspector)
    {
        _ollamaClient = ollamaClient ??
            throw new ArgumentNullException(nameof(ollamaClient));

        _folderPickerService = folderPickerService ??
            throw new ArgumentNullException(nameof(folderPickerService));

        _repositoryInspector = repositoryInspector ??
            throw new ArgumentNullException(nameof(repositoryInspector));

        RefreshModelsCommand = new AsyncRelayCommand(
            RefreshModelsAsync,
            () => !IsBusy);

        BrowseRepositoryCommand = new AsyncRelayCommand(
            SelectRepositoryAsync,
            () => !IsBusy);

        SendCommand = new AsyncRelayCommand(
            SendAsync,
            CanSend);

        CancelCommand = new RelayCommand(
            Cancel,
            () => IsBusy);

        NewChatCommand = new RelayCommand(
            NewChat,
            () => !IsBusy);

        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        _elapsedTimer.Tick += OnElapsedTimerTick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Models { get; } = [];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<string> RepositoryItems { get; } = [];

    public AsyncRelayCommand RefreshModelsCommand { get; }

    public AsyncRelayCommand BrowseRepositoryCommand { get; }

    public AsyncRelayCommand SendCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand NewChatCommand { get; }

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

    public string MessageInput
    {
        get => _messageInput;
        set
        {
            if (SetField(ref _messageInput, value))
            {
                SendCommand.NotifyCanExecuteChanged();
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

    public string RepositoryName
    {
        get => _repositoryName;
        private set => SetField(ref _repositoryName, value);
    }

    public string RepositoryPath
    {
        get => _repositoryPath;
        private set => SetField(ref _repositoryPath, value);
    }

    public string RepositorySummary
    {
        get => _repositorySummary;
        private set => SetField(ref _repositorySummary, value);
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
            BrowseRepositoryCommand.NotifyCanExecuteChanged();
            SendCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            NewChatCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsNotBusy => !IsBusy;

    public async Task InitializeAsync()
    {
        AddWelcomeMessage();
        await RefreshModelsAsync();
    }

    private void AddWelcomeMessage()
    {
        if (Messages.Count > 0)
        {
            return;
        }

        Messages.Add(
            new ChatMessageViewModel(
                isUser: false,
                """
                Welcome to LocalForge AI.

                Select a repository from the sidebar or start a local conversation.
                Your prompts and source code remain on this computer.
                """));
    }

    private void NewChat()
    {
        Messages.Clear();
        MessageInput = string.Empty;
        ElapsedText = string.Empty;
        StatusText = "New conversation started.";
        AddWelcomeMessage();
    }

    private async Task SelectRepositoryAsync()
    {
        string? initialDirectory =
            Directory.Exists(RepositoryPath)
                ? RepositoryPath
                : null;

        string? selectedFolder =
            _folderPickerService.PickFolder(initialDirectory);

        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            return;
        }

        IsBusy = true;
        StatusText = "Inspecting repository...";

        try
        {
            RepositoryInfo repository =
                await _repositoryInspector.InspectAsync(selectedFolder);

            RepositoryPath = repository.RootPath;
            RepositoryName =
                Path.GetFileName(repository.RootPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));

            RepositoryItems.Clear();

            foreach (string solution in repository.SolutionFiles)
            {
                RepositoryItems.Add($"Solution: {solution}");
            }

            foreach (string project in repository.ProjectFiles)
            {
                RepositoryItems.Add($"Project: {project}");
            }

            string gitText = repository.IsGitRepository
                ? "Git repository"
                : "Not a Git repository";

            RepositorySummary =
                $"{gitText} • " +
                $"{repository.SolutionFiles.Count} solution file(s) • " +
                $"{repository.ProjectFiles.Count} project file(s)";

            StatusText = $"Repository selected: {RepositoryName}";
        }
        catch (Exception exception)
        {
            RepositoryName = "Repository unavailable";
            RepositoryPath = selectedFolder;
            RepositorySummary = exception.Message;
            RepositoryItems.Clear();
            StatusText = "Repository inspection failed.";
        }
        finally
        {
            IsBusy = false;
        }
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
                    "Ollama is unavailable at 127.0.0.1:11434.";
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
                ? "Connected to Ollama, but no models were found."
                : $"Local AI connected • {Models.Count} model(s)";
        }
        catch (Exception exception)
        {
            Models.Clear();
            SelectedModel = null;
            StatusText = $"Ollama connection failed: {exception.Message}";
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
               !string.IsNullOrWhiteSpace(MessageInput);
    }

    private async Task SendAsync()
    {
        if (!CanSend())
        {
            return;
        }

        string model = SelectedModel!;
        string prompt = MessageInput.Trim();

        MessageInput = string.Empty;

        Messages.Add(
            new ChatMessageViewModel(
                isUser: true,
                prompt));

        ChatMessageViewModel assistantMessage =
            new(
                isUser: false,
                string.Empty);

        Messages.Add(assistantMessage);

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();

        IsBusy = true;
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
                assistantMessage.Content =
                    responseBuilder.ToString();
            }

            StatusText = "Response completed.";
        }
        catch (OperationCanceledException)
        {
            if (string.IsNullOrWhiteSpace(assistantMessage.Content))
            {
                assistantMessage.Content = "Request cancelled.";
            }

            StatusText = "Request cancelled.";
        }
        catch (HttpRequestException exception)
        {
            assistantMessage.Content =
                $"Ollama connection error:{Environment.NewLine}" +
                exception.Message;

            StatusText = "Connection to Ollama was lost.";
        }
        catch (Exception exception)
        {
            assistantMessage.Content =
                $"Generation error:{Environment.NewLine}" +
                exception.Message;

            StatusText = "Generation failed.";
        }
        finally
        {
            _stopwatch.Stop();
            _elapsedTimer.Stop();
            UpdateElapsedText();

            IsBusy = false;

            _requestCancellation?.Dispose();
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
