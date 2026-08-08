using System.Security.Cryptography;
using System.Text;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Infrastructure.Repositories;

public sealed class RepositoryPatchService : IRepositoryPatchService
{
    private const long MaximumSourceFileBytes = 1_048_576;

    public async Task<PatchApplyResult> ApplyAsync(
        string repositoryRoot,
        ProposedPatchPreview preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();

        if (preview.Files.Count != 1)
        {
            return PatchApplyResult.Failure(
                "Sprint 2.1 can apply exactly one reviewed file at a time.");
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(repositoryRoot));

        if (!Directory.Exists(root) ||
            RepositorySourcePathValidator.IsReparsePoint(root))
        {
            return PatchApplyResult.Failure(
                "The selected repository root is unavailable or linked.");
        }

        string gitPath = Path.Combine(root, ".git");

        if (!Directory.Exists(gitPath) ||
            RepositorySourcePathValidator.IsReparsePoint(gitPath))
        {
            return PatchApplyResult.Failure(
                "Approved patch apply requires a local Git repository.");
        }

        ProposedPatchFile file = preview.Files[0];
        string? pathError = RepositorySourcePathValidator.Validate(
            root,
            file.RelativePath,
            out string normalizedPath,
            out string fullPath);

        if (pathError is not null)
        {
            return PatchApplyResult.Failure(pathError);
        }

        try
        {
            if (!File.Exists(fullPath) ||
                new FileInfo(fullPath).Length > MaximumSourceFileBytes)
            {
                return PatchApplyResult.Failure(
                    $"The reviewed source file '{normalizedPath}' is " +
                    "unavailable or too large.");
            }

            byte[] sourceBytes = await File.ReadAllBytesAsync(
                fullPath,
                cancellationToken);
            string currentHash = Convert.ToHexString(
                SHA256.HashData(sourceBytes));

            if (!currentHash.Equals(
                    file.SourceSha256,
                    StringComparison.Ordinal))
            {
                return PatchApplyResult.Failure(
                    $"The reviewed source file '{normalizedPath}' changed " +
                    "after the preview was created.");
            }

            DecodedTextFile decoded = Decode(sourceBytes);
            string normalizedSource = NormalizeLineEndings(decoded.Text);
            int matchIndex = normalizedSource.IndexOf(
                file.OriginalText,
                StringComparison.Ordinal);

            if (matchIndex < 0 ||
                normalizedSource.IndexOf(
                    file.OriginalText,
                    matchIndex + file.OriginalText.Length,
                    StringComparison.Ordinal) >= 0)
            {
                return PatchApplyResult.Failure(
                    $"The reviewed ORIGINAL text for '{normalizedPath}' " +
                    "is no longer one exact source fragment.");
            }

            string updatedNormalized = normalizedSource
                .Remove(matchIndex, file.OriginalText.Length)
                .Insert(matchIndex, file.ReplacementText);
            string updatedText = RestoreLineEndings(
                updatedNormalized,
                decoded.LineEnding);
            byte[] encoded = Encode(updatedText, decoded);

            cancellationToken.ThrowIfCancellationRequested();
            await ReplaceAtomicallyAsync(
                root,
                fullPath,
                encoded,
                cancellationToken);

            PatchRollbackRecord rollbackRecord = new(
                root,
                normalizedPath,
                sourceBytes,
                encoded);

            return PatchApplyResult.Success(
                normalizedPath,
                rollbackRecord);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            DecoderFallbackException or
            EncoderFallbackException)
        {
            return PatchApplyResult.Failure(
                $"The reviewed patch could not be applied safely: " +
                exception.Message);
        }
    }

