using System.Text;
using System.Text.Json;
using LocalAI.Core.Models;
using LocalAI.Infrastructure.Repositories;

namespace LocalAI.Tests;

public sealed class ProjectMemoryServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _repositoryRoot;
    private readonly string _storageRoot;
    private readonly ProjectMemoryService _service;

    public ProjectMemoryServiceTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            $"LocalAI-ProjectMemory-{Guid.NewGuid():N}");
        _repositoryRoot = Path.Combine(_testRoot, "repository");
        _storageRoot = Path.Combine(_testRoot, "local-app-data");
        Directory.CreateDirectory(_repositoryRoot);
        _service = new ProjectMemoryService(_storageRoot);
    }

    [Fact]
    public async Task CreateAndLoad_UsesOutsideRepositoryStoreAndMetadata()
    {
        ProjectMemoryMutationResult created = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Decision,
            "Use records",
            "Keep immutable transport models.");

        Assert.True(created.IsSuccess, created.Error);
        ProjectMemoryEntry entry = Assert.Single(created.Entries);
        Assert.Equal(ProjectMemoryCategory.Decision, entry.Category);
        Assert.Equal("Use records", entry.Title);
        Assert.True(entry.SizeBytes > 0);
        Assert.True(entry.EstimatedTokens > 0);

        ProjectMemoryService restartedService = new(_storageRoot);
        ProjectMemoryLoadResult loaded = await restartedService.LoadAsync(
            _repositoryRoot);

        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Equal(entry.Id, Assert.Single(loaded.Entries).Id);
        Assert.True(
            loaded.StoragePath.StartsWith(
                Path.GetFullPath(_storageRoot),
                StringComparison.OrdinalIgnoreCase),
            loaded.StoragePath);
        Assert.False(
            loaded.StoragePath.StartsWith(
                Path.GetFullPath(_repositoryRoot) +
                Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RepositoryIdentity_IsolatesDifferentCanonicalRoots()
    {
        string otherRepository = Path.Combine(_testRoot, "other-repository");
        Directory.CreateDirectory(otherRepository);

        await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Architecture,
            "First",
            "Repository one.");
        await _service.CreateAsync(
            otherRepository,
            ProjectMemoryCategory.Architecture,
            "Second",
            "Repository two.");

        ProjectMemoryLoadResult first =
            await _service.LoadAsync(_repositoryRoot);
        ProjectMemoryLoadResult second =
            await _service.LoadAsync(otherRepository);

        Assert.NotEqual(first.StoragePath, second.StoragePath);
        Assert.Equal("First", Assert.Single(first.Entries).Title);
        Assert.Equal("Second", Assert.Single(second.Entries).Title);
    }

    [Fact]
    public async Task Update_PreservesIdentityAndChangesOnlySelectedEntry()
    {
        ProjectMemoryMutationResult first = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Command,
            "Build",
            "dotnet build Sample.slnx");
        ProjectMemoryMutationResult second = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.KnownIssue,
            "GPU",
            "Use CPU mode on this workstation.");
        Guid firstId = first.ChangedEntry!.Id;

        ProjectMemoryMutationResult updated = await _service.UpdateAsync(
            _repositoryRoot,
            firstId,
            ProjectMemoryCategory.Command,
            "Release build",
            "dotnet build Sample.slnx -c Release");

        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal(firstId, updated.ChangedEntry!.Id);
        Assert.Contains(
            updated.Entries,
            entry => entry.Id == firstId &&
                entry.Title == "Release build");
        Assert.Contains(
            updated.Entries,
            entry => entry.Id == second.ChangedEntry!.Id &&
                entry.Title == "GPU");
    }

    [Fact]
    public async Task Delete_RemovesOnlyRequestedEntry()
    {
        ProjectMemoryMutationResult first = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Decision,
            "First",
            "Keep this note.");
        ProjectMemoryMutationResult second = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Decision,
            "Second",
            "Delete this note.");

        ProjectMemoryMutationResult deleted = await _service.DeleteAsync(
            _repositoryRoot,
            second.ChangedEntry!.Id);

        Assert.True(deleted.IsSuccess, deleted.Error);
        Assert.Equal(second.ChangedEntry.Id, deleted.ChangedEntry!.Id);
        Assert.Equal(first.ChangedEntry!.Id, Assert.Single(deleted.Entries).Id);
    }

    [Fact]
    public async Task Create_RejectsOversizedEntryWithoutWriting()
    {
        string oversized = new(
            'x',
            ProjectMemoryService.MaximumEntryBytesLimit);

        ProjectMemoryMutationResult result = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Architecture,
            "Large",
            oversized);

        Assert.False(result.IsSuccess);
        Assert.Contains("1,024", result.Error!);
        Assert.Empty(
            (await _service.LoadAsync(_repositoryRoot)).Entries);
    }

    [Fact]
    public async Task Create_RejectsSeventeenthEntry()
    {
        for (int index = 0;
             index < ProjectMemoryService.MaximumEntriesLimit;
             index++)
        {
            ProjectMemoryMutationResult result =
                await _service.CreateAsync(
                    _repositoryRoot,
                    ProjectMemoryCategory.Architecture,
                    $"Entry {index}",
                    "Bounded content.");
            Assert.True(result.IsSuccess, result.Error);
        }

        ProjectMemoryMutationResult rejected = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Architecture,
            "Entry 17",
            "Must not be stored.");

        Assert.False(rejected.IsSuccess);
        Assert.Contains("16", rejected.Error!);
        Assert.Equal(
            ProjectMemoryService.MaximumEntriesLimit,
            (await _service.LoadAsync(_repositoryRoot)).Entries.Count);
    }

    [Fact]
    public async Task Create_EnforcesEstimatedTokenBudgetWithoutSilentTruncation()
    {
        for (int index = 0; index < 15; index++)
        {
            ProjectMemoryMutationResult result = await _service.CreateAsync(
                _repositoryRoot,
                ProjectMemoryCategory.Decision,
                $"Entry {index:00}",
                new string('x', 492));
            Assert.True(result.IsSuccess, result.Error);
        }

        ProjectMemoryMutationResult rejected = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Decision,
            "Entry 15",
            new string('x', 492));

        Assert.False(rejected.IsSuccess);
        Assert.Contains("2,000", rejected.Error!);
        Assert.Equal(
            15,
            (await _service.LoadAsync(_repositoryRoot)).Entries.Count);
    }

    [Fact]
    public async Task Create_EnforcesCombinedByteBudgetWithoutWritingPartialState()
    {
        for (int index = 0; index < 8; index++)
        {
            ProjectMemoryMutationResult result = await _service.CreateAsync(
                _repositoryRoot,
                ProjectMemoryCategory.Command,
                $"Memory {index}",
                new string('x', 991));
            Assert.True(result.IsSuccess, result.Error);
        }

        ProjectMemoryMutationResult rejected = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Command,
            "Memory 8",
            new string('x', 991));

        Assert.False(rejected.IsSuccess);
        Assert.Contains("8,192", rejected.Error!);
        Assert.Equal(
            8,
            (await _service.LoadAsync(_repositoryRoot)).Entries.Count);
    }

    [Theory]
    [InlineData("password=super-secret")]
    [InlineData("API_TOKEN=abc123")]
    [InlineData("https://user:password@example.test")]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    public async Task Create_RejectsSensitiveMaterial(string content)
    {
        ProjectMemoryMutationResult result = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.KnownIssue,
            "Sensitive",
            content);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            "cannot store",
            result.Error!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(
            (await _service.LoadAsync(_repositoryRoot)).Entries);
    }

    [Fact]
    public async Task CorruptStore_IsReportedAndNeverSilentlyRepaired()
    {
        ProjectMemoryLoadResult empty =
            await _service.LoadAsync(_repositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(empty.StoragePath)!);
        await File.WriteAllTextAsync(
            empty.StoragePath,
            "{ definitely not valid json",
            Encoding.UTF8);
        byte[] original = await File.ReadAllBytesAsync(empty.StoragePath);

        ProjectMemoryLoadResult loaded =
            await _service.LoadAsync(_repositoryRoot);
        ProjectMemoryMutationResult create = await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Decision,
            "Do not repair",
            "Keep corruption visible.");

        Assert.False(loaded.IsSuccess);
        Assert.Contains("malformed", loaded.Error!);
        Assert.False(create.IsSuccess);
        Assert.Equal(original, await File.ReadAllBytesAsync(empty.StoragePath));
    }

    [Fact]
    public async Task BinaryStore_IsReportedHonestly()
    {
        ProjectMemoryLoadResult empty =
            await _service.LoadAsync(_repositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(empty.StoragePath)!);
        await File.WriteAllBytesAsync(
            empty.StoragePath,
            [0x7B, 0x00, 0x7D]);

        ProjectMemoryLoadResult loaded =
            await _service.LoadAsync(_repositoryRoot);

        Assert.False(loaded.IsSuccess);
        Assert.Contains("binary data", loaded.Error!);
    }

    [Fact]
    public async Task InvalidUtf8Store_IsReportedHonestly()
    {
        ProjectMemoryLoadResult empty =
            await _service.LoadAsync(_repositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(empty.StoragePath)!);
        await File.WriteAllBytesAsync(
            empty.StoragePath,
            [0x7B, 0xC3, 0x28, 0x7D]);

        ProjectMemoryLoadResult loaded =
            await _service.LoadAsync(_repositoryRoot);

        Assert.False(loaded.IsSuccess);
        Assert.Contains("UTF-8", loaded.Error!);
    }

    [Fact]
    public async Task UnsupportedSchema_IsReportedHonestly()
    {
        ProjectMemoryLoadResult empty =
            await _service.LoadAsync(_repositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(empty.StoragePath)!);
        await File.WriteAllTextAsync(
            empty.StoragePath,
            "{\"Version\":99,\"Entries\":[]}",
            Encoding.UTF8);

        ProjectMemoryLoadResult loaded =
            await _service.LoadAsync(_repositoryRoot);

        Assert.False(loaded.IsSuccess);
        Assert.Contains("schema version", loaded.Error!);
    }

    [Fact]
    public async Task UnknownSchemaField_IsRejectedInsteadOfSilentlyDiscarded()
    {
        ProjectMemoryLoadResult empty =
            await _service.LoadAsync(_repositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(empty.StoragePath)!);
        await File.WriteAllTextAsync(
            empty.StoragePath,
            "{\"Version\":1,\"Entries\":[],\"Unknown\":\"data\"}",
            Encoding.UTF8);

        ProjectMemoryLoadResult loaded =
            await _service.LoadAsync(_repositoryRoot);

        Assert.False(loaded.IsSuccess);
        Assert.Contains("malformed", loaded.Error!);
    }

    [Fact]
    public async Task DuplicateEntryIdentifiers_AreRejectedAsCorruption()
    {
        ProjectMemoryLoadResult empty =
            await _service.LoadAsync(_repositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(empty.StoragePath)!);
        Guid duplicateId = Guid.NewGuid();
        string updatedAt = DateTimeOffset.UtcNow.ToString("O");
        string json = $$"""
            {
              "Version": 1,
              "Entries": [
                {
                  "Id": "{{duplicateId}}",
                  "Category": 0,
                  "Title": "First",
                  "Content": "First entry.",
                  "UpdatedAtUtc": "{{updatedAt}}"
                },
                {
                  "Id": "{{duplicateId}}",
                  "Category": 2,
                  "Title": "Second",
                  "Content": "Second entry.",
                  "UpdatedAtUtc": "{{updatedAt}}"
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(
            empty.StoragePath,
            json,
            Encoding.UTF8);

        ProjectMemoryLoadResult loaded =
            await _service.LoadAsync(_repositoryRoot);

        Assert.False(loaded.IsSuccess);
        Assert.Contains("duplicate entry ID", loaded.Error!);
    }

    [Fact]
    public async Task AtomicWrite_LeavesOneValidJsonFileAndNoTemporaryFiles()
    {
        await _service.CreateAsync(
            _repositoryRoot,
            ProjectMemoryCategory.Architecture,
            "Atomic",
            "Write by same-directory replacement.");
        ProjectMemoryLoadResult loaded =
            await _service.LoadAsync(_repositoryRoot);

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(loaded.StoragePath));

        Assert.Equal(
            1,
            document.RootElement.GetProperty("Version").GetInt32());
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.GetDirectoryName(loaded.StoragePath)!,
                "*.tmp"));
    }

    [Fact]
    public async Task CancelledCreate_DoesNotWriteMemory()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.CreateAsync(
                _repositoryRoot,
                ProjectMemoryCategory.Decision,
                "Cancelled",
                "Must not be written.",
                cancellation.Token));

        Assert.Empty(
            (await _service.LoadAsync(_repositoryRoot)).Entries);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // Cleanup must not hide test results.
        }
    }
}
