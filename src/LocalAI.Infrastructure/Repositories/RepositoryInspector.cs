using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;

namespace LocalAI.Infrastructure.Repositories;

public sealed class RepositoryInspector : IRepositoryInspector
{
    private const int MaximumTreeEntries = 10_000;

    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".local-ai",
            ".vs",
            "bin",
            "obj",
            "node_modules",
            "artifacts"
        };

    public Task<RepositoryInfo> InspectAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        string fullPath = Path.GetFullPath(repositoryPath);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Repository folder does not exist: {fullPath}");
        }

        return Task.Run(
            () => InspectRepository(fullPath, cancellationToken),
            cancellationToken);
    }

    private static RepositoryInfo InspectRepository(
        string rootPath,
        CancellationToken cancellationToken)
    {
        List<string> solutionFiles = [];
        List<string> projectFiles = [];

        int entryCount = 0;

        IReadOnlyList<RepositoryTreeNode> rootEntries =
            BuildChildren(
                rootPath,
                rootPath,
                solutionFiles,
                projectFiles,
                ref entryCount,
                cancellationToken);

        solutionFiles.Sort(StringComparer.OrdinalIgnoreCase);
        projectFiles.Sort(StringComparer.OrdinalIgnoreCase);

        string gitPath = Path.Combine(rootPath, ".git");

        return new RepositoryInfo(
            rootPath,
            Directory.Exists(gitPath) || File.Exists(gitPath),
            solutionFiles,
            projectFiles,
            rootEntries);
    }

    private static IReadOnlyList<RepositoryTreeNode> BuildChildren(
        string currentDirectory,
        string rootPath,
        List<string> solutionFiles,
        List<string> projectFiles,
        ref int entryCount,
        CancellationToken cancellationToken)
    {
        List<RepositoryTreeNode> entries = [];

        foreach (string directory in
                 GetDirectoriesSafely(currentDirectory)
                     .OrderBy(
                         Path.GetFileName,
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entryCount >= MaximumTreeEntries)
            {
                break;
            }

            string directoryName = Path.GetFileName(directory);

            if (ExcludedDirectories.Contains(directoryName) ||
                IsReparsePoint(directory))
            {
                continue;
            }

            entryCount++;

            IReadOnlyList<RepositoryTreeNode> children =
                BuildChildren(
                    directory,
                    rootPath,
                    solutionFiles,
                    projectFiles,
                    ref entryCount,
                    cancellationToken);

            entries.Add(
                new RepositoryTreeNode(
                    directoryName,
                    Path.GetRelativePath(rootPath, directory),
                    IsDirectory: true,
                    SizeBytes: null,
                    LastModifiedUtc:
                        GetLastModifiedUtcSafely(directory),
                    Children: children));
        }

        foreach (string file in
                 GetFilesSafely(currentDirectory)
                     .OrderBy(
                         Path.GetFileName,
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entryCount >= MaximumTreeEntries)
            {
                break;
            }

            entryCount++;

            string relativePath =
                Path.GetRelativePath(rootPath, file);

            string extension = Path.GetExtension(file);

            if (extension.Equals(
                    ".sln",
                    StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(
                    ".slnx",
                    StringComparison.OrdinalIgnoreCase))
            {
                solutionFiles.Add(relativePath);
            }
            else if (extension.Equals(
                         ".csproj",
                         StringComparison.OrdinalIgnoreCase))
            {
                projectFiles.Add(relativePath);
            }

            entries.Add(
                new RepositoryTreeNode(
                    Path.GetFileName(file),
                    relativePath,
                    IsDirectory: false,
                    SizeBytes: GetFileSizeSafely(file),
                    LastModifiedUtc:
                        GetLastModifiedUtcSafely(file),
                    Children: []));
        }

        return entries;
    }

    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return File.GetAttributes(directory)
                .HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static long? GetFileSizeSafely(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTime? GetLastModifiedUtcSafely(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string[] GetFilesSafely(string directory)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] GetDirectoriesSafely(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
