namespace LocalAI.Core.Models;

public sealed record EvaluationComparisonSource(
    string SourcePath,
    string Sha256,
    string RunId,
    string ProductCommit,
    string ModelLabel,
    string ProfileLabel,
    string EvaluatorSchemaVersion,
    string CaseSetIdentity,
    long DurationMilliseconds);
