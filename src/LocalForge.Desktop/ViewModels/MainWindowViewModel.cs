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
using LocalForge.Core.Repositories;
using LocalForge.Desktop.Commands;

namespace LocalForge.Desktop.ViewModels;

public sealed class MainWindowViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly IOllamaClient _ollamaClient;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IRepositoryInspector _repositoryInspector;
    private readonly IRepositoryFileContextService _repositoryFileContextService;
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
    private RepositoryTreeItemViewModel? _selectedRepositoryItem;
    private RepositoryContextFileViewModel? _selectedContextFile;
    private bool _isRepositoryPanelOpen;
    private bool _isBusy;
    private CancellationTokenSource? _requestCancellation;

    public MainWindowViewModel(
        IOllamaClient ollamaClient,
        IFolderPickerService folderPickerService,
        IRepositoryInspector repositoryInspector,
        IRepositoryFileContextService repositoryFileContextService)
    {
        _ollamaClient = ollamaClient ??
            throw new ArgumentNullException(nameof(ollamaClient));

        _folderPickerService = folderPickerService ??
            throw new ArgumentNullException(
                nameof(folderPickerService));

        _repositoryInspector = repositoryInspector ??
            throw new ArgumentNullException(
                nameof(repositoryInspector));

        _repositoryFileContextService =
            repositoryFileContextService ??
            throw new ArgumentNullException(
                nameof(repositoryFileContextService));

        RefreshModelsCommand = new AsyncRelayCommand(
            RefreshModelsAsync,
            () => !IsBusy);

        BrowseRepositoryCommand = new AsyncRelayCommand(
            SelectRepositoryAsync,
            () => !IsBusy);

        RefreshRepositoryCommand = new AsyncRelayCommand(
            RefreshRepositoryAsync,
            CanRefreshRepository);

        ToggleRepositoryPanelCommand = new RelayCommand(
            ToggleRepositoryPanel);

        AddSelectedFileToContextCommand = new AsyncRelayCommand(
            AddSelectedFileToContextAsync,
            CanAddSelectedFileToContext);

        RemoveSelectedContextFileCommand = new RelayCommand(
            RemoveSelectedContextFile,
            () => SelectedContextFile is not null && !IsBusy);

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

    public ObservableCollection<RepositoryTreeItemViewModel> RepositoryTree
    {
        get;
    } = [];

    public ObservableCollection<RepositoryContextFileViewModel> ContextFiles
    {
        get;
    } = [];

    public AsyncRelayCommand RefreshModelsCommand { get; }

    public AsyncRelayCommand BrowseRepositoryCommand { get; }

    public AsyncRelayCommand RefreshRepositoryCommand { get; }

    public RelayCommand ToggleRepositoryPanelCommand { get; }

    public AsyncRelayCommand AddSelectedFileToContextCommand { get; }

    public RelayCommand RemoveSelectedContextFileCommand { get; }

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
        private set
        {
            if (SetField(ref _repositoryPath, value))
            {
                RefreshRepositoryCommand
                    .NotifyCanExecuteChanged();
            }
        }
    }

    public string RepositorySummary
    {
        get => _repositorySummary;
        private set => SetField(ref _repositorySummary, value);
    }

    public RepositoryTreeItemViewModel? SelectedRepositoryItem
    {
        get => _selectedRepositoryItem;
        set
        {
            if (SetField(ref _selectedRepositoryItem, value))
            {
                AddSelectedFileToContextCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RepositoryContextFileViewModel? SelectedContextFile
    {
        get => _selectedContextFile;
        set
        {
            if (SetField(ref _selectedContextFile, value))
            {
                RemoveSelectedContextFileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public long ContextSizeBytes =>
        ContextFiles.Sum(file => file.File.SizeBytes);

    public int EstimatedContextTokens =>
        ContextFiles.Sum(file => file.File.EstimatedTokens);

    public string ContextSizeText =>
        $"{ContextFiles.Count} file(s) • " +
        $"{ContextSizeBytes / 1024d:0.#} / " +
        $"{_repositoryFileContextService.MaximumTotalBytes / 1024} KB • " +
        $"~{EstimatedContextTokens:N0} tokens";

    public bool IsRepositoryPanelOpen
    {
        get => _isRepositoryPanelOpen;
        set => SetField(ref _isRepositoryPanelOpen, value);
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
            RefreshRepositoryCommand.NotifyCanExecuteChanged();
            AddSelectedFileToContextCommand.NotifyCanExecuteChanged();
            RemoveSelectedContextFileCommand.NotifyCanExecuteChanged();
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

                Select a repository from the sidebar or begin a local conversation.
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

    private void ToggleRepositoryPanel()
    {
        IsRepositoryPanelOpen = !IsRepositoryPanelOpen;
    }

    private bool CanRefreshRepository()
    {
        return !IsBusy &&
               Directory.Exists(RepositoryPath);
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

        await LoadRepositoryAsync(selectedFolder);
    }

    private async Task RefreshRepositoryAsync()
    {
        if (!Directory.Exists(RepositoryPath))
        {
            return;
        }

        await LoadRepositoryAsync(RepositoryPath);
    }

    private async Task LoadRepositoryAsync(string repositoryPath)
    {
        IsBusy = true;
        StatusText = "Inspecting repository...";

        try
        {
            RepositoryInfo repository =
                await _repositoryInspector.InspectAsync(
                    repositoryPath);

            RepositoryPath = repository.RootPath;

            RepositoryName =
                new DirectoryInfo(repository.RootPath).Name;

            RepositorySummary =
                $"{(repository.IsGitRepository ? "Git repository" : "Not a Git repository")} • " +
                $"{repository.SolutionFiles.Count} solution file(s) • " +
                $"{repository.ProjectFiles.Count} project file(s)";

            RepositoryTree.Clear();
            ClearContextFiles();

            foreach (RepositoryTreeNode rootEntry in
                     repository.RootEntries)
            {
                RepositoryTree.Add(
                    new RepositoryTreeItemViewModel(
                        rootEntry));
            }

            SelectedRepositoryItem =
                RepositoryTree.FirstOrDefault();

            IsRepositoryPanelOpen = true;

            StatusText =
                $"Repository selected: {RepositoryName}";
        }
        catch (Exception exception)
        {
            RepositoryName = "Repository unavailable";
            RepositoryPath = repositoryPath;
            RepositorySummary = exception.Message;

            RepositoryTree.Clear();
            SelectedRepositoryItem = null;
            ClearContextFiles();

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

            StatusText =
                $"Ollama connection failed: {exception.Message}";
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

    private bool CanAddSelectedFileToContext()
    {
        return !IsBusy &&
               SelectedRepositoryItem is { IsDirectory: false } selected &&
               Directory.Exists(RepositoryPath) &&
               ContextFiles.All(file =>
                   !file.RelativePath.Equals(
                       selected.RelativePath,
                       StringComparison.OrdinalIgnoreCase));
    }

    private async Task AddSelectedFileToContextAsync()
    {
        if (!CanAddSelectedFileToContext())
        {
            return;
        }

        RepositoryContextReadResult result =
            await _repositoryFileContextService.ReadAsync(
                RepositoryPath,
                SelectedRepositoryItem!.RelativePath,
                ContextSizeBytes);

        if (!result.IsSuccess)
        {
            StatusText = result.Error ?? "The file could not be added.";
            return;
        }

        ContextFiles.Add(
            new RepositoryContextFileViewModel(result.File!));

        SelectedContextFile = ContextFiles[^1];
        NotifyContextChanged();
        StatusText = RepositoryContextPromptBuilder
            .IsLikelyToSlowGeneration(
                ContextFiles.Select(file => file.File))
            ? $"Added to context: {result.File!.RelativePath}. " +
              $"Approximately {EstimatedContextTokens:N0} context tokens " +
              "may slow CPU generation."
            : $"Added to context: {result.File!.RelativePath}";
    }

    private void RemoveSelectedContextFile()
    {
        if (SelectedContextFile is null)
        {
            return;
        }

        string relativePath = SelectedContextFile.RelativePath;
        ContextFiles.Remove(SelectedContextFile);
        SelectedContextFile = ContextFiles.LastOrDefault();
        NotifyContextChanged();
        StatusText = $"Removed from context: {relativePath}";
    }

    private void ClearContextFiles()
    {
        ContextFiles.Clear();
        SelectedContextFile = null;
        NotifyContextChanged();
    }

    private void NotifyContextChanged()
    {
        OnPropertyChanged(nameof(ContextSizeBytes));
        OnPropertyChanged(nameof(EstimatedContextTokens));
        OnPropertyChanged(nameof(ContextSizeText));
        AddSelectedFileToContextCommand.NotifyCanExecuteChanged();
        RemoveSelectedContextFileCommand.NotifyCanExecuteChanged();
    }

    private async Task SendAsync()
    {
        if (!CanSend())
        {
            return;
        }

        string model = SelectedModel!;
        string prompt = MessageInput.Trim();
        string modelPrompt;

        try
        {
            modelPrompt = RepositoryContextPromptBuilder.Build(
                prompt,
                ContextFiles.Select(file => file.File));
        }
        catch (InvalidOperationException exception)
        {
            StatusText = exception.Message;
            return;
        }

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
        _requestCancellation =
            new CancellationTokenSource();

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
                    modelPrompt,
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
            if (string.IsNullOrWhiteSpace(
                assistantMessage.Content))
            {
                assistantMessage.Content =
                    "Request cancelled.";
            }

            StatusText = "Request cancelled.";
        }
        catch (HttpRequestException exception)
        {
            assistantMessage.Content =
                $"Ollama connection error:{Environment.NewLine}" +
                exception.Message;

            StatusText =
                "Connection to Ollama was lost.";
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
        if (EqualityComparer<T>.Default.Equals(
            field,
            value))
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
