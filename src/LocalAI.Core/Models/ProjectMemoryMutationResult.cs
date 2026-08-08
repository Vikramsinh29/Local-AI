namespace LocalAI.Core.Models;

public sealed record ProjectMemoryMutationResult(
    IReadOnlyList<ProjectMemoryEntry> Entries,
    ProjectMemoryEntry? ChangedEntry,
    string? Error)
{
    public bool IsSuccess => Error is null;

    public static ProjectMemoryMutationResult Success(
        IReadOnlyList<ProjectMemoryEntry> entries,
        ProjectMemoryEntry? changedEntry = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new ProjectMemoryMutationResult(
            entries,
            changedEntry,
            null);
    }

    public static ProjectMemoryMutationResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new ProjectMemoryMutationResult([], null, error);
    }
}
