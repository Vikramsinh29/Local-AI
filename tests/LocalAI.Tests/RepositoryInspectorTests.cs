using LocalAI.Core.Models;
using LocalAI.Infrastructure.Repositories;

namespace LocalAI.Tests;

public sealed class RepositoryInspectorTests : IDisposable
{
    private readonly string _temporaryDirectory;

    public RepositoryInspectorTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LocalAI-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task InspectAsync_DetectsProjectsAndBuildsTree()
    {
        Directory.CreateDirectory(
            Path.Combine(_temporaryDirectory, ".git"));

        string projectDirectory = Path.Combine(
            _temporaryDirectory,
            "src",
            "Sample.App");

        Directory.CreateDirectory(projectDirectory);

        string ignoredDirectory = Path.Combine(
            _temporaryDirectory,
            "obj");

        Directory.CreateDirectory(ignoredDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(_temporaryDirectory, "Sample.slnx"),
            string.Empty);

        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                "Sample.App.csproj"),
            string.Empty);

        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                "Program.cs"),
            "Console.WriteLine(\"Hello\");");

        await File.WriteAllTextAsync(
            Path.Combine(
                ignoredDirectory,
                "Ignored.csproj"),
            string.Empty);

        RepositoryInspector inspector = new();

        RepositoryInfo result =
            await inspector.InspectAsync(
                _temporaryDirectory);

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

        List<RepositoryTreeNode> flattened =
            Flatten(result.RootEntries).ToList();

        Assert.Contains(
            flattened,
            entry =>
                entry.RelativePath.Equals(
                    Path.Combine(
                        "src",
                        "Sample.App",
                        "Program.cs"),
                    StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            flattened,
            entry =>
                entry.RelativePath.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) ||
                entry.RelativePath.StartsWith(
                    $"obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InspectAsync_DetectsGitWorktreeFile()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_temporaryDirectory, ".git"),
            "gitdir: C:/example/worktree");

        RepositoryInspector inspector = new();

        RepositoryInfo result =
            await inspector.InspectAsync(
                _temporaryDirectory);

        Assert.True(result.IsGitRepository);
    }

    private static IEnumerable<RepositoryTreeNode> Flatten(
        IEnumerable<RepositoryTreeNode> nodes)
    {
        foreach (RepositoryTreeNode node in nodes)
        {
            yield return node;

            foreach (RepositoryTreeNode child in
                     Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(
                _temporaryDirectory,
                recursive: true);
        }
        catch
        {
            // Cleanup must not hide test results.
        }
    }
}
