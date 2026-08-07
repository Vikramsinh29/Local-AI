namespace LocalAI.Core.Models;

public sealed record ProposedPatchParseResult(
    ProposedPatchPreview? Preview,
    string? Error)
{
    public bool IsSuccess => Preview is not null;

    public static ProposedPatchParseResult Success(
        ProposedPatchPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new ProposedPatchParseResult(preview, null);
    }

    public static ProposedPatchParseResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new ProposedPatchParseResult(null, error);
    }
}
