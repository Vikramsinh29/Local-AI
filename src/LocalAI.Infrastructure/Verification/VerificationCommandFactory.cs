using LocalAI.Core.Models;

namespace LocalAI.Infrastructure.Verification;

public static class VerificationCommandFactory
{
    private const string IntermediateOutputArgument =
        "-p:BaseIntermediateOutputPath=obj\\";

    public static VerificationCommand Create(
        VerificationToolKind tool,
        string repositoryRoot,
        string? solutionRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        VerificationToolDescriptor descriptor =
            VerificationTools.Get(tool);

        string fullRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(repositoryRoot));

        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(
                $"Repository folder does not exist: {fullRoot}");
        }

        RejectReparsePoint(fullRoot, "Repository root");

        if (descriptor.RequiresGitRepository)
        {
            string gitPath = Path.Combine(fullRoot, ".git");

            if (!Directory.Exists(gitPath))
            {
                throw new InvalidOperationException(
                    "Controlled Git verification requires a repository " +
                    "with a local .git directory. Linked Git worktrees " +
                    "are not supported.");
            }

            RejectReparsePoint(gitPath, "Git metadata directory");
        }

        return tool switch
        {
            VerificationToolKind.GitStatus =>
                CreateGitStatus(fullRoot),
            VerificationToolKind.GitDiffCheck =>
                CreateGitDiffCheck(fullRoot),
            VerificationToolKind.DotnetBuild =>
                CreateDotnetBuild(
                    fullRoot,
                    ValidateSolutionPath(
                        fullRoot,
                        solutionRelativePath),
                    ValidateVerificationArtifactsPath(fullRoot)),
            VerificationToolKind.DotnetTest =>
                CreateDotnetTest(
                    fullRoot,
                    ValidateSolutionPath(
                        fullRoot,
                        solutionRelativePath),
                    ValidateVerificationArtifactsPath(fullRoot)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(tool),
                tool,
                "Unknown verification tool.")
        };
    }

    private static VerificationCommand CreateGitStatus(
        string repositoryRoot)
    {
        string[] arguments =
        [
            "-c",
            "core.fsmonitor=false",
            "status",
            "--short",
            "--branch",
            "--untracked-files=all"
        ];

        return new VerificationCommand(
            "git",
            arguments,
            repositoryRoot,
            "git -c core.fsmonitor=false status --short --branch " +
            "--untracked-files=all");
    }

    private static VerificationCommand CreateGitDiffCheck(
        string repositoryRoot)
    {
        string[] arguments =
        [
            "-c",
            "core.fsmonitor=false",
            "diff",
            "--check",
            "--no-ext-diff",
            "--no-textconv"
        ];

        return new VerificationCommand(
            "git",
            arguments,
            repositoryRoot,
            "git -c core.fsmonitor=false diff --check --no-ext-diff " +
            "--no-textconv");
    }

    private static VerificationCommand CreateDotnetBuild(
        string repositoryRoot,
        string solutionPath,
        string artifactsPath)
    {
        string[] arguments =
        [
            "build",
            solutionPath,
            "-c",
            "Release",
            "--no-restore",
            "--nologo",
            "--artifacts-path",
            artifactsPath,
            IntermediateOutputArgument
        ];

        return new VerificationCommand(
            "dotnet",
            arguments,
            repositoryRoot,
            $"dotnet build {QuoteForDisplay(solutionPath)} " +
            "-c Release --no-restore --nologo " +
            $"--artifacts-path {QuoteForDisplay(artifactsPath)} " +
            IntermediateOutputArgument);
    }

    private static VerificationCommand CreateDotnetTest(
        string repositoryRoot,
        string solutionPath,
        string artifactsPath)
    {
        string[] arguments =
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
            IntermediateOutputArgument
        ];

        return new VerificationCommand(
            "dotnet",
            arguments,
            repositoryRoot,
            $"dotnet test {QuoteForDisplay(solutionPath)} " +
            "-c Release --no-build --no-restore --nologo " +
            $"--artifacts-path {QuoteForDisplay(artifactsPath)} " +
            IntermediateOutputArgument);
    }

    private static string ValidateVerificationArtifactsPath(
        string repositoryRoot)
    {
        string localStatePath = Path.Combine(
            repositoryRoot,
            ".local-ai");
        string artifactsPath = Path.Combine(
            localStatePath,
            "verification");

        RejectInvalidOutputPathEntry(
            localStatePath,
            "Local-AI state directory");
        RejectInvalidOutputPathEntry(
            artifactsPath,
            "Verification artifacts directory");

        return artifactsPath;
    }

    private static void RejectInvalidOutputPathEntry(
        string path,
        string description)
    {
        if (File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException(
                $"{description} must be a directory.");
        }

        if (Directory.Exists(path))
        {
            RejectReparsePoint(path, description);
        }
    }

    private static string ValidateSolutionPath(
        string repositoryRoot,
        string? solutionRelativePath)
    {
        if (string.IsNullOrWhiteSpace(solutionRelativePath))
        {
            throw new InvalidOperationException(
                "Exactly one solution file must be detected before " +
                "running .NET verification.");
        }

        if (Path.IsPathRooted(solutionRelativePath))
        {
            throw new InvalidOperationException(
                "The solution path must be relative to the repository.");
        }

        string fullSolutionPath = Path.GetFullPath(
            Path.Combine(repositoryRoot, solutionRelativePath));

        string rootPrefix = repositoryRoot.EndsWith(
            Path.DirectorySeparatorChar)
                ? repositoryRoot
                : repositoryRoot + Path.DirectorySeparatorChar;

        if (!fullSolutionPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The solution path escapes the selected repository.");
        }

        string extension = Path.GetExtension(fullSolutionPath);

        if (!extension.Equals(
                ".sln",
                StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(
                ".slnx",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only .sln and .slnx solution files are allowed.");
        }

        if (!File.Exists(fullSolutionPath))
        {
            throw new FileNotFoundException(
                "The detected solution file no longer exists.",
                fullSolutionPath);
        }

        RejectPathContainingReparsePoint(
            repositoryRoot,
            fullSolutionPath,
            "Solution path");

        return fullSolutionPath;
    }

    private static void RejectReparsePoint(
        string path,
        string description)
    {
        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"{description} could not be validated.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                $"{description} could not be validated.",
                exception);
        }

        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                $"{description} cannot be a linked or reparse-point path.");
        }
    }

    private static void RejectPathContainingReparsePoint(
        string repositoryRoot,
        string fullPath,
        string description)
    {
        FileSystemInfo? current = new FileInfo(fullPath);

        while (current is not null)
        {
            RejectReparsePoint(current.FullName, description);

            if (current.FullName.Equals(
                    repositoryRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }

        throw new InvalidOperationException(
            $"{description} could not be traced to the repository root.");
    }

    private static string QuoteForDisplay(string value)
    {
        return value.Contains(' ')
            ? $"\"{value}\""
            : value;
    }
}
