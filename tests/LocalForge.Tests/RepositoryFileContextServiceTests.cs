using LocalForge.Core.Models;
using LocalForge.Core.Repositories;
using LocalForge.Infrastructure.Repositories;

namespace LocalForge.Tests;

public sealed class RepositoryFileContextServiceTests : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly RepositoryFileContextService _service = new();

    public RepositoryFileContextServiceTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LocalForge-Context-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task ReadAsync_ReadsUtf8TextInsideRepository()
    {
        string relativePath = Path.Combine("src", "Program.cs");
        string fullPath = Path.Combine(_temporaryDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "Console.WriteLine(\"Hello\");");

        RepositoryContextReadResult result =
            await _service.ReadAsync(_temporaryDirectory, relativePath, 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(relativePath, result.File!.RelativePath);
        Assert.Contains("Console.WriteLine", result.File.Content);
    }

    [Theory]
    [InlineData("image.png")]
    [InlineData("Generated.g.cs")]
    [InlineData("obj/Generated.cs")]
    public async Task ReadAsync_RejectsBinaryAndGeneratedFiles(
        string relativePath)
    {
        string platformPath = relativePath.Replace(
            '/', Path.DirectorySeparatorChar);
        string fullPath = Path.Combine(_temporaryDirectory, platformPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "content");

        RepositoryContextReadResult result =
            await _service.ReadAsync(_temporaryDirectory, platformPath, 0);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData("appsettings.Development.json")]
    [InlineData("credentials.json")]
    [InlineData("id_rsa")]
    [InlineData("secrets.json")]
    [InlineData("service-account.json")]
    [InlineData("certificate.pem")]
    [InlineData("signing.key")]
    public async Task ReadAsync_RejectsFilesThatMayContainSecrets(
        string relativePath)
    {
        string fullPath = Path.Combine(_temporaryDirectory, relativePath);
        await File.WriteAllTextAsync(fullPath, "secret");

        RepositoryContextReadResult result =
            await _service.ReadAsync(_temporaryDirectory, relativePath, 0);

        Assert.False(result.IsSuccess);
        Assert.Contains("secrets", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_AppliesIndividualAndTotalLimits()
    {
        string largePath = Path.Combine(_temporaryDirectory, "large.txt");
        await File.WriteAllBytesAsync(
            largePath,
            new byte[_service.MaximumFileBytes + 1]);

        RepositoryContextReadResult largeResult =
            await _service.ReadAsync(_temporaryDirectory, "large.txt", 0);

        await File.WriteAllTextAsync(
            Path.Combine(_temporaryDirectory, "small.txt"),
            "small");

        RepositoryContextReadResult totalResult =
            await _service.ReadAsync(
                _temporaryDirectory,
                "small.txt",
                _service.MaximumTotalBytes);

        Assert.False(largeResult.IsSuccess);
        Assert.False(totalResult.IsSuccess);
    }

    [Fact]
    public async Task ReadAsync_RejectsPathOutsideRepository()
    {
        string outsidePath = Path.Combine(
            Path.GetTempPath(),
            $"LocalForge-Outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outsidePath, "outside");

        try
        {
            string relativePath =
                Path.GetRelativePath(_temporaryDirectory, outsidePath);

            RepositoryContextReadResult result =
                await _service.ReadAsync(
                    _temporaryDirectory,
                    relativePath,
                    0);

            Assert.False(result.IsSuccess);
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task ReadAsync_RejectsLinkedRepositoryRoot()
    {
        string targetPath = Path.Combine(_temporaryDirectory, "target");
        string linkedRoot = Path.Combine(_temporaryDirectory, "linked-root");
        Directory.CreateDirectory(targetPath);
        CreateDirectoryJunction(linkedRoot, targetPath);
        await File.WriteAllTextAsync(
            Path.Combine(targetPath, "source.cs"),
            "return 42;");

        RepositoryContextReadResult result =
            await _service.ReadAsync(linkedRoot, "source.cs", 0);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            "Linked files and folders",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
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
    }

    [Fact]
    public void Build_IncludesFileNamesContentsAndUserPrompt()
    {
        RepositoryContextFile file =
            new("src/Program.cs", "return 42;", 10);

        string prompt = RepositoryContextPromptBuilder.Build(
            "Explain this code.",
            [file]);

        Assert.Contains("--- FILE: src/Program.cs ---", prompt);
        Assert.Contains("return 42;", prompt);
        Assert.EndsWith("Explain this code.", prompt);
    }

    [Fact]
    public void Build_RejectsContextOverTokenBudget()
    {
        string content = new(
            'a',
            RepositoryContextPromptBuilder.MaximumContextTokens * 4 + 1);
        RepositoryContextFile file =
            new("large.txt", content, content.Length);

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => RepositoryContextPromptBuilder.Build(
                    "Explain this file.",
                    [file]));

        Assert.Contains(
            "Remove files",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsLikelyToSlowGeneration_UsesCpuWarningThreshold()
    {
        string content = new(
            'a',
            RepositoryContextPromptBuilder.SlowContextThresholdTokens * 4);
        RepositoryContextFile file =
            new("context.txt", content, content.Length);

        bool isLikelySlow =
            RepositoryContextPromptBuilder.IsLikelyToSlowGeneration(
                [file]);

        Assert.True(isLikelySlow);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
        catch
        {
            // Cleanup must not hide test results.
        }
    }
}
