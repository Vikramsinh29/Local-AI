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
