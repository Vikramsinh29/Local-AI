namespace LocalAI.Core.Models;

public sealed record ProjectInstructionFile(
    ProjectInstructionKind Kind,
    string RelativePath,
    long SizeBytes,
    int EstimatedTokens,
    string? Content,
    string? ExclusionReason)
{
    public bool IsEligible =>
        Content is not null &&
        string.IsNullOrWhiteSpace(ExclusionReason);
}
