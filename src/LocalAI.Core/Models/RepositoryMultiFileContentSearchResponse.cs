namespace LocalAI.Core.Models;

public sealed record RepositoryMultiFileContentSearchResponse(
    IReadOnlyList<RepositoryMultiFileContentMatch> Matches,
    bool IsTruncated,
    string? Error)
{
    public bool IsSuccess => Error is null;
}
