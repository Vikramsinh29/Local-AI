namespace LocalAI.Core.Models;

public sealed record PatchRollbackResult(
    string? RolledBackRelativePath,
    string? Error)
{
    public bool IsSuccess =>
        RolledBackRelativePath is not null && Error is null;

    public static PatchRollbackResult Success(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new PatchRollbackResult(relativePath, null);
    }

    public static PatchRollbackResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new PatchRollbackResult(null, error);
    }
}
