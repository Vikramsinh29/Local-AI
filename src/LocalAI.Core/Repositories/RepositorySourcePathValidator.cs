using System.Text.RegularExpressions;

namespace LocalAI.Core.Repositories;

public static partial class RepositorySourcePathValidator
{
    public const int MaximumRelativePathCharacters = 500;

    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".local-ai", ".vs", "artifacts", "bin", "obj",
            "node_modules"
        };

    private static readonly HashSet<string> SecretFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".env", "credentials.json", "secrets.json",
            "service-account.json"
        };

    private static readonly HashSet<string> SecretExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".key", ".p12", ".pem", ".pfx"
        };

    private static readonly HashSet<string> ReservedWindowsNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5",
            "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
            "LPT6", "LPT7", "LPT8", "LPT9"
        };

    public static string? Validate(
        string repositoryRoot,
        string relativePath,
        out string normalizedPath,
        out string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        normalizedPath = relativePath.Replace(
            '/',
            Path.DirectorySeparatorChar);
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Contains("<<<", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalizedPath) ||
            DrivePathRegex().IsMatch(relativePath))
        {
            return "Every proposed file path must be repository-relative.";
        }

        if (relativePath.Length > MaximumRelativePathCharacters)
        {
            return $"The proposed path '{relativePath}' is invalid.";
        }

        string[] segments = normalizedPath.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 ||
            segments.Any(segment =>
                segment is "." or ".." ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                IsReservedWindowsName(segment) ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return $"The proposed path '{relativePath}' is invalid.";
        }

        if (segments.Any(ExcludedDirectories.Contains) ||
            segments.Any(IsSecretPath))
        {
            return $"The proposed path '{relativePath}' is protected.";
        }

        try
        {
            fullPath = Path.GetFullPath(
                Path.Combine(repositoryRoot, normalizedPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return $"The proposed path '{relativePath}' is invalid.";
        }

        string rootPrefix = repositoryRoot.EndsWith(
            Path.DirectorySeparatorChar)
                ? repositoryRoot
                : repositoryRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"The proposed path '{relativePath}' escapes the repository.";
        }

        if (ContainsExistingReparsePoint(repositoryRoot, fullPath))
        {
            return $"The proposed path '{relativePath}' crosses a linked path.";
        }

        normalizedPath = Path.GetRelativePath(repositoryRoot, fullPath);
        return null;
    }

    public static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path)
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

    private static bool IsSecretPath(string fileName)
    {
        return SecretFileNames.Contains(fileName) ||
               fileName.StartsWith(
                   ".env.",
                   StringComparison.OrdinalIgnoreCase) ||
               SecretExtensions.Contains(Path.GetExtension(fileName));
    }

    private static bool IsReservedWindowsName(string segment)
    {
        int dotIndex = segment.IndexOf('.');
        string baseName = dotIndex >= 0
            ? segment[..dotIndex]
            : segment;
        return ReservedWindowsNames.Contains(baseName);
    }

    private static bool ContainsExistingReparsePoint(
        string repositoryRoot,
        string fullPath)
    {
        FileSystemInfo? current = new FileInfo(fullPath);

        while (current is not null)
        {
            if ((File.Exists(current.FullName) ||
                 Directory.Exists(current.FullName)) &&
                IsReparsePoint(current.FullName))
            {
                return true;
            }

            if (current.FullName.Equals(
                    repositoryRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }

        return true;
    }

    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex DrivePathRegex();
}
