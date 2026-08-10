namespace LocalAI.Core.Models;

public sealed record EvaluationCaseDefinition(
    int SchemaVersion,
    string Id,
    EvaluationCategory Category,
    string InputEvidencePath,
    string RecordedOutputPath,
    string? RepositoryRootPath,
    IReadOnlyList<string>? AllowedSourcePaths,
    EvaluationExpectation Expected,
    IReadOnlyList<string> SafetyLabels,
    string FixturePath = "");
