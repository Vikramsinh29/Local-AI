using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Threading;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using LocalAI.Core.Repositories;
using LocalAI.Desktop.Commands;

namespace LocalAI.Desktop.ViewModels;

public sealed class MainWindowViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private const int MaximumDisplayedVerificationCharacters = 50_000;
    private const int MaximumVerificationAuditEntries = 20;

    private readonly IOllamaClient _ollamaClient;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IRepositoryInspector _repositoryInspector;
    private readonly IRepositoryFileContextService _repositoryFileContextService;
    private readonly IRepositoryPatchService _repositoryPatchService;
    private readonly IVerificationToolRunner _verificationToolRunner;
    private readonly IProjectInstructionService _projectInstructionService;
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _elapsedTimer;
    private readonly List<VerificationRunResult> _verificationRuns = [];

    private string? _selectedModel;
    private GenerationProfile _selectedGenerationProfile =
        GenerationProfiles.Balanced;
    private string _messageInput = string.Empty;
    private string _statusText = "Starting...";
    private string _elapsedText = string.Empty;
    private string _repositoryName = "No repository";
    private string _repositoryPath = string.Empty;
    private string _repositorySummary =
        "Select a repository to add project context.";
    private RepositoryTreeItemViewModel? _selectedRepositoryItem;
    private RepositoryContextFileViewModel? _selectedContextFile;
    private VerificationToolDescriptor _selectedVerificationTool =
        VerificationTools.All[0];
    private VerificationAuditEntryViewModel?
        _selectedVerificationAuditEntry;
    private ProposedPatchPreview? _proposedPatchPreview;
    private PatchRollbackRecord? _patchRollbackRecord;
    private ProjectInstructionManifest _projectInstructionManifest =
        ProjectInstructionManifest.Empty;
    private ProjectInstructionSelection _projectInstructionSelection =
        ProjectInstructionSelectionBuilder.Build(
            ProjectInstructionManifest.Empty);
    private ProjectInstructionItemViewModel? _selectedInstructionSkill;
    private string _instructionManifestSummary =
        "Select a repository to discover project instructions.";
    private string _instructionDiscoveryIssuesText = string.Empty;
    private string? _repositorySolutionFile;
    private string _verificationOutput =
        "No verification command has been run in this session.";
    private string _verificationStatusText =
        "Select a repository and approve one fixed verification command.";
    private bool _repositoryIsGit;
    private bool _isVerificationApproved;
    private bool _isPatchApplyApproved;
    private bool _isPatchRollbackApproved;
    private bool _isPatchPreviewRequested;
    private bool _isRepositoryPanelOpen;
    private bool _isAgentMode;
    private bool _isBusy;
    private CancellationTokenSource? _requestCancellation;

    public MainWindowViewModel(
        IOllamaClient ollamaClient,
        IFolderPickerService folderPickerService,
        IRepositoryInspector repositoryInspector,
        IRepositoryFileContextService repositoryFileContextService,
        IRepositoryPatchService repositoryPatchService,
        IVerificationToolRunner verificationToolRunner,
        IProjectInstructionService projectInstructionService)
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

        _repositoryPatchService = repositoryPatchService ??
            throw new ArgumentNullException(
                nameof(repositoryPatchService));

        _verificationToolRunner = verificationToolRunner ??
            throw new ArgumentNullException(
                nameof(verificationToolRunner));

        _projectInstructionService = projectInstructionService ??
            throw new ArgumentNullException(
                nameof(projectInstructionService));

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

        RunVerificationCommand = new AsyncRelayCommand(
            RunVerificationAsync,
            CanRunVerification);

        DismissPatchPreviewCommand = new RelayCommand(
            DismissPatchPreview,
            () => HasProposedPatchPreview && !IsBusy);

        ApplyProposedPatchCommand = new AsyncRelayCommand(
            ApplyProposedPatchAsync,
            CanApplyProposedPatch);

        RollbackAppliedPatchCommand = new AsyncRelayCommand(
            RollbackAppliedPatchAsync,
            CanRollbackAppliedPatch);

        ClearSelectedInstructionSkillCommand = new RelayCommand(
            ClearSelectedInstructionSkill,
            () => SelectedInstructionSkill is not null && !IsBusy);

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

    public IReadOnlyList<GenerationProfile> AvailableGenerationProfiles
        { get; } = GenerationProfiles.All;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<RepositoryTreeItemViewModel> RepositoryTree
    {
        get;
    } = [];

    public ObservableCollection<RepositoryContextFileViewModel> ContextFiles
    {
        get;
    } = [];

    public ObservableCollection<VerificationAuditEntryViewModel>
        VerificationAuditEntries { get; } = [];

    public ObservableCollection<ProjectInstructionItemViewModel>
        ProjectInstructions { get; } = [];

    public ObservableCollection<ProjectInstructionItemViewModel>
        AvailableInstructionSkills { get; } = [];

    public IReadOnlyList<VerificationToolDescriptor>
        AvailableVerificationTools { get; } = VerificationTools.All;

    public AsyncRelayCommand RefreshModelsCommand { get; }

    public AsyncRelayCommand BrowseRepositoryCommand { get; }

    public AsyncRelayCommand RefreshRepositoryCommand { get; }

    public RelayCommand ToggleRepositoryPanelCommand { get; }

    public AsyncRelayCommand AddSelectedFileToContextCommand { get; }

    public RelayCommand RemoveSelectedContextFileCommand { get; }

    public AsyncRelayCommand SendCommand { get; }

    public AsyncRelayCommand RunVerificationCommand { get; }

    public RelayCommand DismissPatchPreviewCommand { get; }

    public AsyncRelayCommand ApplyProposedPatchCommand { get; }

    public AsyncRelayCommand RollbackAppliedPatchCommand { get; }

    public RelayCommand ClearSelectedInstructionSkillCommand { get; }

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

    public GenerationProfile SelectedGenerationProfile
    {
        get => _selectedGenerationProfile;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (SetField(ref _selectedGenerationProfile, value))
            {
                OnPropertyChanged(nameof(ContextSizeText));
                StatusText = value.Description;
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
                SendCommand.NotifyCanExecuteChanged();
                RunVerificationCommand.NotifyCanExecuteChanged();
                ApplyProposedPatchCommand.NotifyCanExecuteChanged();
                RollbackAppliedPatchCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(AgentEvidenceText));
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
        $"~{EstimatedContextTokens:N0} tokens • " +
        $"{SelectedGenerationProfile.Name}";

    public bool IsAgentMode
    {
        get => _isAgentMode;
        set
        {
            if (!SetField(ref _isAgentMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(AgentEvidenceText));
            OnPropertyChanged(nameof(HasPatchRollback));
            SendCommand.NotifyCanExecuteChanged();
            RollbackAppliedPatchCommand.NotifyCanExecuteChanged();
            IsVerificationApproved = false;
            IsPatchRollbackApproved = false;

            if (!value)
            {
                _isPatchPreviewRequested = false;
                OnPropertyChanged(nameof(IsPatchPreviewRequested));
                ClearProposedPatchPreview();
            }

            UpdateVerificationReadiness();
            StatusText = value
                ? "Agent mode is read-only by default and protects source " +
                  "files. Verification and applying require separate " +
                  "one-run approval."
                : "Local conversation mode enabled.";
        }
    }

    public bool IsPatchPreviewRequested
    {
        get => _isPatchPreviewRequested;
        set
        {
            if (!SetField(ref _isPatchPreviewRequested, value))
            {
                return;
            }

            ClearProposedPatchPreview();
            SendCommand.NotifyCanExecuteChanged();
            StatusText = value
                ? "Patch preview mode enabled. Generation does not apply " +
                  "changes; a separate approval is required later."
                : "Agent planning mode enabled.";
        }
    }

    public ProposedPatchPreview? ProposedPatchPreview
    {
        get => _proposedPatchPreview;
        private set
        {
            if (!SetField(ref _proposedPatchPreview, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasProposedPatchPreview));
            OnPropertyChanged(nameof(PatchPreviewSummaryText));

            if (value is not null)
            {
                ClearPatchRollbackRecord();
            }

            IsPatchApplyApproved = false;
            DismissPatchPreviewCommand.NotifyCanExecuteChanged();
            ApplyProposedPatchCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasProposedPatchPreview =>
        ProposedPatchPreview is not null;

    public string PatchPreviewSummaryText =>
        ProposedPatchPreview is null
            ? "No proposed patch preview."
            : $"{ProposedPatchPreview.Files.Count} file(s) • " +
              $"+{ProposedPatchPreview.AddedLineCount} / " +
              $"-{ProposedPatchPreview.RemovedLineCount}";

    public bool IsPatchApplyApproved
    {
        get => _isPatchApplyApproved;
        set
        {
            if (!SetField(ref _isPatchApplyApproved, value))
            {
                return;
            }

            ApplyProposedPatchCommand.NotifyCanExecuteChanged();

            if (value)
            {
                StatusText =
                    "One apply and its disclosed verification sequence are " +
                    "approved for this exact preview. Local-AI will require " +
                    "clean Git and revalidate the source before writing.";
            }
        }
    }

    public bool HasPatchRollback =>
        _patchRollbackRecord is not null && IsAgentMode;

    public string PatchRollbackSummaryText =>
        _patchRollbackRecord is null
            ? "No current-session rollback is available."
            : $"Restore {_patchRollbackRecord.RelativePath} to its exact " +
              $"pre-apply bytes • " +
              $"{_patchRollbackRecord.OriginalSha256[..12]} -> " +
              $"{_patchRollbackRecord.AppliedSha256[..12]}";

    public bool IsPatchRollbackApproved
    {
        get => _isPatchRollbackApproved;
        set
        {
            if (!SetField(ref _isPatchRollbackApproved, value))
            {
                return;
            }

            RollbackAppliedPatchCommand.NotifyCanExecuteChanged();

            if (value)
            {
                StatusText =
                    "One rollback is approved for the exact latest applied " +
                    "file. Local-AI will revalidate the repository and " +
                    "applied bytes before restoring anything.";
            }
        }
    }

    public VerificationToolDescriptor SelectedVerificationTool
    {
        get => _selectedVerificationTool;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!SetField(ref _selectedVerificationTool, value))
            {
                return;
            }

            IsVerificationApproved = false;
            UpdateVerificationReadiness();
        }
    }

    public VerificationAuditEntryViewModel?
        SelectedVerificationAuditEntry
    {
        get => _selectedVerificationAuditEntry;
        set
        {
            if (!SetField(
                    ref _selectedVerificationAuditEntry,
                    value) ||
                value is null)
            {
                return;
            }

            VerificationOutput = value.Output;
            VerificationStatusText =
                $"{value.Outcome}: {value.Command}";
        }
    }

    public bool IsVerificationApproved
    {
        get => _isVerificationApproved;
        set
        {
            if (SetField(ref _isVerificationApproved, value))
            {
                RunVerificationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string VerificationOutput
    {
        get => _verificationOutput;
        private set => SetField(ref _verificationOutput, value);
    }

    public string VerificationStatusText
    {
        get => _verificationStatusText;
        private set => SetField(ref _verificationStatusText, value);
    }

    public string AgentEvidenceText
    {
        get
        {
            if (!IsAgentMode)
            {
                return string.Empty;
            }

            string files = ContextFiles.Count == 0
                ? "No source files selected."
                : string.Join(
                    ", ",
                    ContextFiles.Select(file => file.RelativePath));

            return $"Repository evidence: {RepositoryName} • " +
                   $"{RepositorySummary}{Environment.NewLine}" +
                   $"Instructions: {InstructionManifestSummary}" +
                   $"{Environment.NewLine}" +
                   $"Source evidence: {files}";
        }
    }

    public ProjectInstructionItemViewModel? SelectedInstructionSkill
    {
        get => _selectedInstructionSkill;
        set
        {
            if (value is not null &&
                (!value.IsEligible ||
                 value.File.Kind != ProjectInstructionKind.Skill))
            {
                throw new ArgumentException(
                    "Only one eligible discovered skill can be selected.",
                    nameof(value));
            }

            if (!SetField(ref _selectedInstructionSkill, value))
            {
                return;
            }

            RefreshInstructionSelection();
            ClearProposedPatchPreview();
            ClearSelectedInstructionSkillCommand.NotifyCanExecuteChanged();
            StatusText = value is null
                ? "Project skill selection cleared."
                : $"Selected project skill: {value.RelativePath}";
        }
    }

    public string InstructionManifestSummary
    {
        get => _instructionManifestSummary;
        private set
        {
            if (SetField(ref _instructionManifestSummary, value))
            {
                OnPropertyChanged(nameof(AgentEvidenceText));
            }
        }
    }

    public string InstructionDiscoveryIssuesText
    {
        get => _instructionDiscoveryIssuesText;
        private set => SetField(
            ref _instructionDiscoveryIssuesText,
            value);
    }

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
            RunVerificationCommand.NotifyCanExecuteChanged();
            DismissPatchPreviewCommand.NotifyCanExecuteChanged();
            ApplyProposedPatchCommand.NotifyCanExecuteChanged();
            RollbackAppliedPatchCommand.NotifyCanExecuteChanged();
            ClearSelectedInstructionSkillCommand.NotifyCanExecuteChanged();
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
                Welcome to Local AI.

                Select a repository from the sidebar or begin a local conversation.
                Your prompts and source code remain on this computer.
                """));
    }

    private void NewChat()
    {
        Messages.Clear();
        MessageInput = string.Empty;
        ElapsedText = string.Empty;
        ClearVerificationHistory();
        ClearProposedPatchPreview();
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

            _repositoryIsGit = repository.IsGitRepository;
            _repositorySolutionFile =
                repository.SolutionFiles.Count == 1
                    ? repository.SolutionFiles[0]
                    : null;

            RepositoryTree.Clear();
            ClearContextFiles();
            ClearProjectInstructions();
            ClearVerificationHistory();
            ClearProposedPatchPreview();
            ClearPatchRollbackRecord();

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

            await LoadProjectInstructionsAsync(
                repository.RootPath);

            OnPropertyChanged(nameof(AgentEvidenceText));
            UpdateVerificationReadiness();

            StatusText =
                $"Repository selected: {RepositoryName}";
        }
        catch (Exception exception)
        {
            RepositoryName = "Repository unavailable";
            RepositoryPath = repositoryPath;
            RepositorySummary = exception.Message;

            _repositoryIsGit = false;
            _repositorySolutionFile = null;

            RepositoryTree.Clear();
            SelectedRepositoryItem = null;
            ClearContextFiles();
            ClearProjectInstructions();
            ClearVerificationHistory();
            ClearProposedPatchPreview();
            ClearPatchRollbackRecord();
            OnPropertyChanged(nameof(AgentEvidenceText));
            UpdateVerificationReadiness();

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
               !string.IsNullOrWhiteSpace(MessageInput) &&
               (!IsAgentMode || Directory.Exists(RepositoryPath)) &&
               (!IsAgentMode ||
                !IsPatchPreviewRequested ||
                ContextFiles.Count > 0);
    }

    private bool CanRunVerification()
    {
        if (IsBusy ||
            !IsAgentMode ||
            !IsVerificationApproved ||
            !Directory.Exists(RepositoryPath))
        {
            return false;
        }

        VerificationToolDescriptor tool =
            SelectedVerificationTool;

        if (tool.RequiresGitRepository && !_repositoryIsGit)
        {
            return false;
        }

        return !tool.RequiresSolution ||
               !string.IsNullOrWhiteSpace(
                   _repositorySolutionFile);
    }

    private async Task RunVerificationAsync()
    {
        if (!CanRunVerification())
        {
            return;
        }

        VerificationToolDescriptor tool =
            SelectedVerificationTool;

        IsVerificationApproved = false;

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();

        IsBusy = true;
        ElapsedText = "00:00.0";
        VerificationOutput = string.Empty;
        VerificationStatusText = $"Running {tool.Name}...";
        StatusText = VerificationStatusText;

        _stopwatch.Restart();
        _elapsedTimer.Start();

        DateTimeOffset startedAt = DateTimeOffset.Now;
        Progress<VerificationOutputLine> progress =
            new(AppendVerificationOutput);

        try
        {
            VerificationRunResult result =
                await _verificationToolRunner.RunAsync(
                    tool.Kind,
                    RepositoryPath,
                    _repositorySolutionFile,
                    progress,
                    _requestCancellation.Token);

            RecordVerificationResult(tool, result);

            VerificationStatusText = result.WasCancelled
                ? $"{tool.Name} cancelled."
                : result.IsSuccess
                    ? $"{tool.Name} passed."
                    : $"{tool.Name} failed with exit code " +
                      $"{result.ExitCode}.";

            StatusText = VerificationStatusText;
        }
        catch (OperationCanceledException)
        {
            VerificationRunResult cancelled = new(
                tool.Kind,
                tool.Name,
                startedAt,
                DateTimeOffset.Now,
                ExitCode: -1,
                WasCancelled: true,
                Output: VerificationOutput);

            RecordVerificationResult(tool, cancelled);
            VerificationStatusText = $"{tool.Name} cancelled.";
            StatusText = VerificationStatusText;
        }
        catch (Exception exception)
        {
            VerificationRunResult failed = new(
                tool.Kind,
                $"{tool.Name} (not completed)",
                startedAt,
                DateTimeOffset.Now,
                ExitCode: -1,
                WasCancelled: false,
                Output: exception.Message);

            RecordVerificationResult(tool, failed);
            VerificationStatusText =
                $"{tool.Name} could not run: {exception.Message}";
            StatusText = "Verification failed to start.";
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

    private void AppendVerificationOutput(
        VerificationOutputLine line)
    {
        string formatted = line.IsError
            ? $"[stderr] {line.Text}"
            : line.Text;

        if (VerificationOutput.Contains(
                "[Live output truncated by Local-AI]",
                StringComparison.Ordinal))
        {
            return;
        }

        int required = formatted.Length +
                       Environment.NewLine.Length;

        if (VerificationOutput.Length + required >
            MaximumDisplayedVerificationCharacters)
        {
            VerificationOutput +=
                Environment.NewLine +
                "[Live output truncated by Local-AI]";
            return;
        }

        VerificationOutput = string.IsNullOrEmpty(
            VerificationOutput)
                ? formatted
                : VerificationOutput +
                  Environment.NewLine +
                  formatted;
    }

    private void RecordVerificationResult(
        VerificationToolDescriptor tool,
        VerificationRunResult result)
    {
        _verificationRuns.Add(result);

        if (_verificationRuns.Count > MaximumVerificationAuditEntries)
        {
            _verificationRuns.RemoveAt(0);
        }

        VerificationAuditEntryViewModel entry =
            new(tool, result);

        VerificationAuditEntries.Insert(0, entry);

        if (VerificationAuditEntries.Count >
            MaximumVerificationAuditEntries)
        {
            VerificationAuditEntries.RemoveAt(
                VerificationAuditEntries.Count - 1);
        }

        SelectedVerificationAuditEntry = entry;
        VerificationOutput = LimitVerificationOutput(result.Output);
    }

    private static string LimitVerificationOutput(string output)
    {
        if (output.Length <= MaximumDisplayedVerificationCharacters)
        {
            return output;
        }

        return output[..MaximumDisplayedVerificationCharacters] +
               Environment.NewLine +
               "[Displayed output truncated by Local-AI]";
    }

    private void ClearVerificationHistory()
    {
        _verificationRuns.Clear();
        VerificationAuditEntries.Clear();
        _selectedVerificationAuditEntry = null;
        OnPropertyChanged(nameof(SelectedVerificationAuditEntry));
        VerificationOutput =
            "No verification command has been run in this session.";
        IsVerificationApproved = false;
        UpdateVerificationReadiness();
    }

    private void UpdateVerificationReadiness()
    {
        VerificationToolDescriptor tool =
            SelectedVerificationTool;

        if (!Directory.Exists(RepositoryPath))
        {
            VerificationStatusText =
                "Select a repository before running verification.";
        }
        else if (tool.RequiresGitRepository && !_repositoryIsGit)
        {
            VerificationStatusText =
                "This fixed command requires a Git repository root.";
        }
        else if (tool.RequiresSolution &&
                 string.IsNullOrWhiteSpace(
                     _repositorySolutionFile))
        {
            VerificationStatusText =
                "Build and test require exactly one detected solution file.";
        }
        else
        {
            string commandPreview = tool.CommandPreview.Replace(
                "{solution}",
                QuoteForCommandPreview(_repositorySolutionFile),
                StringComparison.Ordinal).Replace(
                    "{artifacts}",
                    QuoteForCommandPreview(
                        Path.Combine(
                            ".local-ai",
                            "verification")),
                    StringComparison.Ordinal);

            VerificationStatusText =
                $"{tool.Description} Will run: {commandPreview}. " +
                "Approve one run to enable it.";
        }

        RunVerificationCommand.NotifyCanExecuteChanged();
    }

    private static string QuoteForCommandPreview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<single detected solution>";
        }

        return value.Contains(' ')
            ? $"\"{value}\""
            : value;
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

    private async Task LoadProjectInstructionsAsync(
        string repositoryRoot)
    {
        try
        {
            _projectInstructionManifest =
                await _projectInstructionService.DiscoverAsync(
                    repositoryRoot);

            ProjectInstructions.Clear();
            AvailableInstructionSkills.Clear();
            _selectedInstructionSkill = null;
            OnPropertyChanged(nameof(SelectedInstructionSkill));

            foreach (ProjectInstructionFile file in
                     _projectInstructionManifest.Files)
            {
                ProjectInstructionItemViewModel item = new(file);
                ProjectInstructions.Add(item);

                if (file.Kind == ProjectInstructionKind.Skill &&
                    file.IsEligible)
                {
                    AvailableInstructionSkills.Add(item);
                }
            }

            InstructionDiscoveryIssuesText = string.Join(
                Environment.NewLine,
                _projectInstructionManifest.DiscoveryIssues);
            RefreshInstructionSelection();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ClearProjectInstructions();
            InstructionManifestSummary =
                $"Instruction discovery unavailable: {exception.Message}";
        }
    }

    private void ClearProjectInstructions()
    {
        _projectInstructionManifest = ProjectInstructionManifest.Empty;
        _projectInstructionSelection =
            ProjectInstructionSelectionBuilder.Build(
                _projectInstructionManifest);
        _selectedInstructionSkill = null;
        ProjectInstructions.Clear();
        AvailableInstructionSkills.Clear();
        InstructionManifestSummary =
            "No project instructions are loaded.";
        InstructionDiscoveryIssuesText = string.Empty;
        OnPropertyChanged(nameof(SelectedInstructionSkill));
        ClearSelectedInstructionSkillCommand.NotifyCanExecuteChanged();
    }

    private void ClearSelectedInstructionSkill()
    {
        SelectedInstructionSkill = null;
    }

    private void RefreshInstructionSelection()
    {
        _projectInstructionSelection =
            ProjectInstructionSelectionBuilder.Build(
                _projectInstructionManifest,
                SelectedInstructionSkill?.RelativePath);

        Dictionary<string, ProjectInstructionSelectionItem> selectionByPath =
            _projectInstructionSelection.Items
                .GroupBy(
                    item => item.File.RelativePath,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        foreach (ProjectInstructionItemViewModel item in
                 ProjectInstructions)
        {
            if (selectionByPath.TryGetValue(
                    item.RelativePath,
                    out ProjectInstructionSelectionItem? selectionItem))
            {
                item.ApplySelection(selectionItem);
            }
        }

        ProjectInstructionSelectionItem? agentRules =
            _projectInstructionSelection.Items.FirstOrDefault(
                item => item.File.Kind ==
                    ProjectInstructionKind.AgentRules);
        string agentState = agentRules is null
            ? "AGENTS.md unavailable"
            : agentRules.IsIncluded
                ? "AGENTS.md included"
                : "AGENTS.md excluded";
        string skillState = SelectedInstructionSkill is null
            ? "no skill selected"
            : _projectInstructionSelection.Items.Any(
                item => item.IsIncluded &&
                    item.File.RelativePath.Equals(
                        SelectedInstructionSkill.RelativePath,
                        StringComparison.OrdinalIgnoreCase))
                ? "1 skill included"
                : "selected skill excluded";

        InstructionManifestSummary =
            $"{agentState} • {skillState} • " +
            $"{_projectInstructionSelection.IncludedBytes:N0} / " +
            $"{ProjectInstructionSelectionBuilder.MaximumInstructionBytes:N0} B • " +
            $"~{_projectInstructionSelection.IncludedTokens:N0} / " +
            $"{ProjectInstructionSelectionBuilder.MaximumInstructionTokens:N0} tokens";
    }

    private void NotifyContextChanged()
    {
        ClearProposedPatchPreview();
        OnPropertyChanged(nameof(ContextSizeBytes));
        OnPropertyChanged(nameof(EstimatedContextTokens));
        OnPropertyChanged(nameof(ContextSizeText));
        OnPropertyChanged(nameof(AgentEvidenceText));
        AddSelectedFileToContextCommand.NotifyCanExecuteChanged();
        RemoveSelectedContextFileCommand.NotifyCanExecuteChanged();
        SendCommand.NotifyCanExecuteChanged();
    }

    private async Task SendAsync()
    {
        if (!CanSend())
        {
            return;
        }

        string model = SelectedModel!;
        string prompt = MessageInput.Trim();
        bool patchPreviewRequested =
            IsAgentMode && IsPatchPreviewRequested;
        bool agentPlanRequested =
            IsAgentMode && !patchPreviewRequested;
        RepositoryContextFile[] promptContextFiles =
            ContextFiles.Select(file => file.File).ToArray();
        ProjectInstructionSelection promptInstructionSelection =
            _projectInstructionSelection;
        string modelPrompt;

        try
        {
            modelPrompt = patchPreviewRequested
                ? AgentPatchPromptBuilder.Build(
                    prompt,
                    RepositoryName,
                    RepositorySummary,
                    promptContextFiles,
                    SelectedGenerationProfile
                        .MaximumRepositoryContextTokens,
                    _verificationRuns,
                    promptInstructionSelection)
                : IsAgentMode
                    ? AgentPlanPromptBuilder.Build(
                        prompt,
                        RepositoryName,
                        RepositorySummary,
                        promptContextFiles,
                        SelectedGenerationProfile
                            .MaximumRepositoryContextTokens,
                        _verificationRuns,
                        promptInstructionSelection)
                : RepositoryContextPromptBuilder.Build(
                    prompt,
                    promptContextFiles,
                    SelectedGenerationProfile
                        .MaximumRepositoryContextTokens);
        }
        catch (InvalidOperationException exception)
        {
            StatusText = exception.Message;
            return;
        }

        MessageInput = string.Empty;

        if (patchPreviewRequested)
        {
            ClearProposedPatchPreview();
        }

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
                    SelectedGenerationProfile,
                    _requestCancellation.Token))
            {
                responseBuilder.Append(chunk);

                assistantMessage.Content =
                    responseBuilder.ToString();
            }

            if (patchPreviewRequested)
            {
                ProcessProposedPatch(
                    responseBuilder.ToString(),
                    assistantMessage);
            }
            else if (agentPlanRequested)
            {
                ProcessAgentPlanResponse(
                    responseBuilder.ToString(),
                    assistantMessage,
                    promptContextFiles,
                    promptInstructionSelection);
            }
            else
            {
                StatusText = "Response completed.";
            }
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
            PreservePartialResponse(
                assistantMessage,
                "Ollama connection error",
                exception.Message);

            StatusText =
                "Connection to Ollama was lost.";
        }
        catch (Exception exception)
        {
            PreservePartialResponse(
                assistantMessage,
                "Generation error",
                exception.Message);

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

    private void ProcessAgentPlanResponse(
        string modelResponse,
        ChatMessageViewModel assistantMessage,
        IReadOnlyList<RepositoryContextFile> sourceFiles,
        ProjectInstructionSelection instructionSelection)
    {
        AgentResponseEvidenceValidationResult validation =
            AgentResponseEvidenceValidator.Validate(
                modelResponse,
                sourceFiles,
                instructionSelection);

        if (validation.IsValid)
        {
            assistantMessage.Content = modelResponse;
            StatusText =
                "Response completed. Evidence citations verified.";
            return;
        }

        StringBuilder rejection = new();
        rejection.AppendLine(
            "Agent response rejected by the evidence gate.");
        rejection.AppendLine(
            "The model did not ground its answer in the exact displayed " +
            "evidence paths.");

        if (validation.MissingRequiredPaths.Count > 0)
        {
            rejection.AppendLine();
            rejection.AppendLine("Missing required path(s):");

            foreach (string path in validation.MissingRequiredPaths)
            {
                rejection.AppendLine($"- {path}");
            }
        }

        if (validation.UnexpectedPaths.Count > 0)
        {
            rejection.AppendLine();
            rejection.AppendLine("Unlisted path(s) cited by the model:");

            foreach (string path in validation.UnexpectedPaths)
            {
                rejection.AppendLine($"- {path}");
            }
        }

        rejection.AppendLine();
        rejection.Append(
            "The ungrounded plan was withheld. No files or repository " +
            "state were changed.");

        assistantMessage.Content = rejection.ToString();
        StatusText =
            "Agent response rejected because its evidence citations " +
            "were incomplete or unlisted.";
    }

    private void ProcessProposedPatch(
        string modelResponse,
        ChatMessageViewModel assistantMessage)
    {
        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            modelResponse,
            RepositoryPath,
            ContextFiles.Select(file => file.RelativePath));

        if (!result.IsSuccess)
        {
            ProposedPatchPreview = null;
            assistantMessage.Content =
                modelResponse +
                Environment.NewLine +
                Environment.NewLine +
                $"Patch preview rejected: {result.Error}" +
                Environment.NewLine +
                "Preview only — no changes applied.";
            StatusText = "Patch preview rejected as malformed or unsafe.";
            return;
        }

        ProposedPatchPreview = result.Preview;
        assistantMessage.Content =
            $"Proposed patch preview created for " +
            $"{result.Preview!.Files.Count} file(s)." +
            Environment.NewLine +
            "Preview only — not applied.";
        StatusText = "Patch preview ready. No source changes were applied.";
    }

    private void DismissPatchPreview()
    {
        ClearProposedPatchPreview();
        StatusText = "Patch preview dismissed. No source changes were applied.";
    }

    private void ClearProposedPatchPreview()
    {
        ProposedPatchPreview = null;
    }

    private void SetPatchRollbackRecord(PatchRollbackRecord rollbackRecord)
    {
        ArgumentNullException.ThrowIfNull(rollbackRecord);

        _patchRollbackRecord = rollbackRecord;
        IsPatchRollbackApproved = false;
        OnPropertyChanged(nameof(HasPatchRollback));
        OnPropertyChanged(nameof(PatchRollbackSummaryText));
        RollbackAppliedPatchCommand.NotifyCanExecuteChanged();
    }

    private void ClearPatchRollbackRecord()
    {
        if (_patchRollbackRecord is null && !IsPatchRollbackApproved)
        {
            return;
        }

        _patchRollbackRecord = null;
        IsPatchRollbackApproved = false;
        OnPropertyChanged(nameof(HasPatchRollback));
        OnPropertyChanged(nameof(PatchRollbackSummaryText));
        RollbackAppliedPatchCommand.NotifyCanExecuteChanged();
    }

    private bool CanApplyProposedPatch()
    {
        return !IsBusy &&
               IsAgentMode &&
               IsPatchApplyApproved &&
               _repositoryIsGit &&
               Directory.Exists(RepositoryPath) &&
               ProposedPatchPreview is { Files.Count: 1 };
    }

    private async Task ApplyProposedPatchAsync()
    {
        if (!CanApplyProposedPatch())
        {
            return;
        }

        ProposedPatchPreview preview = ProposedPatchPreview!;
        ProposedPatchFile reviewedFile = preview.Files[0];
        VerificationToolDescriptor gitStatusTool =
            VerificationTools.Get(VerificationToolKind.GitStatus);
        bool patchApplied = false;

        IsPatchApplyApproved = false;
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        IsBusy = true;
        ElapsedText = "00:00.0";
        StatusText = "Checking clean Git state before approved apply...";
        _stopwatch.Restart();
        _elapsedTimer.Start();

        try
        {
            VerificationRunResult gitStatus =
                await _verificationToolRunner.RunAsync(
                    VerificationToolKind.GitStatus,
                    RepositoryPath,
                    _repositorySolutionFile,
                    progress: null,
                    cancellationToken: _requestCancellation.Token);

            RecordVerificationResult(gitStatusTool, gitStatus);

            if (gitStatus.WasCancelled)
            {
                StatusText =
                    "Approved patch apply cancelled before any source write.";
                return;
            }

            if (!gitStatus.IsSuccess)
            {
                StatusText =
                    "Approved patch apply stopped because Git status failed.";
                return;
            }

            if (!IsCleanGitStatus(gitStatus.Output))
            {
                StatusText =
                    "Approved patch apply requires a clean Git working tree.";
                return;
            }

            PatchApplyResult result =
                await _repositoryPatchService.ApplyAsync(
                    RepositoryPath,
                    preview,
                    _requestCancellation.Token);

            if (!result.IsSuccess)
            {
                StatusText = result.Error ??
                    "The reviewed patch could not be applied safely.";
                return;
            }

            patchApplied = true;

            if (result.RollbackRecord is null)
            {
                ClearPatchRollbackRecord();
            }
            else
            {
                SetPatchRollbackRecord(result.RollbackRecord);
            }

            ClearContextFiles();

            string verificationSummary =
                await RunPostApplyVerificationAsync(
                    _requestCancellation.Token);

            Messages.Add(
                new ChatMessageViewModel(
                    isUser: false,
                    $"Applied the approved patch to " +
                    $"{result.AppliedRelativePath}." +
                    Environment.NewLine +
                    verificationSummary));

            StatusText = verificationSummary;
        }
        catch (OperationCanceledException)
        {
            StatusText = patchApplied
                ? $"Approved patch applied to {reviewedFile.RelativePath}, " +
                  "but post-apply verification was cancelled. The source " +
                  "change remains applied."
                : "Approved patch apply cancelled before completion.";
        }
        catch (Exception exception)
        {
            StatusText = patchApplied
                ? $"Approved patch applied to {reviewedFile.RelativePath}, " +
                  $"but post-apply verification could not complete: " +
                  $"{exception.Message}. The source change remains applied."
                : $"Approved patch apply failed safely: {exception.Message}";
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

    private bool CanRollbackAppliedPatch()
    {
        return !IsBusy &&
               IsAgentMode &&
               IsPatchRollbackApproved &&
               _repositoryIsGit &&
               Directory.Exists(RepositoryPath) &&
               _patchRollbackRecord is not null;
    }

    private async Task RollbackAppliedPatchAsync()
    {
        if (!CanRollbackAppliedPatch())
        {
            return;
        }

        PatchRollbackRecord rollbackRecord = _patchRollbackRecord!;
        bool rollbackCompleted = false;

        IsPatchRollbackApproved = false;
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        IsBusy = true;
        ElapsedText = "00:00.0";
        StatusText = "Revalidating the approved current-session rollback...";
        _stopwatch.Restart();
        _elapsedTimer.Start();

        try
        {
            PatchRollbackResult result =
                await _repositoryPatchService.RollbackAsync(
                    RepositoryPath,
                    rollbackRecord,
                    _requestCancellation.Token);

            if (!result.IsSuccess)
            {
                StatusText = result.Error ??
                    "The applied patch could not be rolled back safely.";
                return;
            }

            rollbackCompleted = true;
            ClearPatchRollbackRecord();
            ClearContextFiles();

            string verificationSummary =
                await RunPostRollbackConfirmationAsync(
                    _requestCancellation.Token);

            Messages.Add(
                new ChatMessageViewModel(
                    isUser: false,
                    $"Restored the exact pre-apply bytes for " +
                    $"{result.RolledBackRelativePath}." +
                    Environment.NewLine +
                    verificationSummary));

            StatusText = verificationSummary;
        }
        catch (OperationCanceledException)
        {
            StatusText = rollbackCompleted
                ? $"Rollback restored {rollbackRecord.RelativePath}, but " +
                  "post-rollback confirmation was cancelled. The rollback " +
                  "remains applied."
                : "Approved rollback cancelled before any source write.";
        }
        catch (Exception exception)
        {
            StatusText = rollbackCompleted
                ? $"Rollback restored {rollbackRecord.RelativePath}, but " +
                  $"confirmation could not complete: {exception.Message}. " +
                  "The rollback remains applied."
                : $"Approved rollback failed safely: {exception.Message}";
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

    private async Task<string> RunPostRollbackConfirmationAsync(
        CancellationToken cancellationToken)
    {
        VerificationRunResult diffCheck =
            await RunProtectedVerificationStepAsync(
                VerificationToolKind.GitDiffCheck,
                "post-rollback",
                cancellationToken);

        if (diffCheck.WasCancelled)
        {
            return "Rollback restored the exact pre-apply bytes, but Git " +
                   "diff check was cancelled. Final Git state was not " +
                   "confirmed; the rollback remains applied.";
        }

        if (!diffCheck.IsSuccess)
        {
            return "Rollback restored the exact pre-apply bytes, but Git " +
                   "diff check failed. Review the retained verification " +
                   "output; the rollback remains applied.";
        }

        VerificationRunResult gitStatus =
            await RunProtectedVerificationStepAsync(
                VerificationToolKind.GitStatus,
                "post-rollback",
                cancellationToken);

        if (gitStatus.WasCancelled)
        {
            return "Rollback restored the exact pre-apply bytes and Git " +
                   "diff check passed, but Git status was cancelled. The " +
                   "rollback remains applied.";
        }

        if (!gitStatus.IsSuccess)
        {
            return "Rollback restored the exact pre-apply bytes and Git " +
                   "diff check passed, but Git status failed. Review the " +
                   "retained evidence; the rollback remains applied.";
        }

        return IsCleanGitStatus(gitStatus.Output)
            ? "Rollback restored the exact pre-apply bytes; Git diff " +
              "check passed and Git status is clean."
            : "Rollback restored the exact pre-apply bytes and Git diff " +
              "check passed, but Git status is not clean. Review the " +
              "retained evidence; the rollback remains applied.";
    }

    private async Task<string> RunPostApplyVerificationAsync(
        CancellationToken cancellationToken)
    {
        VerificationRunResult diffCheck =
            await RunProtectedVerificationStepAsync(
                VerificationToolKind.GitDiffCheck,
                "post-apply",
                cancellationToken);

        if (diffCheck.WasCancelled)
        {
            return "Patch applied, but post-apply Git diff check was " +
                   "cancelled. Build and tests were not run; the source " +
                   "change remains applied.";
        }

        if (!diffCheck.IsSuccess)
        {
            return "Patch applied, but post-apply Git diff check failed. " +
                   "Build and tests were not run; review the retained " +
                   "verification output. The source change remains applied.";
        }

        if (string.IsNullOrWhiteSpace(_repositorySolutionFile))
        {
            return "Patch applied and Git diff check passed. Release build " +
                   "and tests were not run because exactly one .NET " +
                   "solution was not detected.";
        }

        VerificationRunResult build =
            await RunProtectedVerificationStepAsync(
                VerificationToolKind.DotnetBuild,
                "post-apply",
                cancellationToken);

        if (build.WasCancelled)
        {
            return "Patch applied and Git diff check passed, but Release " +
                   "build was cancelled. Tests were not run; the source " +
                   "change remains applied.";
        }

        if (!build.IsSuccess)
        {
            return "Patch applied and Git diff check passed, but Release " +
                   "build failed. Tests were not run; review the retained " +
                   "verification output. The source change remains applied.";
        }

        VerificationRunResult tests =
            await RunProtectedVerificationStepAsync(
                VerificationToolKind.DotnetTest,
                "post-apply",
                cancellationToken);

        if (tests.WasCancelled)
        {
            return "Patch applied; Git diff check and Release build passed, " +
                   "but Release tests were cancelled. The source change " +
                   "remains applied.";
        }

        return tests.IsSuccess
            ? "Patch applied; Git diff check, Release build, and Release " +
              "tests all passed."
            : "Patch applied; Git diff check and Release build passed, but " +
              "Release tests failed. Review the retained verification " +
              "output. The source change remains applied.";
    }

    private async Task<VerificationRunResult>
        RunProtectedVerificationStepAsync(
            VerificationToolKind kind,
            string operationLabel,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationLabel);

        VerificationToolDescriptor tool = VerificationTools.Get(kind);
        DateTimeOffset startedAt = DateTimeOffset.Now;
        VerificationOutput = string.Empty;
        VerificationStatusText =
            $"Running {operationLabel} {tool.Name}...";
        StatusText = VerificationStatusText;
        Progress<VerificationOutputLine> progress =
            new(AppendVerificationOutput);

        VerificationRunResult result;

        try
        {
            result = await _verificationToolRunner.RunAsync(
                kind,
                RepositoryPath,
                _repositorySolutionFile,
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result = new VerificationRunResult(
                kind,
                tool.Name,
                startedAt,
                DateTimeOffset.Now,
                ExitCode: -1,
                WasCancelled: true,
                Output: VerificationOutput);
        }
        catch (Exception exception)
        {
            result = new VerificationRunResult(
                kind,
                $"{tool.Name} (not completed)",
                startedAt,
                DateTimeOffset.Now,
                ExitCode: -1,
                WasCancelled: false,
                Output: exception.Message);
        }

        RecordVerificationResult(tool, result);
        string label = char.ToUpperInvariant(operationLabel[0]) +
            operationLabel[1..];
        VerificationStatusText = result.WasCancelled
            ? $"{label} {tool.Name} cancelled."
            : result.IsSuccess
                ? $"{label} {tool.Name} passed."
                : $"{label} {tool.Name} failed with exit code " +
                  $"{result.ExitCode}.";
        return result;
    }

    private static bool IsCleanGitStatus(string output)
    {
        string[] lines = output
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        return lines.Length == 1 &&
               lines[0].StartsWith("## ", StringComparison.Ordinal);
    }

    private static void PreservePartialResponse(
        ChatMessageViewModel assistantMessage,
        string errorKind,
        string errorMessage)
    {
        string error =
            $"{errorKind}:{Environment.NewLine}{errorMessage}";

        assistantMessage.Content =
            string.IsNullOrWhiteSpace(assistantMessage.Content)
                ? error
                : $"{assistantMessage.Content}{Environment.NewLine}" +
                  $"{Environment.NewLine}{error}";
    }

    private void Cancel()
    {
        if (_requestCancellation is null)
        {
            return;
        }

        StatusText = "Cancelling current operation...";
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
