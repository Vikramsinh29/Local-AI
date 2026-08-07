namespace LocalAI.Core.Models;

public sealed record PatchApplyResult(
    string? AppliedRelativePath,
    string? Error)
{
    public bool IsSuccess =>
        AppliedRelativePath is not null && Error is null;

    public static PatchApplyResult Success(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new PatchApplyResult(relativePath, null);
    }

    public static PatchApplyResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new PatchApplyResult(null, error);
    }
}
