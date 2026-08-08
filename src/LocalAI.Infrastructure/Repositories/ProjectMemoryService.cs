using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Infrastructure.Repositories;

public sealed partial class ProjectMemoryService : IProjectMemoryService
{
    public const int MaximumEntriesLimit = 16;
    public const int MaximumEntryBytesLimit = 1024;
    public const int MaximumCombinedBytesLimit = 8 * 1024;
    public const int MaximumCombinedTokensLimit = 2000;
    public const int MaximumTitleCharacters = 120;

    private const int StoreVersion = 1;
    private const int MaximumStoreFileBytes = 64 * 1024;
    private const string StoreFileName = "memory.json";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    private readonly string _storageRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int MaximumEntries => MaximumEntriesLimit;

    public int MaximumEntryBytes => MaximumEntryBytesLimit;

    public int MaximumCombinedBytes => MaximumCombinedBytesLimit;

    public int MaximumCombinedTokens => MaximumCombinedTokensLimit;

    public ProjectMemoryService(string? storageRoot = null)
    {
        string selectedRoot = string.IsNullOrWhiteSpace(storageRoot)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Local-AI",
                "ProjectMemory")
            : storageRoot;

        _storageRoot = Path.GetFullPath(selectedRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    }

    public async Task<ProjectMemoryLoadResult> LoadAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        string storagePath;

