using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IProjectMemoryService
{
    int MaximumEntries { get; }

    int MaximumEntryBytes { get; }

    int MaximumCombinedBytes { get; }

    int MaximumCombinedTokens { get; }

    Task<ProjectMemoryLoadResult> LoadAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);

    Task<ProjectMemoryMutationResult> CreateAsync(
        string repositoryRoot,
        ProjectMemoryCategory category,
        string title,
        string content,
        CancellationToken cancellationToken = default);

    Task<ProjectMemoryMutationResult> UpdateAsync(
        string repositoryRoot,
        Guid entryId,
        ProjectMemoryCategory category,
        string title,
        string content,
        CancellationToken cancellationToken = default);

    Task<ProjectMemoryMutationResult> DeleteAsync(
        string repositoryRoot,
        Guid entryId,
        CancellationToken cancellationToken = default);
}
