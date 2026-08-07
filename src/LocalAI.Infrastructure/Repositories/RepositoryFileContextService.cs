using System.Text;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;

namespace LocalAI.Infrastructure.Repositories;

public sealed class RepositoryFileContextService :
    IRepositoryFileContextService
{
    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".local-ai", ".vs", "bin", "obj", "node_modules",
            "artifacts"
        };

    private static readonly HashSet<string> BinaryExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".7z", ".avi", ".bmp", ".class", ".dll", ".doc", ".docx",
            ".exe", ".gif", ".gz", ".ico", ".jar", ".jpeg", ".jpg",
            ".mov", ".mp3", ".mp4", ".pdf", ".png", ".pdb", ".so",
            ".tar", ".wav", ".webp", ".xls", ".xlsx", ".zip"
        };

    private static readonly string[] GeneratedSuffixes =
    [
        ".designer.cs", ".generated.cs", ".g.cs", ".g.i.cs", ".min.css", ".min.js"
    ];

    private static readonly HashSet<string> SecretFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "appsettings.Development.json",
            "credentials.json",
            "id_dsa",
            "id_ecdsa",
            "id_ed25519",
            "id_rsa",
            "secrets.json",
            "service-account.json"
        };

    private static readonly HashSet<string> SecretExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".key", ".p12", ".pem", ".pfx"
        };

    public long MaximumFileBytes => 128 * 1024;

    public long MaximumTotalBytes => 512 * 1024;

    public async Task<RepositoryContextReadResult> ReadAsync(
        string repositoryRoot,
        string relativePath,
        long currentTotalBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootPrefix = root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryContextReadResult.Failure(
                "The selected file is outside the repository.");
        }

        string normalizedRelativePath = Path.GetRelativePath(root, fullPath);
        string[] segments = normalizedRelativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Take(Math.Max(0, segments.Length - 1))
            .Any(ExcludedDirectories.Contains) ||
            GeneratedSuffixes.Any(suffix =>
                fullPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return RepositoryContextReadResult.Failure(
                "Generated files cannot be added to context.");
        }

        if (BinaryExtensions.Contains(Path.GetExtension(fullPath)))
        {
            return RepositoryContextReadResult.Failure(
                "Binary files cannot be added to context.");
        }

        if (IsSecretFile(fullPath))
        {
            return RepositoryContextReadResult.Failure(
                "Files that may contain secrets cannot be added to context.");
        }

        if (!File.Exists(fullPath))
        {
            return RepositoryContextReadResult.Failure(
                "The selected file no longer exists.");
        }

        if (ContainsReparsePoint(root, fullPath))
        {
            return RepositoryContextReadResult.Failure(
                "Linked files and folders cannot be added to context.");
        }

        FileInfo fileInfo = new(fullPath);

        if (fileInfo.Length > MaximumFileBytes)
        {
            return RepositoryContextReadResult.Failure(
                $"Files larger than {MaximumFileBytes / 1024} KB cannot be added.");
        }

        if (currentTotalBytes + fileInfo.Length > MaximumTotalBytes)
        {
            return RepositoryContextReadResult.Failure(
                $"Context cannot exceed {MaximumTotalBytes / 1024} KB.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);

        if (bytes.LongLength > MaximumFileBytes ||
            currentTotalBytes + bytes.LongLength > MaximumTotalBytes)
        {
            return RepositoryContextReadResult.Failure(
                "The file changed while it was being read and now exceeds a context limit.");
        }

        if (bytes.Contains((byte)0))
        {
            return RepositoryContextReadResult.Failure(
                "Binary files cannot be added to context.");
        }

        string content;

        try
        {
            content = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return RepositoryContextReadResult.Failure(
                "The file is not valid UTF-8 text.");
        }

        return RepositoryContextReadResult.Success(
            new RepositoryContextFile(
                normalizedRelativePath,
                content,
                bytes.LongLength));
    }

    private static bool IsSecretFile(string fullPath)
    {
        string fileName = Path.GetFileName(fullPath);

        return SecretFileNames.Contains(fileName) ||
               SecretExtensions.Contains(Path.GetExtension(fileName)) ||
               fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsReparsePoint(
        string root,
        string fullPath)
    {
        FileSystemInfo? current = new FileInfo(fullPath);

        while (current is not null)
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            if (current.FullName.Equals(
                root,
                StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }

        return false;
    }
}
