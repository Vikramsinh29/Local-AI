using LocalAI.Core.Models;
using LocalAI.Infrastructure.Verification;

namespace LocalAI.Tests;

public sealed class VerificationCommandFactoryTests : IDisposable
{
    private readonly string _temporaryDirectory;

    public VerificationCommandFactoryTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LocalAI-Verification-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void Create_BuildUsesFixedArgumentsWithoutRestoreOrShell()
    {
        string solutionPath = Path.Combine(
            _temporaryDirectory,
            "Sample.slnx");

        File.WriteAllText(solutionPath, string.Empty);

        VerificationCommand command =
            VerificationCommandFactory.Create(
                VerificationToolKind.DotnetBuild,
                _temporaryDirectory,
                "Sample.slnx");
        string artifactsPath = Path.Combine(
            _temporaryDirectory,
            ".local-ai",
            "verification");

        Assert.Equal("dotnet", command.FileName);
        Assert.Equal(
            [
                "build",
                solutionPath,
                "-c",
                "Release",
                "--no-restore",
                "--nologo",
                "--artifacts-path",
                artifactsPath,
                "-p:BaseIntermediateOutputPath=obj\\"
            ],
            command.Arguments);
        Assert.DoesNotContain("cmd", command.DisplayText);
        Assert.DoesNotContain("powershell", command.DisplayText);
        string displaySolutionPath = solutionPath.Contains(' ')
            ? $"\"{solutionPath}\""
            : solutionPath;
        Assert.Equal(
            VerificationTools.Get(VerificationToolKind.DotnetBuild)
                .CommandPreview.Replace(
                    "{solution}",
                    displaySolutionPath,
                    StringComparison.Ordinal).Replace(
                    "{artifacts}",
                    artifactsPath.Contains(' ')
                        ? $"\"{artifactsPath}\""
                        : artifactsPath,
                    StringComparison.Ordinal),
            command.DisplayText);
    }

    [Fact]
    public void Create_TestUsesSameIsolatedArtifactsWithoutBuildOrRestore()
    {
        string solutionPath = Path.Combine(
            _temporaryDirectory,
            "Sample.slnx");
        string artifactsPath = Path.Combine(
            _temporaryDirectory,
            ".local-ai",
            "verification");

        File.WriteAllText(solutionPath, string.Empty);

        VerificationCommand command =
            VerificationCommandFactory.Create(
                VerificationToolKind.DotnetTest,
                _temporaryDirectory,
                "Sample.slnx");

        Assert.Equal(
            [
                "test",
                solutionPath,
                "-c",
                "Release",
                "--no-build",
                "--no-restore",
                "--nologo",
                "--artifacts-path",
                artifactsPath,
                "-p:BaseIntermediateOutputPath=obj\\"
            ],
            command.Arguments);
    }

    [Fact]
    public void Create_RejectsFileAtLocalStateDirectoryPath()
    {
        string solutionPath = Path.Combine(
            _temporaryDirectory,
            "Sample.slnx");

        File.WriteAllText(solutionPath, string.Empty);
        File.WriteAllText(
            Path.Combine(_temporaryDirectory, ".local-ai"),
            "not a directory");

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => VerificationCommandFactory.Create(
                    VerificationToolKind.DotnetBuild,
                    _temporaryDirectory,
                    "Sample.slnx"));

        Assert.Contains("must be a directory", exception.Message);
    }

    [Fact]
    public void Create_GitStatusUsesOnlyAllowListedArguments()
    {
        Directory.CreateDirectory(
            Path.Combine(_temporaryDirectory, ".git"));

        VerificationCommand command =
            VerificationCommandFactory.Create(
                VerificationToolKind.GitStatus,
                _temporaryDirectory,
                solutionRelativePath: null);

        Assert.Equal("git", command.FileName);
        Assert.Equal(
            [
                "-c",
                "core.fsmonitor=false",
                "status",
                "--short",
                "--branch",
                "--untracked-files=all"
            ],
            command.Arguments);
        Assert.Equal(
            VerificationTools.Get(VerificationToolKind.GitStatus)
                .CommandPreview,
            command.DisplayText);
    }

    [Fact]
    public void Create_GitDiffDisablesExternalDiffAndTextConversion()
    {
        Directory.CreateDirectory(
            Path.Combine(_temporaryDirectory, ".git"));

        VerificationCommand command =
            VerificationCommandFactory.Create(
                VerificationToolKind.GitDiffCheck,
                _temporaryDirectory,
                solutionRelativePath: null);

        Assert.Equal(
            [
                "-c",
                "core.fsmonitor=false",
                "diff",
                "--check",
                "--no-ext-diff",
                "--no-textconv"
            ],
            command.Arguments);
        Assert.Equal(
            VerificationTools.Get(VerificationToolKind.GitDiffCheck)
                .CommandPreview,
            command.DisplayText);
    }

    [Fact]
    public void Create_RejectsSolutionOutsideRepository()
    {
        Assert.Throws<InvalidOperationException>(
            () => VerificationCommandFactory.Create(
                VerificationToolKind.DotnetTest,
                _temporaryDirectory,
                Path.Combine("..", "Outside.slnx")));
    }

    [Fact]
    public void Create_RejectsGitCommandOutsideGitRepository()
    {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => VerificationCommandFactory.Create(
                    VerificationToolKind.GitDiffCheck,
                    _temporaryDirectory,
                    solutionRelativePath: null));

        Assert.Contains("local .git directory", exception.Message);
    }

    [Fact]
    public void Create_RejectsLinkedGitWorktreeMetadataFile()
    {
        File.WriteAllText(
            Path.Combine(_temporaryDirectory, ".git"),
            "gitdir: C:\\outside\\repository");

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => VerificationCommandFactory.Create(
                    VerificationToolKind.GitStatus,
                    _temporaryDirectory,
                    solutionRelativePath: null));

        Assert.Contains("Linked Git worktrees", exception.Message);
    }

    [Fact]
    public void Create_RejectsUnknownTool()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VerificationCommandFactory.Create(
                (VerificationToolKind)999,
                _temporaryDirectory,
                solutionRelativePath: null));
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
