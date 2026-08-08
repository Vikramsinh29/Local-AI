using System.Text;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using LocalAI.Desktop.ViewModels;

namespace LocalAI.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task ProjectMemory_DefaultSelectionNeverEntersAgentPrompt()
    {
        string repositoryRoot = CreateTemporaryRepository();
        ProjectMemoryEntry memory = CreateMemoryEntry(
            ProjectMemoryCategory.Decision,
            "Local decision",
            "MEMORY_SENTINEL_MUST_NOT_REACH_MODEL");
        FakeProjectMemoryService memoryService = new([memory]);
        FakeOllamaClient ollama = new(
            (_, _) => StreamText("### Evidence Used\n- Sample.cs"));

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                ollama,
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository),
                projectMemoryService: memoryService);

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();

            ProjectMemoryEntryViewModel displayed = Assert.Single(
                viewModel.ProjectMemoryEntries);
            Assert.Equal("Decision", displayed.CategoryText);
            Assert.Contains("tokens", displayed.SizeText);
            Assert.Contains(
                "no memory selected",
                viewModel.ProjectMemorySummary,
                StringComparison.OrdinalIgnoreCase);
            Assert.Null(viewModel.SelectedPromptProjectMemoryEntry);

            viewModel.ContextFiles.Add(
                new RepositoryContextFileViewModel(
                    new RepositoryContextFile(
                        "Sample.cs",
                        "source evidence",
                        15)));
            viewModel.IsAgentMode = true;
            viewModel.MessageInput = "Plan safely.";
            await viewModel.SendCommand.ExecuteAsync();

            Assert.DoesNotContain(
                "MEMORY_SENTINEL_MUST_NOT_REACH_MODEL",
                ollama.LastPrompt);

            viewModel.IsPatchPreviewRequested = true;
            viewModel.MessageInput = "Preview the same safe change.";
            await viewModel.SendCommand.ExecuteAsync();

            Assert.DoesNotContain(
                "MEMORY_SENTINEL_MUST_NOT_REACH_MODEL",
                ollama.LastPrompt);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ProjectMemory_ExplicitSelectionEntersPromptInPrecedenceOrder()
    {
        string repositoryRoot = CreateTemporaryRepository();
        ProjectMemoryEntry memory = CreateMemoryEntry(
            ProjectMemoryCategory.Decision,
            "Greeting convention",
            "MEMORY_SENTINEL_USE_HELLO_LOCAL_AI");
        FakeProjectMemoryService memoryService = new([memory]);
        FakeOllamaClient ollama = new(
            (_, _) => StreamText(
                "### Evidence Used\n" +
                "- Sample.cs\n" +
                $"- project-memory:{memory.Id:D}"));

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                ollama,
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository),
                projectMemoryService: memoryService);

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.ContextFiles.Add(
                new RepositoryContextFileViewModel(
                    new RepositoryContextFile(
                        "Sample.cs",
                        "SOURCE_SENTINEL",
                        15)));
            viewModel.IsAgentMode = true;
            viewModel.SelectedPromptProjectMemoryEntry =
                Assert.Single(viewModel.ProjectMemoryEntries);
            viewModel.MessageInput = "USER_SENTINEL";

            await viewModel.SendCommand.ExecuteAsync();

            int user = ollama.LastPrompt.IndexOf(
                "USER_SENTINEL",
                StringComparison.Ordinal);
            int memoryIndex = ollama.LastPrompt.IndexOf(
                "MEMORY_SENTINEL_USE_HELLO_LOCAL_AI",
                StringComparison.Ordinal);
            int source = ollama.LastPrompt.IndexOf(
                "SOURCE_SENTINEL",
                StringComparison.Ordinal);

            Assert.True(user >= 0);
            Assert.True(memoryIndex > user);
            Assert.True(source > memoryIndex);
            Assert.Contains(
                $"project-memory:{memory.Id:D}",
                ollama.LastPrompt);
            Assert.Equal(2, memoryService.LoadCount);
            Assert.Contains(
                "verified",
                viewModel.StatusText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ProjectMemory_ChangedSelectionIsRejectedBeforeGeneration()
    {
        string repositoryRoot = CreateTemporaryRepository();
        ProjectMemoryEntry memory = CreateMemoryEntry(
            ProjectMemoryCategory.KnownIssue,
            "Known issue",
            "ORIGINAL_MEMORY");
        FakeProjectMemoryService memoryService = new([memory]);
        FakeOllamaClient ollama = new(
            (_, _) => StreamText("must not run"));

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                ollama,
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository),
                projectMemoryService: memoryService);

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.ContextFiles.Add(
                new RepositoryContextFileViewModel(
                    new RepositoryContextFile(
                        "Sample.cs",
                        "source",
                        6)));
            viewModel.IsAgentMode = true;
            viewModel.SelectedPromptProjectMemoryEntry =
                Assert.Single(viewModel.ProjectMemoryEntries);
            memoryService.Entries[0] = memory with
            {
                Content = "EXTERNALLY_CHANGED_MEMORY",
                SizeBytes = memory.SizeBytes + 10,
                EstimatedTokens = memory.EstimatedTokens + 3,
                UpdatedAtUtc = memory.UpdatedAtUtc.AddSeconds(1)
            };
            viewModel.MessageInput = "Plan safely.";

            await viewModel.SendCommand.ExecuteAsync();

            Assert.Equal(0, ollama.GenerationCount);
            Assert.Null(viewModel.SelectedPromptProjectMemoryEntry);
            Assert.Contains(
                "nothing was sent",
                viewModel.ProjectMemoryStatusText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ProjectMemory_CorruptReloadIsRejectedBeforeGeneration()
    {
        string repositoryRoot = CreateTemporaryRepository();
        ProjectMemoryEntry memory = CreateMemoryEntry(
            ProjectMemoryCategory.Architecture,
            "Layering",
            "Keep I/O in Infrastructure.");
        FakeProjectMemoryService memoryService = new([memory]);
        FakeOllamaClient ollama = new(
            (_, _) => StreamText("must not run"));

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                ollama,
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository),
                projectMemoryService: memoryService);

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.ContextFiles.Add(
                new RepositoryContextFileViewModel(
                    new RepositoryContextFile(
                        "Sample.cs",
                        "source",
                        6)));
            viewModel.IsAgentMode = true;
            viewModel.SelectedPromptProjectMemoryEntry =
                Assert.Single(viewModel.ProjectMemoryEntries);
            memoryService.LoadError = "memory.json is malformed";
            viewModel.MessageInput = "Plan safely.";

            await viewModel.SendCommand.ExecuteAsync();

            Assert.Equal(0, ollama.GenerationCount);
            Assert.Null(viewModel.SelectedPromptProjectMemoryEntry);
            Assert.Contains(
                "could not be revalidated",
                viewModel.ProjectMemoryStatusText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "nothing was sent",
                viewModel.ProjectMemoryStatusText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ProjectMemory_CreateUpdateDelete_ConsumesSeparateApprovals()
    {
        string repositoryRoot = CreateTemporaryRepository();
        FakeProjectMemoryService memoryService = new();

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                new FakeOllamaClient((_, _) => StreamText("unused")),
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository),
                projectMemoryService: memoryService);

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.IsAgentMode = true;
            viewModel.SelectedProjectMemoryCategory =
                ProjectMemoryCategory.Architecture;
            viewModel.ProjectMemoryTitle = "Layers";
            viewModel.ProjectMemoryContent =
                "Desktop depends on Core interfaces.";

            Assert.False(viewModel.SaveProjectMemoryCommand.CanExecute(null));

            viewModel.IsProjectMemoryChangeApproved = true;
            await viewModel.SaveProjectMemoryCommand.ExecuteAsync();

            Assert.Equal(1, memoryService.CreateCount);
            Assert.False(viewModel.IsProjectMemoryChangeApproved);
            Assert.Single(viewModel.ProjectMemoryEntries);

            viewModel.SelectedPromptProjectMemoryEntry =
                viewModel.ProjectMemoryEntries[0];

            viewModel.ProjectMemoryTitle = "Layering";
            Assert.False(viewModel.IsProjectMemoryChangeApproved);
            viewModel.IsProjectMemoryChangeApproved = true;
            await viewModel.SaveProjectMemoryCommand.ExecuteAsync();

            Assert.Equal(1, memoryService.UpdateCount);
            Assert.False(viewModel.IsProjectMemoryChangeApproved);
            Assert.Null(viewModel.SelectedPromptProjectMemoryEntry);

            viewModel.SelectedPromptProjectMemoryEntry =
                Assert.Single(viewModel.ProjectMemoryEntries);
            Assert.False(viewModel.DeleteProjectMemoryCommand.CanExecute(null));
            viewModel.IsProjectMemoryChangeApproved = true;
            await viewModel.DeleteProjectMemoryCommand.ExecuteAsync();

            Assert.Equal(1, memoryService.DeleteCount);
            Assert.False(viewModel.IsProjectMemoryChangeApproved);
            Assert.Empty(viewModel.ProjectMemoryEntries);
            Assert.Null(viewModel.SelectedPromptProjectMemoryEntry);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ProjectMemory_RefreshClearsStaleStateBeforeReload()
    {
        string repositoryRoot = CreateTemporaryRepository();
        FakeProjectMemoryService memoryService = new(
            [
                CreateMemoryEntry(
                    ProjectMemoryCategory.KnownIssue,
                    "Old",
                    "Old repository state.")
            ]);

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                new FakeOllamaClient((_, _) => StreamText("unused")),
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository),
                projectMemoryService: memoryService);

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            Assert.Single(viewModel.ProjectMemoryEntries);
            viewModel.IsAgentMode = true;
            viewModel.SelectedProjectMemoryEntry =
                viewModel.ProjectMemoryEntries[0];
            viewModel.SelectedPromptProjectMemoryEntry =
                viewModel.ProjectMemoryEntries[0];
            viewModel.IsProjectMemoryChangeApproved = true;
            memoryService.Entries.Clear();

            await viewModel.RefreshRepositoryCommand.ExecuteAsync();

            Assert.Empty(viewModel.ProjectMemoryEntries);
            Assert.Null(viewModel.SelectedProjectMemoryEntry);
            Assert.Null(viewModel.SelectedPromptProjectMemoryEntry);
            Assert.False(viewModel.IsProjectMemoryChangeApproved);
            Assert.Equal(string.Empty, viewModel.ProjectMemoryTitle);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task ProjectMemory_CorruptLoadIsDisplayedHonestly()
    {
        string repositoryRoot = CreateTemporaryRepository();
        FakeProjectMemoryService memoryService = new()
        {
            LoadError = "memory.json is malformed"
        };

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                new FakeOllamaClient((_, _) => StreamText("unused")),
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository),
                projectMemoryService: memoryService);

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();

            Assert.Empty(viewModel.ProjectMemoryEntries);
            Assert.Contains(
                "malformed",
                viewModel.ProjectMemoryStatusText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task AgentPlan_RejectsUngroundedEvidenceCitations()
    {
        string repositoryRoot = CreateTemporaryRepository();
        ProjectInstructionManifest manifest = new(
            [
                CreateInstruction(
                    ProjectInstructionKind.AgentRules,
                    "AGENTS.md",
                    "AGENT_RULES"),
                CreateInstruction(
                    ProjectInstructionKind.Skill,
                    "skills/review/SKILL.md",
                    "SKILL_RULES")
            ],
            []);
        FakeOllamaClient ollama = new(
            (_, _) => StreamText(
                "### Evidence Used\n- AGENTS.md\n- SKILL.md\n- README.md"));

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                ollama,
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository),
                projectInstructionService:
                    new FakeProjectInstructionService(manifest));

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.SelectedInstructionSkill =
                Assert.Single(viewModel.AvailableInstructionSkills);
            viewModel.ContextFiles.Add(
                new RepositoryContextFileViewModel(
                    new RepositoryContextFile(
                        "Sample.cs",
                        "source",
                        6)));
            viewModel.IsAgentMode = true;
            viewModel.MessageInput = "Plan this work.";

            await viewModel.SendCommand.ExecuteAsync();

            Assert.Contains(
                "rejected by the evidence gate",
                viewModel.Messages[^1].Content);
            Assert.Contains(
                "skills/review/SKILL.md",
                viewModel.Messages[^1].Content);
            Assert.Contains(
                "Sample.cs",
                viewModel.Messages[^1].Content);
            Assert.Contains(
                "README.md",
                viewModel.Messages[^1].Content);
            Assert.Contains(
                "rejected",
                viewModel.StatusText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task SendCommand_DisplaysFirstChunkBeforeCompletion()
    {
        TaskCompletionSource firstChunkConsumed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeOllamaClient ollama = new(
            (_, cancellationToken) => StreamUntilReleased(
                firstChunkConsumed,
                releaseCompletion,
                cancellationToken));
        using MainWindowViewModel viewModel = CreateViewModel(ollama);
        viewModel.MessageInput = "Explain.";

        Task send = viewModel.SendCommand.ExecuteAsync();
        await firstChunkConsumed.Task;

        Assert.True(viewModel.IsBusy);
        Assert.Equal("first", viewModel.Messages[^1].Content);

        releaseCompletion.SetResult();
        await send;

        Assert.Equal("first second", viewModel.Messages[^1].Content);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SendCommand_PreventsDuplicateConcurrentSend()
    {
        TaskCompletionSource firstChunkConsumed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeOllamaClient ollama = new(
            (_, cancellationToken) => StreamUntilReleased(
                firstChunkConsumed,
                releaseCompletion,
                cancellationToken));
        using MainWindowViewModel viewModel = CreateViewModel(ollama);
        viewModel.MessageInput = "First.";

        Task firstSend = viewModel.SendCommand.ExecuteAsync();
        await firstChunkConsumed.Task;
        viewModel.MessageInput = "Second.";

        await viewModel.SendCommand.ExecuteAsync();

        Assert.Equal(1, ollama.GenerationCount);
        Assert.False(viewModel.SendCommand.CanExecute(null));

        releaseCompletion.SetResult();
        await firstSend;
    }

    [Fact]
    public async Task CancelCommand_RestoresBusyAndCommandState()
    {
        TaskCompletionSource streamingStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeOllamaClient ollama = new(
            (_, cancellationToken) =>
                WaitForCancellation(
                    streamingStarted,
                    cancellationToken));
        using MainWindowViewModel viewModel = CreateViewModel(ollama);
        viewModel.MessageInput = "Long request.";

        Task send = viewModel.SendCommand.ExecuteAsync();
        await streamingStarted.Task;

        viewModel.CancelCommand.Execute(null);
        await send;

        Assert.False(viewModel.IsBusy);
        Assert.Equal("Request cancelled.", viewModel.StatusText);

        viewModel.MessageInput = "Try again.";
        Assert.True(viewModel.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task SendCommand_PreservesPartialOutputWhenStreamFails()
    {
        FakeOllamaClient ollama = new(
            (_, _) => StreamThenFail());
        using MainWindowViewModel viewModel = CreateViewModel(ollama);
        viewModel.MessageInput = "Explain.";

        await viewModel.SendCommand.ExecuteAsync();

        string response = viewModel.Messages[^1].Content;
        Assert.StartsWith("partial", response);
        Assert.Contains("Generation error", response);
        Assert.Contains("stream failed", response);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public void AgentMode_RequiresSelectedRepositoryBeforeSend()
    {
        FakeOllamaClient ollama = new(
            (_, _) => StreamThenFail());
        using MainWindowViewModel viewModel = CreateViewModel(ollama);
        viewModel.MessageInput = "Plan this feature.";

        Assert.True(viewModel.SendCommand.CanExecute(null));

        viewModel.IsAgentMode = true;

        Assert.False(viewModel.SendCommand.CanExecute(null));
        Assert.Contains(
            "read-only",
            viewModel.StatusText,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "No source files selected",
            viewModel.AgentEvidenceText);
    }

    [Fact]
    public async Task ProjectInstructions_ShowManifestSelectOneSkillAndClearOnRefresh()
    {
        string repositoryRoot = CreateTemporaryRepository();
        ProjectInstructionManifest manifest = new(
            [
                CreateInstruction(
                    ProjectInstructionKind.AgentRules,
                    "AGENTS.md",
                    "AGENT_RULES"),
                CreateInstruction(
                    ProjectInstructionKind.Skill,
                    "skills/review/SKILL.md",
                    "SKILL_RULES")
            ],
            []);
        FakeOllamaClient ollama = new(
            (_, _) => StreamText("plan"));

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                ollama,
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository),
                projectInstructionService:
                    new FakeProjectInstructionService(manifest));

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();

            Assert.Equal(2, viewModel.ProjectInstructions.Count);
            Assert.True(viewModel.ProjectInstructions[0].IsIncluded);
            Assert.Contains("B", viewModel.ProjectInstructions[0].SizeText);
            Assert.Null(viewModel.SelectedInstructionSkill);

            viewModel.SelectedInstructionSkill =
                Assert.Single(viewModel.AvailableInstructionSkills);

            Assert.Equal(
                2,
                viewModel.ProjectInstructions.Count(item => item.IsIncluded));
            Assert.Contains(
                "1 skill included",
                viewModel.InstructionManifestSummary);
            Assert.Contains(
                "B",
                viewModel.InstructionManifestSummary);

            viewModel.IsAgentMode = true;
            viewModel.MessageInput = "USER_REQUEST";
            await viewModel.SendCommand.ExecuteAsync();

            int user = ollama.LastPrompt.IndexOf(
                "USER_REQUEST",
                StringComparison.Ordinal);
            int agents = ollama.LastPrompt.IndexOf(
                "AGENT_RULES",
                StringComparison.Ordinal);
            int skill = ollama.LastPrompt.IndexOf(
                "SKILL_RULES",
                StringComparison.Ordinal);
            Assert.True(user >= 0);
            Assert.True(agents > user);
            Assert.True(skill > agents);

            await viewModel.RefreshRepositoryCommand.ExecuteAsync();

            Assert.Null(viewModel.SelectedInstructionSkill);
            Assert.Single(
                viewModel.ProjectInstructions,
                item => item.IsIncluded);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task VerificationCommand_RequiresApprovalAndRecordsOutput()
    {
        string repositoryRoot = CreateTemporaryRepository();

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);

            FakeVerificationToolRunner runner = new(
                (tool, _, _, progress, _) =>
                {
                    progress?.Report(
                        new VerificationOutputLine(
                            "verification output",
                            IsError: false));

                    DateTimeOffset now = DateTimeOffset.UtcNow;

                    return Task.FromResult(
                        new VerificationRunResult(
                            tool,
                            "git status --short --branch",
                            now,
                            now.AddSeconds(1),
                            ExitCode: 0,
                            WasCancelled: false,
                            Output: "verification output"));
                });

            using MainWindowViewModel viewModel = CreateViewModel(
                new FakeOllamaClient((_, _) => StreamThenFail()),
                runner,
                new FakeFolderPickerService(repositoryRoot),
                new FakeRepositoryInspector(repository));

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.IsAgentMode = true;

            Assert.Contains(
                "git -c core.fsmonitor=false status",
                viewModel.VerificationStatusText);

            Assert.False(
                viewModel.RunVerificationCommand.CanExecute(null));

            viewModel.IsVerificationApproved = true;

            Assert.True(
                viewModel.RunVerificationCommand.CanExecute(null));

            await viewModel.RunVerificationCommand.ExecuteAsync();

            Assert.Equal(1, runner.RunCount);
            Assert.False(viewModel.IsVerificationApproved);
            Assert.Single(viewModel.VerificationAuditEntries);
            Assert.Contains(
                "verification output",
                viewModel.VerificationOutput);
            Assert.Equal(
                "Git status passed.",
                viewModel.VerificationStatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task VerificationCommand_CancelRecordsCancelledRun()
    {
        string repositoryRoot = CreateTemporaryRepository();
        TaskCompletionSource started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);

            FakeVerificationToolRunner runner = new(
                async (tool, _, _, _, cancellationToken) =>
                {
                    DateTimeOffset startedAt = DateTimeOffset.UtcNow;
                    started.SetResult();

                    try
                    {
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    return new VerificationRunResult(
                        tool,
                        "git status --short --branch",
                        startedAt,
                        DateTimeOffset.UtcNow,
                        ExitCode: -1,
                        WasCancelled: true,
                        Output: "cancelled output");
                });

            using MainWindowViewModel viewModel = CreateViewModel(
                new FakeOllamaClient((_, _) => StreamThenFail()),
                runner,
                new FakeFolderPickerService(repositoryRoot),
                new FakeRepositoryInspector(repository));

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.IsAgentMode = true;
            viewModel.IsVerificationApproved = true;

            Task run =
                viewModel.RunVerificationCommand.ExecuteAsync();

            await started.Task;
            viewModel.CancelCommand.Execute(null);
            await run;

            Assert.False(viewModel.IsBusy);
            Assert.Single(viewModel.VerificationAuditEntries);
            Assert.Equal(
                "Git status cancelled.",
                viewModel.VerificationStatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task VerificationCommand_RecordsNonzeroExitAsFailure()
    {
        string repositoryRoot = CreateTemporaryRepository();

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);

            FakeVerificationToolRunner runner = new(
                (tool, _, _, _, _) =>
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;

                    return Task.FromResult(
                        new VerificationRunResult(
                            tool,
                            "git diff --check --no-ext-diff " +
                            "--no-textconv",
                            now,
                            now.AddSeconds(1),
                            ExitCode: 2,
                            WasCancelled: false,
                            Output: "whitespace error"));
                });

            using MainWindowViewModel viewModel = CreateViewModel(
                new FakeOllamaClient((_, _) => StreamThenFail()),
                runner,
                new FakeFolderPickerService(repositoryRoot),
                new FakeRepositoryInspector(repository));

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.IsAgentMode = true;
            viewModel.SelectedVerificationTool =
                VerificationTools.Get(
                    VerificationToolKind.GitDiffCheck);
            viewModel.IsVerificationApproved = true;

            await viewModel.RunVerificationCommand.ExecuteAsync();

            VerificationAuditEntryViewModel audit =
                Assert.Single(viewModel.VerificationAuditEntries);

            Assert.False(viewModel.IsVerificationApproved);
            Assert.Equal("Failed (2)", audit.Outcome);
            Assert.Contains(
                "failed with exit code 2",
                viewModel.VerificationStatusText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task VerificationCommand_RejectsBuildWithoutSingleSolution()
    {
        string repositoryRoot = CreateTemporaryRepository();

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["One.slnx", "Two.slnx"]);

            FakeVerificationToolRunner runner = new(
                (_, _, _, _, _) =>
                    throw new InvalidOperationException(
                        "Runner must not be called."));

            using MainWindowViewModel viewModel = CreateViewModel(
                new FakeOllamaClient((_, _) => StreamThenFail()),
                runner,
                new FakeFolderPickerService(repositoryRoot),
                new FakeRepositoryInspector(repository));

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.IsAgentMode = true;
            viewModel.SelectedVerificationTool =
                VerificationTools.Get(
                    VerificationToolKind.DotnetBuild);
            viewModel.IsVerificationApproved = true;

            Assert.False(
                viewModel.RunVerificationCommand.CanExecute(null));
            Assert.Contains(
                "exactly one",
                viewModel.VerificationStatusText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, runner.RunCount);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchPreview_RequiresSelectedSourceEvidence()
    {
        string repositoryRoot = CreateTemporaryRepository();

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                new FakeOllamaClient((_, _) => StreamText("unused")),
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository));

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.IsAgentMode = true;
            viewModel.IsPatchPreviewRequested = true;
            viewModel.MessageInput = "Propose a change.";

            Assert.False(viewModel.SendCommand.CanExecute(null));
            Assert.False(viewModel.HasProposedPatchPreview);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchPreview_ParsesAndDisplaysValidatedProposal()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        string modelResponse = BuildValidPatchResponse();
        FakeOllamaClient ollama = new(
            (_, _) => StreamText(modelResponse));

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                ollama,
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository));

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.ContextFiles.Add(
                new RepositoryContextFileViewModel(
                    new RepositoryContextFile(
                        "src/Program.cs",
                        "return 42;",
                        10)));
            viewModel.IsAgentMode = true;
            viewModel.IsPatchPreviewRequested = true;
            viewModel.MessageInput = "Change the return value.";

            await viewModel.SendCommand.ExecuteAsync();

            Assert.True(viewModel.HasProposedPatchPreview);
            Assert.Single(viewModel.ProposedPatchPreview!.Files);
            Assert.Equal(
                "1 file(s) • +1 / -1",
                viewModel.PatchPreviewSummaryText);
            Assert.Contains(
                "Preview only — not applied",
                viewModel.Messages[^1].Content);
            Assert.Contains(
                "controlled patch-preview mode",
                ollama.LastPrompt);
            Assert.Equal(
                "return 42;\n",
                File.ReadAllText(
                    Path.Combine(
                        repositoryRoot,
                        "src",
                        "Program.cs")));

            viewModel.IsPatchPreviewRequested = false;

            Assert.False(viewModel.HasProposedPatchPreview);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchPreview_RejectsMalformedModelOutput()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeOllamaClient ollama = new(
            (_, _) => StreamText("```diff\nunsafe\n```"));

        try
        {
            RepositoryInfo repository = CreateRepositoryInfo(
                repositoryRoot,
                isGitRepository: true,
                solutionFiles: ["Sample.slnx"]);
            using MainWindowViewModel viewModel = CreateViewModel(
                ollama,
                folderPickerService:
                    new FakeFolderPickerService(repositoryRoot),
                repositoryInspector:
                    new FakeRepositoryInspector(repository));

            await viewModel.BrowseRepositoryCommand.ExecuteAsync();
            viewModel.ContextFiles.Add(
                new RepositoryContextFileViewModel(
                    new RepositoryContextFile(
                        "src/Program.cs",
                        "return 42;",
                        10)));
            viewModel.IsAgentMode = true;
            viewModel.IsPatchPreviewRequested = true;
            viewModel.MessageInput = "Change the return value.";

            await viewModel.SendCommand.ExecuteAsync();

            Assert.False(viewModel.HasProposedPatchPreview);
            Assert.Contains(
                "Patch preview rejected",
                viewModel.Messages[^1].Content);
            Assert.Contains(
                "malformed or unsafe",
                viewModel.StatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchApply_RequiresApprovalAndCleanGitThenConsumesPreview()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = CreateGitStatusRunner("## main");
        FakeRepositoryPatchService patchService = new(
            (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath,
                    CreateRollbackRecord(repositoryRoot))));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);

            Assert.False(
                viewModel.ApplyProposedPatchCommand.CanExecute(null));

            viewModel.IsPatchApplyApproved = true;

            Assert.True(
                viewModel.ApplyProposedPatchCommand.CanExecute(null));

            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();

            Assert.Equal(4, runner.RunCount);
            Assert.Equal(
                new[]
                {
                    VerificationToolKind.GitStatus,
                    VerificationToolKind.GitDiffCheck,
                    VerificationToolKind.DotnetBuild,
                    VerificationToolKind.DotnetTest
                },
                runner.RunTools);
            Assert.Equal(1, patchService.ApplyCount);
            Assert.False(viewModel.IsPatchApplyApproved);
            Assert.False(viewModel.HasProposedPatchPreview);
            Assert.True(viewModel.HasPatchRollback);
            Assert.Empty(viewModel.ContextFiles);
            Assert.Equal(4, viewModel.VerificationAuditEntries.Count);
            Assert.Contains("all passed", viewModel.StatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchApply_RejectsDirtyGitAndConsumesApprovalOnly()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = CreateGitStatusRunner(
            "## main\n M src/Program.cs");
        FakeRepositoryPatchService patchService = new(
            (_, _, _) => throw new InvalidOperationException(
                "Patch service must not run for dirty Git."));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;

            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();

            Assert.Equal(1, runner.RunCount);
            Assert.Equal(0, patchService.ApplyCount);
            Assert.False(viewModel.IsPatchApplyApproved);
            Assert.True(viewModel.HasProposedPatchPreview);
            Assert.Contains("clean Git", viewModel.StatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchApply_RetainsPreviewWhenSourceRevalidationFails()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = CreateGitStatusRunner("## main");
        FakeRepositoryPatchService patchService = new(
            (_, _, _) => Task.FromResult(
                PatchApplyResult.Failure(
                    "The reviewed source file changed after preview.")));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;

            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();

            Assert.Equal(1, patchService.ApplyCount);
            Assert.True(viewModel.HasProposedPatchPreview);
            Assert.False(viewModel.IsPatchApplyApproved);
            Assert.Contains("changed after preview", viewModel.StatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchApply_StopsPostApplySequenceWhenDiffCheckFails()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = new(
            (tool, _, _, _, _) =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                return Task.FromResult(
                    new VerificationRunResult(
                        tool,
                        tool.ToString(),
                        now,
                        now,
                        ExitCode: tool == VerificationToolKind.GitDiffCheck
                            ? 2
                            : 0,
                        WasCancelled: false,
                        Output: tool == VerificationToolKind.GitStatus
                            ? "## main"
                            : "whitespace error"));
            });
        FakeRepositoryPatchService patchService = new(
            (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath)));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;

            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();

            Assert.Equal(
                new[]
                {
                    VerificationToolKind.GitStatus,
                    VerificationToolKind.GitDiffCheck
                },
                runner.RunTools);
            Assert.False(viewModel.HasProposedPatchPreview);
            Assert.Contains("diff check failed", viewModel.StatusText);
            Assert.Contains("remains applied", viewModel.StatusText);
            Assert.Contains("Patch applied", viewModel.Messages[^1].Content);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchApply_StopsPostApplySequenceWhenBuildFails()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = new(
            (tool, _, _, _, _) =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                return Task.FromResult(
                    new VerificationRunResult(
                        tool,
                        tool.ToString(),
                        now,
                        now,
                        ExitCode: tool == VerificationToolKind.DotnetBuild
                            ? 1
                            : 0,
                        WasCancelled: false,
                        Output: tool == VerificationToolKind.GitStatus
                            ? "## main"
                            : "verification output"));
            });
        FakeRepositoryPatchService patchService = new(
            (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath)));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;

            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();

            Assert.Equal(
                new[]
                {
                    VerificationToolKind.GitStatus,
                    VerificationToolKind.GitDiffCheck,
                    VerificationToolKind.DotnetBuild
                },
                runner.RunTools);
            Assert.Contains("Release build failed", viewModel.StatusText);
            Assert.Contains("remains applied", viewModel.StatusText);
            Assert.DoesNotContain(
                VerificationToolKind.DotnetTest,
                runner.RunTools);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchApply_ReportsPostApplyTestFailure()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = new(
            (tool, _, _, _, _) =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                return Task.FromResult(
                    new VerificationRunResult(
                        tool,
                        tool.ToString(),
                        now,
                        now,
                        ExitCode: tool == VerificationToolKind.DotnetTest
                            ? 1
                            : 0,
                        WasCancelled: false,
                        Output: tool == VerificationToolKind.GitStatus
                            ? "## main"
                            : "verification output"));
            });
        FakeRepositoryPatchService patchService = new(
            (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath)));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;

            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();

            Assert.Equal(4, runner.RunCount);
            Assert.Contains("Release tests failed", viewModel.StatusText);
            Assert.Contains("remains applied", viewModel.StatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchApply_ReportsCancelledPostApplyVerification()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = new(
            (tool, _, _, _, _) =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                return Task.FromResult(
                    new VerificationRunResult(
                        tool,
                        tool.ToString(),
                        now,
                        now,
                        ExitCode: tool == VerificationToolKind.GitDiffCheck
                            ? -1
                            : 0,
                        WasCancelled:
                            tool == VerificationToolKind.GitDiffCheck,
                        Output: tool == VerificationToolKind.GitStatus
                            ? "## main"
                            : "cancelled"));
            });
        FakeRepositoryPatchService patchService = new(
            (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath)));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;

            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();

            Assert.Equal(2, runner.RunCount);
            Assert.False(viewModel.HasProposedPatchPreview);
            Assert.Contains("cancelled", viewModel.StatusText);
            Assert.Contains("remains applied", viewModel.StatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchApply_WithoutSingleSolutionRunsOnlyDiffCheck()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = CreateGitStatusRunner("## main");
        FakeRepositoryPatchService patchService = new(
            (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath)));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService,
                    hasSingleSolution: false);
            viewModel.IsPatchApplyApproved = true;

            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();

            Assert.Equal(
                new[]
                {
                    VerificationToolKind.GitStatus,
                    VerificationToolKind.GitDiffCheck
                },
                runner.RunTools);
            Assert.Contains("not detected", viewModel.StatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchRollback_RequiresApprovalThenConfirmsCleanGit()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = CreateGitStatusRunner("## main");
        FakeRepositoryPatchService patchService = new(
            apply: (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath,
                    CreateRollbackRecord(repositoryRoot))),
            rollback: (_, record, _) => Task.FromResult(
                PatchRollbackResult.Success(record.RelativePath)));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;
            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();

            Assert.True(viewModel.HasPatchRollback);
            Assert.False(
                viewModel.RollbackAppliedPatchCommand.CanExecute(null));

            viewModel.IsPatchRollbackApproved = true;

            Assert.True(
                viewModel.RollbackAppliedPatchCommand.CanExecute(null));

            await viewModel.RollbackAppliedPatchCommand.ExecuteAsync();

            Assert.Equal(1, patchService.RollbackCount);
            Assert.False(viewModel.IsPatchRollbackApproved);
            Assert.False(viewModel.HasPatchRollback);
            Assert.Equal(
                new[]
                {
                    VerificationToolKind.GitDiffCheck,
                    VerificationToolKind.GitStatus
                },
                runner.RunTools.TakeLast(2).ToArray());
            Assert.Contains("Git status is clean", viewModel.StatusText);
            Assert.Contains(
                "exact pre-apply bytes",
                viewModel.Messages[^1].Content);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchRollback_RejectionConsumesApprovalAndRetainsRecord()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = CreateGitStatusRunner("## main");
        FakeRepositoryPatchService patchService = new(
            apply: (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath,
                    CreateRollbackRecord(repositoryRoot))),
            rollback: (_, _, _) => Task.FromResult(
                PatchRollbackResult.Failure(
                    "The applied source file was externally changed.")));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;
            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();
            int runsAfterApply = runner.RunCount;
            viewModel.IsPatchRollbackApproved = true;

            await viewModel.RollbackAppliedPatchCommand.ExecuteAsync();

            Assert.Equal(1, patchService.RollbackCount);
            Assert.False(viewModel.IsPatchRollbackApproved);
            Assert.True(viewModel.HasPatchRollback);
            Assert.Equal(runsAfterApply, runner.RunCount);
            Assert.Contains("externally changed", viewModel.StatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchRollback_RepositoryRefreshInvalidatesRecord()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = CreateGitStatusRunner("## main");
        FakeRepositoryPatchService patchService = new(
            (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath,
                    CreateRollbackRecord(repositoryRoot))));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;
            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();
            Assert.True(viewModel.HasPatchRollback);

            await viewModel.RefreshRepositoryCommand.ExecuteAsync();

            Assert.False(viewModel.HasPatchRollback);
            Assert.False(
                viewModel.RollbackAppliedPatchCommand.CanExecute(null));
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchRollback_NewPreviewInvalidatesPreviousRecord()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        FakeVerificationToolRunner runner = CreateGitStatusRunner("## main");
        FakeRepositoryPatchService patchService = new(
            (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath,
                    CreateRollbackRecord(repositoryRoot))));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;
            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();
            Assert.True(viewModel.HasPatchRollback);

            viewModel.ContextFiles.Add(
                new RepositoryContextFileViewModel(
                    new RepositoryContextFile(
                        "src/Program.cs",
                        "return 42;",
                        10)));
            viewModel.MessageInput = "Create another preview.";

            await viewModel.SendCommand.ExecuteAsync();

            Assert.True(viewModel.HasProposedPatchPreview);
            Assert.False(viewModel.HasPatchRollback);
            Assert.False(viewModel.IsPatchRollbackApproved);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    [Fact]
    public async Task PatchRollback_StopsConfirmationWhenDiffCheckFails()
    {
        string repositoryRoot = CreateTemporaryRepository();
        CreatePatchSourceFile(repositoryRoot);
        int diffCheckCount = 0;
        FakeVerificationToolRunner runner = new(
            (tool, _, _, _, _) =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                bool rollbackDiffFailure =
                    tool == VerificationToolKind.GitDiffCheck &&
                    ++diffCheckCount == 2;
                return Task.FromResult(
                    new VerificationRunResult(
                        tool,
                        tool.ToString(),
                        now,
                        now,
                        ExitCode: rollbackDiffFailure ? 1 : 0,
                        WasCancelled: false,
                        Output: tool == VerificationToolKind.GitStatus
                            ? "## main"
                            : "verification output"));
            });
        FakeRepositoryPatchService patchService = new(
            apply: (_, preview, _) => Task.FromResult(
                PatchApplyResult.Success(
                    preview.Files[0].RelativePath,
                    CreateRollbackRecord(repositoryRoot))),
            rollback: (_, record, _) => Task.FromResult(
                PatchRollbackResult.Success(record.RelativePath)));

        try
        {
            using MainWindowViewModel viewModel =
                await CreateViewModelWithPatchPreviewAsync(
                    repositoryRoot,
                    runner,
                    patchService);
            viewModel.IsPatchApplyApproved = true;
            await viewModel.ApplyProposedPatchCommand.ExecuteAsync();
            int runsAfterApply = runner.RunCount;
            viewModel.IsPatchRollbackApproved = true;

            await viewModel.RollbackAppliedPatchCommand.ExecuteAsync();

            Assert.Equal(runsAfterApply + 1, runner.RunCount);
            Assert.Equal(
                VerificationToolKind.GitDiffCheck,
                runner.RunTools[^1]);
            Assert.False(viewModel.HasPatchRollback);
            Assert.Contains("diff check failed", viewModel.StatusText);
            Assert.Contains("rollback remains applied", viewModel.StatusText);
        }
        finally
        {
            DeleteTemporaryRepository(repositoryRoot);
        }
    }

    private static FakeVerificationToolRunner CreateGitStatusRunner(
        string output)
    {
        return new FakeVerificationToolRunner(
            (tool, _, _, _, _) =>
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                return Task.FromResult(
                    new VerificationRunResult(
                        tool,
                        "git status --short --branch",
                        now,
                        now,
                        ExitCode: 0,
                        WasCancelled: false,
                        Output: output));
            });
    }

    private static async Task<MainWindowViewModel>
        CreateViewModelWithPatchPreviewAsync(
            string repositoryRoot,
            IVerificationToolRunner runner,
            IRepositoryPatchService patchService,
            bool hasSingleSolution = true)
    {
        IReadOnlyList<string> solutionFiles = hasSingleSolution
            ? new[] { "Sample.slnx" }
            : Array.Empty<string>();
        RepositoryInfo repository = CreateRepositoryInfo(
            repositoryRoot,
            isGitRepository: true,
            solutionFiles: solutionFiles);
        MainWindowViewModel viewModel = CreateViewModel(
            new FakeOllamaClient(
                (_, _) => StreamText(BuildValidPatchResponse())),
            runner,
            new FakeFolderPickerService(repositoryRoot),
            new FakeRepositoryInspector(repository),
            patchService);

        await viewModel.BrowseRepositoryCommand.ExecuteAsync();
        viewModel.ContextFiles.Add(
            new RepositoryContextFileViewModel(
                new RepositoryContextFile(
                    "src/Program.cs",
                    "return 42;",
                    10)));
        viewModel.IsAgentMode = true;
        viewModel.IsPatchPreviewRequested = true;
        viewModel.MessageInput = "Change the return value.";
        await viewModel.SendCommand.ExecuteAsync();
        Assert.True(viewModel.HasProposedPatchPreview);
        return viewModel;
    }

    private static MainWindowViewModel CreateViewModel(
        IOllamaClient ollamaClient,
        IVerificationToolRunner? verificationToolRunner = null,
        IFolderPickerService? folderPickerService = null,
        IRepositoryInspector? repositoryInspector = null,
        IRepositoryPatchService? repositoryPatchService = null,
        IProjectInstructionService? projectInstructionService = null,
        IProjectMemoryService? projectMemoryService = null)
    {
        MainWindowViewModel viewModel = new(
            ollamaClient,
            folderPickerService ?? new FakeFolderPickerService(),
            repositoryInspector ?? new FakeRepositoryInspector(),
            new FakeRepositoryFileContextService(),
            repositoryPatchService ??
                new FakeRepositoryPatchService(),
            verificationToolRunner ??
                new FakeVerificationToolRunner(),
            projectInstructionService ??
                new FakeProjectInstructionService(),
            projectMemoryService ??
                new FakeProjectMemoryService())
        {
            SelectedModel = "qwen2.5-coder:3b"
        };

        return viewModel;
    }

    private static string CreateTemporaryRepository()
    {
        string repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"LocalAI-ViewModel-{Guid.NewGuid():N}");

        Directory.CreateDirectory(repositoryRoot);
        return repositoryRoot;
    }

    private static RepositoryInfo CreateRepositoryInfo(
        string repositoryRoot,
        bool isGitRepository,
        IReadOnlyList<string> solutionFiles)
    {
        return new RepositoryInfo(
            repositoryRoot,
            isGitRepository,
            solutionFiles,
            [],
            []);
    }

    private static void DeleteTemporaryRepository(
        string repositoryRoot)
    {
        try
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
        catch
        {
            // Cleanup must not hide test results.
        }
    }

    private static async IAsyncEnumerable<string> StreamUntilReleased(
        TaskCompletionSource firstChunkConsumed,
        TaskCompletionSource releaseCompletion,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        yield return "first";
        firstChunkConsumed.SetResult();
        await releaseCompletion.Task.WaitAsync(cancellationToken);
        yield return " second";
    }

    private static async IAsyncEnumerable<string> WaitForCancellation(
        TaskCompletionSource streamingStarted,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        streamingStarted.SetResult();
        await Task.Delay(
            Timeout.InfiniteTimeSpan,
            cancellationToken);
        yield break;
    }

    private static async IAsyncEnumerable<string> StreamThenFail()
    {
        yield return "partial";
        await Task.Yield();
        throw new InvalidDataException("stream failed");
    }

    private static async IAsyncEnumerable<string> StreamText(string text)
    {
        await Task.Yield();
        yield return text;
    }

    private static string BuildValidPatchResponse()
    {
        return
            "<<<LOCAL_AI_PATCH_V1>>>\n" +
            "SUMMARY:\n" +
            "Change the return value.\n" +
            "<<<FILE:src/Program.cs>>>\n" +
            "<<<ORIGINAL>>>\n" +
            "return 42;\n" +
            "<<<REPLACEMENT>>>\n" +
            "return 43;\n" +
            "<<<END_FILE>>>\n" +
            "<<<END_LOCAL_AI_PATCH>>>";
    }

    private static PatchRollbackRecord CreateRollbackRecord(
        string repositoryRoot)
    {
        return new PatchRollbackRecord(
            repositoryRoot,
            Path.Combine("src", "Program.cs"),
            "return 42;\n"u8.ToArray(),
            "return 43;\n"u8.ToArray());
    }

    private static ProjectInstructionFile CreateInstruction(
        ProjectInstructionKind kind,
        string relativePath,
        string content)
    {
        return new ProjectInstructionFile(
            kind,
            relativePath,
            content.Length,
            Math.Max(1, (content.Length + 3) / 4),
            content,
            ExclusionReason: null);
    }

    private static ProjectMemoryEntry CreateMemoryEntry(
        ProjectMemoryCategory category,
        string title,
        string content)
    {
        long bytes = Encoding.UTF8.GetByteCount(title) +
                     Encoding.UTF8.GetByteCount(content) + 1;
        return new ProjectMemoryEntry(
            Guid.NewGuid(),
            category,
            title,
            content,
            bytes,
            Math.Max(1, (int)((bytes + 3) / 4)),
            DateTimeOffset.UtcNow);
    }

    private static void CreatePatchSourceFile(string repositoryRoot)
    {
        string sourceDirectory = Path.Combine(repositoryRoot, "src");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "Program.cs"),
            "return 42;\n");
    }

    private sealed class FakeOllamaClient(
        Func<GenerationProfile, CancellationToken, IAsyncEnumerable<string>>
            streamFactory)
        : IOllamaClient
    {
        public int GenerationCount { get; private set; }

        public string LastPrompt { get; private set; } = string.Empty;

        public Task<bool> IsAvailableAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<string>> GetModelsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(
                ["qwen2.5-coder:3b"]);

        public IAsyncEnumerable<string> StreamGenerateAsync(
            string model,
            string prompt,
            GenerationProfile profile,
            CancellationToken cancellationToken = default)
        {
            GenerationCount++;
            LastPrompt = prompt;
            return streamFactory(profile, cancellationToken);
        }
    }

    private sealed class FakeFolderPickerService(
        string? selectedFolder = null) :
        IFolderPickerService
    {
        public string? PickFolder(string? initialDirectory = null) =>
            selectedFolder;
    }

    private sealed class FakeRepositoryInspector(
        RepositoryInfo? repository = null) :
        IRepositoryInspector
    {
        public Task<RepositoryInfo> InspectAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            return repository is null
                ? throw new NotSupportedException()
                : Task.FromResult(repository);
        }
    }

    private sealed class FakeVerificationToolRunner(
        Func<
            VerificationToolKind,
            string,
            string?,
            IProgress<VerificationOutputLine>?,
            CancellationToken,
            Task<VerificationRunResult>>? run = null) :
        IVerificationToolRunner
    {
        public int RunCount { get; private set; }

        public List<VerificationToolKind> RunTools { get; } = [];

        public Task<VerificationRunResult> RunAsync(
            VerificationToolKind tool,
            string repositoryRoot,
            string? solutionRelativePath,
            IProgress<VerificationOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            RunTools.Add(tool);

            return run is null
                ? throw new NotSupportedException()
                : run(
                    tool,
                    repositoryRoot,
                    solutionRelativePath,
                    progress,
                    cancellationToken);
        }
    }

    private sealed class FakeRepositoryPatchService(
        Func<
            string,
            ProposedPatchPreview,
            CancellationToken,
            Task<PatchApplyResult>>? apply = null,
        Func<
            string,
            PatchRollbackRecord,
            CancellationToken,
            Task<PatchRollbackResult>>? rollback = null) :
        IRepositoryPatchService
    {
        public int ApplyCount { get; private set; }

        public int RollbackCount { get; private set; }

        public Task<PatchApplyResult> ApplyAsync(
            string repositoryRoot,
            ProposedPatchPreview preview,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;

            return apply is null
                ? throw new NotSupportedException()
                : apply(repositoryRoot, preview, cancellationToken);
        }

        public Task<PatchRollbackResult> RollbackAsync(
            string repositoryRoot,
            PatchRollbackRecord rollbackRecord,
            CancellationToken cancellationToken = default)
        {
            RollbackCount++;

            return rollback is null
                ? throw new NotSupportedException()
                : rollback(
                    repositoryRoot,
                    rollbackRecord,
                    cancellationToken);
        }
    }

    private sealed class FakeRepositoryFileContextService :
        IRepositoryFileContextService
    {
        public long MaximumFileBytes => 128 * 1024;

        public long MaximumTotalBytes => 512 * 1024;

        public Task<RepositoryContextReadResult> ReadAsync(
            string repositoryRoot,
            string relativePath,
            long currentTotalBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProjectInstructionService(
        ProjectInstructionManifest? manifest = null) :
        IProjectInstructionService
    {
        public Task<ProjectInstructionManifest> DiscoverAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                manifest ?? ProjectInstructionManifest.Empty);
    }

    private sealed class FakeProjectMemoryService(
        IEnumerable<ProjectMemoryEntry>? entries = null) :
        IProjectMemoryService
    {
        public List<ProjectMemoryEntry> Entries { get; } =
            entries?.ToList() ?? [];

        public string? LoadError { get; set; }

        public int CreateCount { get; private set; }

        public int UpdateCount { get; private set; }

        public int DeleteCount { get; private set; }

        public int LoadCount { get; private set; }

        public int MaximumEntries => 16;

        public int MaximumEntryBytes => 1024;

        public int MaximumCombinedBytes => 8 * 1024;

        public int MaximumCombinedTokens => 2000;

        public Task<ProjectMemoryLoadResult> LoadAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            string storagePath = Path.Combine(
                Path.GetTempPath(),
                "Local-AI",
                "ProjectMemory",
                "fake",
                "memory.json");
            return Task.FromResult(LoadError is null
                ? ProjectMemoryLoadResult.Success(
                    Entries.ToArray(),
                    storagePath)
                : ProjectMemoryLoadResult.Failure(
                    storagePath,
                    LoadError));
        }

        public Task<ProjectMemoryMutationResult> CreateAsync(
            string repositoryRoot,
            ProjectMemoryCategory category,
            string title,
            string content,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            ProjectMemoryEntry entry = CreateMemoryEntry(
                category,
                title,
                content);
            Entries.Add(entry);
            return Task.FromResult(
                ProjectMemoryMutationResult.Success(
                    Entries.ToArray(),
                    entry));
        }

        public Task<ProjectMemoryMutationResult> UpdateAsync(
            string repositoryRoot,
            Guid entryId,
            ProjectMemoryCategory category,
            string title,
            string content,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            int index = Entries.FindIndex(entry => entry.Id == entryId);
            ProjectMemoryEntry entry = CreateMemoryEntry(
                category,
                title,
                content) with
            {
                Id = entryId
            };
            Entries[index] = entry;
            return Task.FromResult(
                ProjectMemoryMutationResult.Success(
                    Entries.ToArray(),
                    entry));
        }

        public Task<ProjectMemoryMutationResult> DeleteAsync(
            string repositoryRoot,
            Guid entryId,
            CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            ProjectMemoryEntry entry = Entries.Single(
                item => item.Id == entryId);
            Entries.Remove(entry);
            return Task.FromResult(
                ProjectMemoryMutationResult.Success(
                    Entries.ToArray(),
                    entry));
        }
    }
}
