namespace LocalAI.Core.Models;

public sealed record EvaluationRunReport(
    int SchemaVersion,
    string RunId,
    string EvaluatorSchemaVersion,
    string ProductCommit,
    string ModelLabel,
    string ProfileLabel,
    string FixtureRoot,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long DurationMilliseconds,
    IReadOnlyList<EvaluationCaseResult> Cases,
    IReadOnlyList<EvaluationMetricSummary> Metrics)
{
    public int PassedCases => Cases.Count(item => item.Passed);

    public int FailedCases => Cases.Count - PassedCases;
}