        try
        {
            storagePath = GetStoragePath(repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            DirectoryNotFoundException or
            IOException or
            UnauthorizedAccessException)
        {
            return ProjectMemoryLoadResult.Failure(
                Path.Combine(_storageRoot, StoreFileName),
                exception.Message);
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            ValidateExistingStoragePath(storagePath);
            StoreReadResult read = await ReadStoreAsync(
                storagePath,
                cancellationToken);

            return read.Error is null
                ? ProjectMemoryLoadResult.Success(
                    read.Entries,
                    storagePath)
                : ProjectMemoryLoadResult.Failure(
                    storagePath,
                    read.Error);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            DecoderFallbackException)
        {
            return ProjectMemoryLoadResult.Failure(
                storagePath,
                $"Project memory could not be loaded safely: " +
                exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ProjectMemoryMutationResult> CreateAsync(
        string repositoryRoot,
        ProjectMemoryCategory category,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            repositoryRoot,
            async (entries, token) =>
            {
                if (entries.Count >= MaximumEntriesLimit)
                {
                    return Mutation.Failure(
                        $"Project memory is limited to " +
                        $"{MaximumEntriesLimit} entries.");
                }

                string? validationError = TryCreateEntry(
                    Guid.NewGuid(),
                    category,
                    title,
                    content,
                    DateTimeOffset.UtcNow,
                    out ProjectMemoryEntry? entry);

                if (validationError is not null)
                {
                    return Mutation.Failure(validationError);
                }

                List<ProjectMemoryEntry> updated = [.. entries, entry!];
                string? budgetError = ValidateCombinedBudget(updated);

                if (budgetError is not null)
                {
                    return Mutation.Failure(budgetError);
                }

                await Task.CompletedTask;
                return Mutation.Success(updated, entry);
            },
            cancellationToken);
    }

    public Task<ProjectMemoryMutationResult> UpdateAsync(
        string repositoryRoot,
        Guid entryId,
        ProjectMemoryCategory category,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (entryId == Guid.Empty)
        {
            return Task.FromResult(
                ProjectMemoryMutationResult.Failure(
                    "Select a valid project memory entry to update."));
        }

        return MutateAsync(
            repositoryRoot,
            async (entries, token) =>
            {
                int index = entries.FindIndex(entry => entry.Id == entryId);

                if (index < 0)
                {
                    return Mutation.Failure(
                        "The selected project memory entry no longer exists.");
                }

                string? validationError = TryCreateEntry(
                    entryId,
                    category,
                    title,
                    content,
                    DateTimeOffset.UtcNow,
                    out ProjectMemoryEntry? replacement);

                if (validationError is not null)
                {
                    return Mutation.Failure(validationError);
                }

                List<ProjectMemoryEntry> updated = [.. entries];
                updated[index] = replacement!;
                string? budgetError = ValidateCombinedBudget(updated);

                if (budgetError is not null)
                {
                    return Mutation.Failure(budgetError);
                }

                await Task.CompletedTask;
                return Mutation.Success(updated, replacement);
            },
            cancellationToken);
    }

    public Task<ProjectMemoryMutationResult> DeleteAsync(
        string repositoryRoot,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId == Guid.Empty)
        {
            return Task.FromResult(
                ProjectMemoryMutationResult.Failure(
                    "Select a valid project memory entry to delete."));
        }

        return MutateAsync(
            repositoryRoot,
            async (entries, token) =>
            {
                ProjectMemoryEntry? existing = entries.FirstOrDefault(
                    entry => entry.Id == entryId);

                if (existing is null)
                {
                    return Mutation.Failure(
                        "The selected project memory entry no longer exists.");
                }

                List<ProjectMemoryEntry> updated = entries
                    .Where(entry => entry.Id != entryId)
                    .ToList();
                await Task.CompletedTask;
                return Mutation.Success(updated, existing);
            },
            cancellationToken);
    }

    private async Task<ProjectMemoryMutationResult> MutateAsync(
        string repositoryRoot,
        Func<
            List<ProjectMemoryEntry>,
            CancellationToken,
            Task<Mutation>> mutation,
        CancellationToken cancellationToken)
    {
        string storagePath;

        try
        {
            storagePath = GetStoragePath(repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            DirectoryNotFoundException or
            IOException or
            UnauthorizedAccessException)
        {
            return ProjectMemoryMutationResult.Failure(exception.Message);
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            ValidateExistingStoragePath(storagePath);
            StoreReadResult read = await ReadStoreAsync(
                storagePath,
                cancellationToken);

            if (read.Error is not null)
            {
                return ProjectMemoryMutationResult.Failure(
                    "Project memory was not changed because the existing " +
                    $"store is invalid: {read.Error}");
            }

            Mutation result = await mutation(
                [.. read.Entries],
                cancellationToken);

            if (result.Error is not null)
            {
                return ProjectMemoryMutationResult.Failure(result.Error);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await WriteStoreAsync(
                storagePath,
                result.Entries,
                cancellationToken);

            return ProjectMemoryMutationResult.Success(
                Order(result.Entries),
                result.ChangedEntry);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            DecoderFallbackException)
        {
            return ProjectMemoryMutationResult.Failure(
                $"Project memory was not changed: {exception.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetStoragePath(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        string canonicalRoot = Path.GetFullPath(repositoryRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(canonicalRoot))
        {
            throw new DirectoryNotFoundException(
                $"Repository folder does not exist: {canonicalRoot}");
        }

        if (RepositorySourcePathValidator.IsReparsePoint(canonicalRoot))
        {
            throw new IOException(
                "Project memory is unavailable because the selected " +
                "repository root is linked.");
        }

        string identityInput = OperatingSystem.IsWindows()
            ? canonicalRoot.ToUpperInvariant()
            : canonicalRoot;
        string repositoryId = Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(identityInput)))
            .ToLowerInvariant();

        return Path.Combine(
            _storageRoot,
            repositoryId,
            StoreFileName);
    }

    private static async Task<StoreReadResult> ReadStoreAsync(
        string storagePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(storagePath))
        {
            return StoreReadResult.Success([]);
        }

        FileInfo file = new(storagePath);

        if (file.Length > MaximumStoreFileBytes)
        {
            return StoreReadResult.Failure(
                "memory.json exceeds the bounded store size.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(
            storagePath,
            cancellationToken);

        if (bytes.Length > MaximumStoreFileBytes)
        {
            return StoreReadResult.Failure(
                "memory.json changed while loading and now exceeds the " +
                "bounded store size.");
        }

        if (bytes.Contains((byte)0))
        {
            return StoreReadResult.Failure(
                "memory.json contains binary data.");
        }

        string json;

        try
        {
            int contentOffset = bytes.Length >= 3 &&
                bytes[0] == 0xEF &&
                bytes[1] == 0xBB &&
                bytes[2] == 0xBF
                ? 3
                : 0;
            json = StrictUtf8.GetString(
                bytes,
                contentOffset,
                bytes.Length - contentOffset);
        }
        catch (DecoderFallbackException)
        {
            return StoreReadResult.Failure(
                "memory.json is not valid UTF-8 text.");
        }

        StoreDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<StoreDocument>(
                json,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            return StoreReadResult.Failure(
                $"memory.json is malformed: {exception.Message}");
        }

        if (document is null || document.Version != StoreVersion)
        {
            return StoreReadResult.Failure(
                "memory.json has an unsupported or missing schema version.");
        }

        if (document.Entries is null)
        {
            return StoreReadResult.Failure(
                "memory.json does not contain a valid entries array.");
        }

        if (document.Entries.Count > MaximumEntriesLimit)
        {
            return StoreReadResult.Failure(
                $"memory.json contains more than " +
                $"{MaximumEntriesLimit} entries.");
        }

        List<ProjectMemoryEntry> entries = [];
        HashSet<Guid> ids = [];

        foreach (StoredEntry stored in document.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stored.Id == Guid.Empty || !ids.Add(stored.Id))
            {
                return StoreReadResult.Failure(
                    "memory.json contains a missing or duplicate entry ID.");
            }

            string? error = TryCreateEntry(
                stored.Id,
                stored.Category,
                stored.Title,
                stored.Content,
                stored.UpdatedAtUtc,
                out ProjectMemoryEntry? entry);

            if (error is not null)
            {
                return StoreReadResult.Failure(
                    $"memory.json contains an invalid entry: {error}");
            }

            entries.Add(entry!);
        }

        string? budgetError = ValidateCombinedBudget(entries);
        return budgetError is null
            ? StoreReadResult.Success(Order(entries))
            : StoreReadResult.Failure(
                $"memory.json is over budget: {budgetError}");
    }

    private async Task WriteStoreAsync(
        string storagePath,
        IReadOnlyList<ProjectMemoryEntry> entries,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(storagePath)!;
        EnsureSafeStorageDirectory(directory);

        StoreDocument document = new(
            StoreVersion,
            entries.Select(entry => new StoredEntry(
                entry.Id,
                entry.Category,
                entry.Title,
                entry.Content,
                entry.UpdatedAtUtc)).ToList());
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            document,
            JsonOptions);

        if (bytes.Length > MaximumStoreFileBytes)
        {
            throw new IOException(
                "The serialized project memory exceeds the bounded store size.");
        }

        string temporaryPath = Path.Combine(
            directory,
            $".{StoreFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(storagePath))
            {
                File.Replace(
                    temporaryPath,
                    storagePath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, storagePath);
            }
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
            catch
            {
                // Temporary cleanup must not hide the original result.
            }
        }
    }

    private void EnsureSafeStorageDirectory(string projectDirectory)
    {
        Directory.CreateDirectory(_storageRoot);

        if (RepositorySourcePathValidator.IsReparsePoint(_storageRoot))
        {
            throw new IOException(
                "The Local-AI project memory root is linked and was not used.");
        }

        Directory.CreateDirectory(projectDirectory);

        if (RepositorySourcePathValidator.IsReparsePoint(projectDirectory))
        {
            throw new IOException(
                "The repository memory folder is linked and was not used.");
        }

        string storagePath = Path.Combine(projectDirectory, StoreFileName);

        if (File.Exists(storagePath) &&
            RepositorySourcePathValidator.IsReparsePoint(storagePath))
        {
            throw new IOException(
                "The project memory file is linked and was not used.");
        }
    }

    private void ValidateExistingStoragePath(string storagePath)
    {
        if (Directory.Exists(_storageRoot) &&
            RepositorySourcePathValidator.IsReparsePoint(_storageRoot))
        {
            throw new IOException(
                "The Local-AI project memory root is linked and was not used.");
        }

        string projectDirectory = Path.GetDirectoryName(storagePath)!;

        if (Directory.Exists(projectDirectory) &&
            RepositorySourcePathValidator.IsReparsePoint(projectDirectory))
        {
            throw new IOException(
                "The repository memory folder is linked and was not used.");
        }

        if (File.Exists(storagePath) &&
            RepositorySourcePathValidator.IsReparsePoint(storagePath))
        {
            throw new IOException(
                "The project memory file is linked and was not used.");
        }
    }

    private static string? TryCreateEntry(
        Guid id,
        ProjectMemoryCategory category,
        string? title,
        string? content,
        DateTimeOffset updatedAtUtc,
        out ProjectMemoryEntry? entry)
    {
        entry = null;

        if (id == Guid.Empty)
        {
            return "The entry ID is missing.";
        }

        if (!Enum.IsDefined(category))
        {
            return "Select one supported project memory category.";
        }

        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(content))
        {
            return "Project memory title and content are both required.";
        }

        if (title.Length > MaximumTitleCharacters ||
            title.Contains('\r') ||
            title.Contains('\n'))
        {
            return $"Project memory titles must be one line and no more than " +
                   $"{MaximumTitleCharacters} characters.";
        }

        if (ContainsUnsupportedControlCharacter(title) ||
            ContainsUnsupportedControlCharacter(content))
        {
            return "Project memory must contain plain UTF-8 text only.";
        }

        if (ContainsSensitiveMaterial(title) ||
            ContainsSensitiveMaterial(content))
        {
            return "Project memory cannot store credentials, tokens, " +
                   "secrets, private keys, connection strings, or " +
                   "environment-variable values.";
        }

        long sizeBytes;

        try
        {
            sizeBytes = StrictUtf8.GetByteCount(title) +
                        StrictUtf8.GetByteCount(content) + 1;
        }
        catch (EncoderFallbackException)
        {
            return "Project memory is not valid UTF-8 text.";
        }

        if (sizeBytes > MaximumEntryBytesLimit)
        {
            return $"Each complete project memory entry is limited to " +
                   $"{MaximumEntryBytesLimit:N0} UTF-8 bytes.";
        }

        if (updatedAtUtc == default)
        {
            return "The project memory updated time is missing.";
        }

        entry = new ProjectMemoryEntry(
            id,
            category,
            title,
            content,
            sizeBytes,
            Math.Max(1, (int)((sizeBytes + 3) / 4)),
            updatedAtUtc.ToUniversalTime());
        return null;
    }