    public async Task<PatchRollbackResult> RollbackAsync(
        string repositoryRoot,
        PatchRollbackRecord rollbackRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(rollbackRecord);
        cancellationToken.ThrowIfCancellationRequested();

        string root;
        string recordedRoot;

        try
        {
            root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(repositoryRoot));
            recordedRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(rollbackRecord.RepositoryRoot));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return PatchRollbackResult.Failure(
                "The rollback repository path is invalid.");
        }

        if (!root.Equals(
                recordedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return PatchRollbackResult.Failure(
                "The selected repository changed after the patch was applied.");
        }

        if (!Directory.Exists(root) ||
            RepositorySourcePathValidator.IsReparsePoint(root))
        {
            return PatchRollbackResult.Failure(
                "The selected repository root is unavailable or linked.");
        }

        string gitPath = Path.Combine(root, ".git");

        if (!Directory.Exists(gitPath) ||
            RepositorySourcePathValidator.IsReparsePoint(gitPath))
        {
            return PatchRollbackResult.Failure(
                "Approved rollback requires the original local Git repository.");
        }

        string? pathError = RepositorySourcePathValidator.Validate(
            root,
            rollbackRecord.RelativePath,
            out string normalizedPath,
            out string fullPath);

        if (pathError is not null)
        {
            return PatchRollbackResult.Failure(pathError);
        }

        if (!normalizedPath.Equals(
                rollbackRecord.RelativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return PatchRollbackResult.Failure(
                "The rollback source path no longer matches the applied patch.");
        }

        try
        {
            if (!File.Exists(fullPath) ||
                new FileInfo(fullPath).Length !=
                rollbackRecord.AppliedBytes.Length)
            {
                return PatchRollbackResult.Failure(
                    $"The applied source file '{normalizedPath}' is " +
                    "unavailable or was externally changed.");
            }

            byte[] currentBytes = await File.ReadAllBytesAsync(
                fullPath,
                cancellationToken);

            if (!currentBytes.AsSpan().SequenceEqual(
                    rollbackRecord.AppliedBytes.Span))
            {
                return PatchRollbackResult.Failure(
                    $"The applied source file '{normalizedPath}' was " +
                    "externally changed after apply.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await ReplaceAtomicallyAsync(
                root,
                fullPath,
                rollbackRecord.OriginalBytes.ToArray(),
                cancellationToken);

            return PatchRollbackResult.Success(normalizedPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            return PatchRollbackResult.Failure(
                "The applied patch could not be rolled back safely: " +
                exception.Message);
        }
    }

    private static async Task ReplaceAtomicallyAsync(
        string repositoryRoot,
        string destinationPath,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string stateDirectory = Path.Combine(
            repositoryRoot,
            ".local-ai");
        string applyDirectory = Path.Combine(
            stateDirectory,
            "apply");

        ValidateStateDirectory(stateDirectory, "Local-AI state directory");
        Directory.CreateDirectory(stateDirectory);
        ValidateStateDirectory(stateDirectory, "Local-AI state directory");

        ValidateStateDirectory(applyDirectory, "Patch staging directory");
        Directory.CreateDirectory(applyDirectory);
        ValidateStateDirectory(applyDirectory, "Patch staging directory");

        string temporaryPath = Path.Combine(
            applyDirectory,
            $"patch-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                options: FileOptions.Asynchronous |
                         FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Replace(
                temporaryPath,
                destinationPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Cleanup must not hide the apply result.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup must not hide the apply result.
            }
        }
    }

    private static void ValidateStateDirectory(
        string path,
        string description)
    {
        if (File.Exists(path) && !Directory.Exists(path))
        {
            throw new IOException($"{description} must be a directory.");
        }

        if (Directory.Exists(path) &&
            RepositorySourcePathValidator.IsReparsePoint(path))
        {
            throw new IOException($"{description} must not be linked.");
        }
    }

    private static DecodedTextFile Decode(byte[] bytes)
    {
        (Encoding encoding, int preambleLength) = DetectEncoding(bytes);
        string text = encoding.GetString(bytes, preambleLength,
            bytes.Length - preambleLength);
        return new DecodedTextFile(
            text,
            encoding,
            bytes[..preambleLength],
            DetectLineEnding(text));
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(
        byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(
                new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            return (new UTF32Encoding(true, true, true), 4);
        }

        if (bytes.AsSpan().StartsWith(
                new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            return (new UTF32Encoding(false, true, true), 4);
        }

        if (bytes.AsSpan().StartsWith(
                new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return (new UTF8Encoding(true, true), 3);
        }

        if (bytes.AsSpan().StartsWith(
                new byte[] { 0xFE, 0xFF }))
        {
            return (new UnicodeEncoding(true, true, true), 2);
        }

        if (bytes.AsSpan().StartsWith(
                new byte[] { 0xFF, 0xFE }))
        {
            return (new UnicodeEncoding(false, true, true), 2);
        }

        return (new UTF8Encoding(false, true), 0);
    }

    private static byte[] Encode(
        string text,
        DecodedTextFile decoded)
    {
        byte[] content = decoded.Encoding.GetBytes(text);

        if (decoded.Preamble.Length == 0)
        {
            return content;
        }

        byte[] result = new byte[decoded.Preamble.Length + content.Length];
        decoded.Preamble.CopyTo(result, 0);
        content.CopyTo(result, decoded.Preamble.Length);
        return result;
    }

    private static string DetectLineEnding(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : text.Contains('\n')
                ? "\n"
                : text.Contains('\r')
                    ? "\r"
                    : Environment.NewLine;
    }

    private static string RestoreLineEndings(
        string normalized,
        string lineEnding)
    {
        return lineEnding == "\n"
            ? normalized
            : normalized.Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private sealed record DecodedTextFile(
        string Text,
        Encoding Encoding,
        byte[] Preamble,
        string LineEnding);
}

