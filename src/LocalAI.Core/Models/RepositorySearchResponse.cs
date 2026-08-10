namespace LocalAI.Core.Models;

public sealed record RepositorySearchResponse(
    IReadOnlyList<RepositorySearchResult> Results,
    bool IsTruncated,
    string? Error)
{
    public bool IsSuccess => Error is null;
}
