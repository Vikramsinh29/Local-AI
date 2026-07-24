using LocalForge.Core.Interfaces;
using LocalForge.Core.Models;
using LocalForge.Desktop.ViewModels;

namespace LocalForge.Tests;

public sealed class MainWindowViewModelTests
{
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

    private static MainWindowViewModel CreateViewModel(
        IOllamaClient ollamaClient)
    {
        MainWindowViewModel viewModel = new(
            ollamaClient,
            new FakeFolderPickerService(),
            new FakeRepositoryInspector(),
            new FakeRepositoryFileContextService())
        {
            SelectedModel = "qwen2.5-coder:3b"
        };

        return viewModel;
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

    private sealed class FakeOllamaClient(
        Func<GenerationProfile, CancellationToken, IAsyncEnumerable<string>>
            streamFactory)
        : IOllamaClient
    {
        public int GenerationCount { get; private set; }

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
            return streamFactory(profile, cancellationToken);
        }
    }

    private sealed class FakeFolderPickerService :
        IFolderPickerService
    {
        public string? PickFolder(string? initialDirectory = null) =>
            null;
    }

    private sealed class FakeRepositoryInspector :
        IRepositoryInspector
    {
        public Task<RepositoryInfo> InspectAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
}
