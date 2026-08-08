namespace LocalAI.Core.Models;

public sealed record ProjectMemoryEntry(
    Guid Id,
    ProjectMemoryCategory Category,
    string Title,
    string Content,
    long SizeBytes,
    int EstimatedTokens,
    DateTimeOffset UpdatedAtUtc);
