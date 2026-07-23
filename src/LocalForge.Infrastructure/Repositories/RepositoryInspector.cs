using LocalForge.Core.Interfaces;
using LocalForge.Core.Models;

namespace LocalForge.Infrastructure.Repositories;

public sealed class RepositoryInspector : IRepositoryInspector
{
    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
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

        Stack<string> pendingDirectories = new();
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentDirectory = pendingDirectories.Pop();

            foreach (string file in GetFilesSafely(currentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string extension = Path.GetExtension(file);
                string relativePath = Path.GetRelativePath(rootPath, file);

                if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
                {
                    solutionFiles.Add(relativePath);
                }
                else if (extension.Equals(
                             ".csproj",
                             StringComparison.OrdinalIgnoreCase))
                {
                    projectFiles.Add(relativePath);
                }
            }

            foreach (string directory in
                     GetDirectoriesSafely(currentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string directoryName = Path.GetFileName(directory);

                if (ExcludedDirectories.Contains(directoryName))
                {
                    continue;
                }

                try
                {
                    FileAttributes attributes =
                        File.GetAttributes(directory);

                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                pendingDirectories.Push(directory);
            }
        }

        solutionFiles.Sort(StringComparer.OrdinalIgnoreCase);
        projectFiles.Sort(StringComparer.OrdinalIgnoreCase);

        string gitPath = Path.Combine(rootPath, ".git");

        return new RepositoryInfo(
            rootPath,
            Directory.Exists(gitPath) || File.Exists(gitPath),
            solutionFiles,
            projectFiles);
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
