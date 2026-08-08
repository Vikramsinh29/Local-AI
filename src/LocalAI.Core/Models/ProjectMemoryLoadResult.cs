namespace LocalAI.Core.Models;

public sealed record ProjectMemoryLoadResult(
    IReadOnlyList<ProjectMemoryEntry> Entries,
    string StoragePath,
    string? Error)
{
    public bool IsSuccess => Error is null;

    public static ProjectMemoryLoadResult Success(
        IReadOnlyList<ProjectMemoryEntry> entries,
        string storagePath)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        return new ProjectMemoryLoadResult(entries, storagePath, null);
    }

    public static ProjectMemoryLoadResult Failure(
        string storagePath,
        string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new ProjectMemoryLoadResult([], storagePath, error);
    }
}
