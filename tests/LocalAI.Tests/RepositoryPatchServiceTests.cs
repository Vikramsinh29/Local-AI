using System.Text;
using LocalAI.Core.Models;
using LocalAI.Core.Repositories;
using LocalAI.Infrastructure.Repositories;

namespace LocalAI.Tests;

public sealed class RepositoryPatchServiceTests : IDisposable
{
    private readonly string _repositoryRoot;
    private readonly string _sourcePath;

    public RepositoryPatchServiceTests()
    {
        _repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"LocalAI-Apply-{Guid.NewGuid():N}");
        _sourcePath = Path.Combine(
            _repositoryRoot,
            "src",
            "Program.cs");

        Directory.CreateDirectory(
            Path.Combine(_repositoryRoot, ".git"));
        Directory.CreateDirectory(
            Path.GetDirectoryName(_sourcePath)!);
    }

    [Fact]
    public async Task ApplyAsync_RevalidatesAndAtomicallyReplacesOneFile()
    {
        await File.WriteAllTextAsync(
            _sourcePath,
            "return 42;\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        ProposedPatchPreview preview = BuildPreview();
        RepositoryPatchService service = new();

        PatchApplyResult result = await service.ApplyAsync(
            _repositoryRoot,
            preview);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Path.Combine("src", "Program.cs"),
            result.AppliedRelativePath);
        byte[] bytes = await File.ReadAllBytesAsync(_sourcePath);
        Assert.True(
            bytes.AsSpan().StartsWith(
                new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal(
            "return 43;\r\n",
            await File.ReadAllTextAsync(_sourcePath));
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.Combine(_repositoryRoot, ".local-ai", "apply")));
    }

    [Fact]
    public async Task ApplyAsync_RejectsSourceChangedAfterPreview()
    {
        await File.WriteAllTextAsync(_sourcePath, "return 42;\n");
        ProposedPatchPreview preview = BuildPreview();
        await File.WriteAllTextAsync(_sourcePath, "return 99;\n");
        RepositoryPatchService service = new();

        PatchApplyResult result = await service.ApplyAsync(
            _repositoryRoot,
            preview);

        Assert.False(result.IsSuccess);
        Assert.Contains("changed", result.Error!);
        Assert.Equal(
            "return 99;\n",
            await File.ReadAllTextAsync(_sourcePath));
    }

    [Fact]
    public async Task ApplyAsync_RejectsMoreThanOneReviewedFile()
    {
        await File.WriteAllTextAsync(_sourcePath, "return 42;\n");
        ProposedPatchPreview parsed = BuildPreview();
        ProposedPatchPreview multiple = parsed with
        {
            Files = [parsed.Files[0], parsed.Files[0]]
        };
        RepositoryPatchService service = new();

        PatchApplyResult result = await service.ApplyAsync(
            _repositoryRoot,
            multiple);

        Assert.False(result.IsSuccess);
        Assert.Contains("exactly one", result.Error!);
        Assert.Equal(
            "return 42;\n",
            await File.ReadAllTextAsync(_sourcePath));
    }

    [Fact]
    public async Task ApplyAsync_RejectsUnsafeReviewedPath()
    {
        await File.WriteAllTextAsync(_sourcePath, "return 42;\n");
        ProposedPatchPreview parsed = BuildPreview();
        ProposedPatchFile unsafeFile = parsed.Files[0] with
        {
            RelativePath = "../outside.cs"
        };
        ProposedPatchPreview unsafePreview = parsed with
        {
            Files = [unsafeFile]
        };
        RepositoryPatchService service = new();

        PatchApplyResult result = await service.ApplyAsync(
            _repositoryRoot,
            unsafePreview);

        Assert.False(result.IsSuccess);
        Assert.Contains("invalid", result.Error!);
        Assert.Equal(
            "return 42;\n",
            await File.ReadAllTextAsync(_sourcePath));
    }

    [Fact]
    public async Task ApplyAsync_HonorsCancellationBeforeWriting()
    {
        await File.WriteAllTextAsync(_sourcePath, "return 42;\n");
        ProposedPatchPreview preview = BuildPreview();
        RepositoryPatchService service = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ApplyAsync(
                _repositoryRoot,
                preview,
                cancellation.Token));

        Assert.Equal(
            "return 42;\n",
            await File.ReadAllTextAsync(_sourcePath));
    }

    private ProposedPatchPreview BuildPreview()
    {
        ProposedPatchParseResult parsed = ProposedPatchParser.Parse(
            "<<<LOCAL_AI_PATCH_V1>>>\n" +
            "SUMMARY:\n" +
            "Change the return value.\n" +
            "<<<FILE:src/Program.cs>>>\n" +
            "<<<ORIGINAL>>>\n" +
            "return 42;\n" +
            "<<<REPLACEMENT>>>\n" +
            "return 43;\n" +
            "<<<END_FILE>>>\n" +
            "<<<END_LOCAL_AI_PATCH>>>",
            _repositoryRoot,
            ["src/Program.cs"]);

        Assert.True(parsed.IsSuccess, parsed.Error);
        return parsed.Preview!;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
        catch
        {
            // Cleanup must not hide test results.
        }
    }
}
