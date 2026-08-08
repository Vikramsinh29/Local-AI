namespace LocalAI.Core.Models;

public sealed record AgentResponseEvidenceValidationResult(
    IReadOnlyList<string> MissingRequiredPaths,
    IReadOnlyList<string> UnexpectedPaths)
{
    public bool IsValid =>
        MissingRequiredPaths.Count == 0 &&
        UnexpectedPaths.Count == 0;
}