    private static string? ValidateCombinedBudget(
        IReadOnlyList<ProjectMemoryEntry> entries)
    {
        long totalBytes = entries.Sum(entry => entry.SizeBytes);

        if (totalBytes > MaximumCombinedBytesLimit)
        {
            return $"Combined project memory is limited to " +
                   $"{MaximumCombinedBytesLimit:N0} UTF-8 bytes.";
        }

        int totalTokens = entries.Sum(entry => entry.EstimatedTokens);
        return totalTokens <= MaximumCombinedTokensLimit
            ? null
            : $"Combined project memory is limited to approximately " +
              $"{MaximumCombinedTokensLimit:N0} estimated tokens.";
    }

    private static bool ContainsUnsupportedControlCharacter(string value)
    {
        return value.Any(character =>
            char.IsControl(character) &&
            character is not '\r' and not '\n' and not '\t');
    }

    private static bool ContainsSensitiveMaterial(string value)
    {
        return value.Contains(
                   "-----BEGIN PRIVATE KEY-----",
                   StringComparison.OrdinalIgnoreCase) ||
               value.Contains(
                   "-----BEGIN RSA PRIVATE KEY-----",
                   StringComparison.OrdinalIgnoreCase) ||
               SensitiveAssignmentRegex().IsMatch(value) ||
               EnvironmentAssignmentRegex().IsMatch(value) ||
               CredentialUrlRegex().IsMatch(value) ||
               JwtRegex().IsMatch(value);
    }

