namespace LocalAI.Core.Models;

public sealed record PatchApplyResult(
    string? AppliedRelativePath,
    string? Error)
{
    public PatchRollbackRecord? RollbackRecord { get; init; }

    public bool IsSuccess =>
        AppliedRelativePath is not null && Error is null;

    public static PatchApplyResult Success(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new PatchApplyResult(relativePath, null);
    }

    public static PatchApplyResult Success(
        string relativePath,
        PatchRollbackRecord rollbackRecord)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(rollbackRecord);

        return new PatchApplyResult(relativePath, null)
        {
            RollbackRecord = rollbackRecord
        };
    }

    public static PatchApplyResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new PatchApplyResult(null, error);
    }
}

