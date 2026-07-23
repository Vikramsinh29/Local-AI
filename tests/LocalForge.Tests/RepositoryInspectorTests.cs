using LocalForge.Infrastructure.Repositories;
using Xunit;

namespace LocalForge.Tests;

public sealed class RepositoryInspectorTests : IDisposable
{
    private readonly string _temporaryDirectory;

    public RepositoryInspectorTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LocalForge-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task InspectAsync_DetectsGitSolutionAndProjects()
    {
        Directory.CreateDirectory(
            Path.Combine(_temporaryDirectory, ".git"));

        Directory.CreateDirectory(
            Path.Combine(_temporaryDirectory, "src", "Sample.App"));

        Directory.CreateDirectory(
            Path.Combine(_temporaryDirectory, "obj"));

        await File.WriteAllTextAsync(
            Path.Combine(_temporaryDirectory, "Sample.slnx"),
            string.Empty);

        await File.WriteAllTextAsync(
            Path.Combine(
                _temporaryDirectory,
                "src",
                "Sample.App",
                "Sample.App.csproj"),
            string.Empty);

        await File.WriteAllTextAsync(
            Path.Combine(
                _temporaryDirectory,
                "obj",
                "Ignored.csproj"),
            string.Empty);

        RepositoryInspector inspector = new();

        var result =
            await inspector.InspectAsync(_temporaryDirectory);

        Assert.True(result.IsGitRepository);

        Assert.Equal(
            ["Sample.slnx"],
            result.SolutionFiles);

        Assert.Equal(
            [
                Path.Combine(
                    "src",
                    "Sample.App",
                    "Sample.App.csproj")
            ],
            result.ProjectFiles);
    }

    [Fact]
    public async Task InspectAsync_DetectsGitWorktreeFile()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_temporaryDirectory, ".git"),
            "gitdir: C:/example/worktree");

        RepositoryInspector inspector = new();

        var result =
            await inspector.InspectAsync(_temporaryDirectory);

        Assert.True(result.IsGitRepository);
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
