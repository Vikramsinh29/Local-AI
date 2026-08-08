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
        byte[] originalBytes = await File.ReadAllBytesAsync(_sourcePath);
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
        PatchRollbackRecord rollbackRecord =
            Assert.IsType<PatchRollbackRecord>(result.RollbackRecord);
        Assert.True(
            originalBytes.AsSpan().SequenceEqual(
                rollbackRecord.OriginalBytes.Span));
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.Combine(_repositoryRoot, ".local-ai", "apply")));
    }

    [Fact]
    public async Task RollbackAsync_RestoresExactOriginalBytes()
    {
        await File.WriteAllTextAsync(
            _sourcePath,
            "return 42;\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        byte[] originalBytes = await File.ReadAllBytesAsync(_sourcePath);
        RepositoryPatchService service = new();
        PatchApplyResult apply = await service.ApplyAsync(
            _repositoryRoot,
            BuildPreview());

        PatchRollbackResult rollback = await service.RollbackAsync(
            _repositoryRoot,
            apply.RollbackRecord!);

        Assert.True(rollback.IsSuccess, rollback.Error);
        Assert.Equal(
            Path.Combine("src", "Program.cs"),
            rollback.RolledBackRelativePath);
        byte[] restoredBytes = await File.ReadAllBytesAsync(_sourcePath);
        Assert.True(originalBytes.AsSpan().SequenceEqual(restoredBytes));
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.Combine(_repositoryRoot, ".local-ai", "apply")));
    }

    [Fact]
    public async Task RollbackAsync_RejectsExternalEditAfterApply()
    {
        await File.WriteAllTextAsync(_sourcePath, "return 42;\n");
        RepositoryPatchService service = new();
        PatchApplyResult apply = await service.ApplyAsync(
            _repositoryRoot,
            BuildPreview());
        await File.WriteAllTextAsync(_sourcePath, "return 99;\n");

        PatchRollbackResult rollback = await service.RollbackAsync(
            _repositoryRoot,
            apply.RollbackRecord!);

        Assert.False(rollback.IsSuccess);
        Assert.Contains("externally changed", rollback.Error!);
        Assert.Equal(
            "return 99;\n",
            await File.ReadAllTextAsync(_sourcePath));
    }

    [Fact]
    public async Task RollbackAsync_RejectsDifferentRepositoryRoot()
    {
        await File.WriteAllTextAsync(_sourcePath, "return 42;\n");
        RepositoryPatchService service = new();
        PatchApplyResult apply = await service.ApplyAsync(
            _repositoryRoot,
            BuildPreview());

        PatchRollbackResult rollback = await service.RollbackAsync(
            _repositoryRoot + "-different",
            apply.RollbackRecord!);

        Assert.False(rollback.IsSuccess);
        Assert.Contains("repository changed", rollback.Error!);
        Assert.Equal(
            "return 43;\n",
            await File.ReadAllTextAsync(_sourcePath));
    }

    [Fact]
    public async Task RollbackAsync_RejectsUnsafeRecordedPath()
    {
        await File.WriteAllTextAsync(_sourcePath, "return 43;\n");
        PatchRollbackRecord unsafeRecord = new(
            _repositoryRoot,
            "../outside.cs",
            "return 42;\n"u8.ToArray(),
            "return 43;\n"u8.ToArray());
        RepositoryPatchService service = new();

        PatchRollbackResult rollback = await service.RollbackAsync(
            _repositoryRoot,
            unsafeRecord);

        Assert.False(rollback.IsSuccess);
        Assert.Contains("invalid", rollback.Error!);
        Assert.Equal(
            "return 43;\n",
            await File.ReadAllTextAsync(_sourcePath));
    }

    [Fact]
    public async Task RollbackAsync_RejectsLinkedRepositoryRoot()
    {
        await File.WriteAllTextAsync(_sourcePath, "return 43;\n");
        string linkedRoot = _repositoryRoot + "-linked";
        CreateDirectoryJunction(linkedRoot, _repositoryRoot);
        PatchRollbackRecord linkedRecord = new(
            linkedRoot,
            Path.Combine("src", "Program.cs"),
            "return 42;\n"u8.ToArray(),
            "return 43;\n"u8.ToArray());
        RepositoryPatchService service = new();

        try
        {
            PatchRollbackResult rollback = await service.RollbackAsync(
                linkedRoot,
                linkedRecord);

            Assert.False(rollback.IsSuccess);
            Assert.Contains("linked", rollback.Error!);
            Assert.Equal(
                "return 43;\n",
                await File.ReadAllTextAsync(_sourcePath));
        }
        finally
        {
            if (Directory.Exists(linkedRoot))
            {
                Directory.Delete(linkedRoot);
            }
        }
    }

    [Fact]
    public async Task RollbackAsync_HonorsCancellationBeforeWriting()
    {
        await File.WriteAllTextAsync(_sourcePath, "return 42;\n");
        RepositoryPatchService service = new();
        PatchApplyResult apply = await service.ApplyAsync(
            _repositoryRoot,
            BuildPreview());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RollbackAsync(
                _repositoryRoot,
                apply.RollbackRecord!,
                cancellation.Token));

        Assert.Equal(
            "return 43;\n",
            await File.ReadAllTextAsync(_sourcePath));
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

    private static void CreateDirectoryJunction(
        string junctionPath,
        string targetPath)
    {
        System.Diagnostics.ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            $"New-Item -ItemType Junction -Path " +
            $"'{junctionPath.Replace("'", "''")}' -Target " +
            $"'{targetPath.Replace("'", "''")}' | Out-Null");

        using System.Diagnostics.Process process =
            System.Diagnostics.Process.Start(startInfo)!;

        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.True(Directory.Exists(junctionPath));
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

