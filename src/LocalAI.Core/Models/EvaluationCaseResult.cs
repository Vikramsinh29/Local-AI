namespace LocalAI.Core.Models;

public sealed record EvaluationCaseResult(
    string CaseId,
    EvaluationCategory Category,
    string FixturePath,
    string InputEvidencePath,
    string RecordedOutputPath,
    bool Passed,
    IReadOnlyDictionary<string, bool> Scores,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> SafetyLabels);
