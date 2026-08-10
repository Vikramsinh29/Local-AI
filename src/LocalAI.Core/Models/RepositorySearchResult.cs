namespace LocalAI.Core.Models;

public sealed record RepositorySearchResult(
    string Name,
    string RelativePath,
    long? SizeBytes,
    DateTime? LastModifiedUtc);
