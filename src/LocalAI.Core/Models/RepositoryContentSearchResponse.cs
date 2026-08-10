namespace LocalAI.Core.Models;

public sealed record RepositoryContentSearchResponse(
    IReadOnlyList<RepositoryContentMatch> Matches,
    bool IsTruncated,
    string? Error)
{
    public bool IsSuccess => Error is null;
}