    private static ProjectMemoryEntry[] Order(
        IEnumerable<ProjectMemoryEntry> entries)
    {
        return entries
            .OrderByDescending(entry => entry.UpdatedAtUtc)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id)
            .ToArray();
    }

    [GeneratedRegex(
        @"(?im)^\s*(?:password|passwd|pwd|secret|token|api[_ -]?key|access[_ -]?key|client[_ -]?secret|connection[_ -]?string)\s*[:=]\s*\S+")]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(@"(?m)^\s*[A-Z][A-Z0-9_]{2,}\s*=\s*\S+")]
    private static partial Regex EnvironmentAssignmentRegex();

    [GeneratedRegex(@"(?i)https?://[^\s/:]+:[^\s/@]+@")]
    private static partial Regex CredentialUrlRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.")]
    private static partial Regex JwtRegex();

    private sealed record StoreDocument(
        int Version,
        List<StoredEntry>? Entries);

    private sealed record StoredEntry(
        Guid Id,
        ProjectMemoryCategory Category,
        string? Title,
        string? Content,
        DateTimeOffset UpdatedAtUtc);

    private sealed record StoreReadResult(
        IReadOnlyList<ProjectMemoryEntry> Entries,
        string? Error)
    {
        public static StoreReadResult Success(
            IReadOnlyList<ProjectMemoryEntry> entries) =>
            new(entries, null);

        public static StoreReadResult Failure(string error) =>
            new([], error);
    }

    private sealed record Mutation(
        List<ProjectMemoryEntry> Entries,
        ProjectMemoryEntry? ChangedEntry,
        string? Error)
    {
        public static Mutation Success(
            List<ProjectMemoryEntry> entries,
            ProjectMemoryEntry? changedEntry) =>
            new(entries, changedEntry, null);

        public static Mutation Failure(string error) =>
            new([], null, error);
    }
}
