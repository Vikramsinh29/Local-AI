namespace LocalAI.Core.Models;

public sealed record RepositoryContextReadResult(
    RepositoryContextFile? File,
    string? Error)
{
    public bool IsSuccess => File is not null;

    public static RepositoryContextReadResult Success(
        RepositoryContextFile file) =>
        new(file, null);

    public static RepositoryContextReadResult Failure(
        string error) =>
        new(null, error);
}
