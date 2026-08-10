namespace LocalAI.Core.Models;

public sealed record EvaluationExpectation(
    IReadOnlyList<string>? RequiredEvidencePaths = null,
    IReadOnlyList<string>? ExpectedCandidateFiles = null,
    bool? ExpectedPatchValid = null,
    bool? ExpectedUnsafeActionRejected = null);
