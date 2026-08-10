namespace LocalAI.Core.Models;

public sealed record EvaluationComparisonResult(
    int SchemaVersion,
    string ComparisonId,
    DateTimeOffset ComparedAtUtc,
    EvaluationComparisonSource Baseline,
    EvaluationComparisonSource Candidate,
    long DurationDeltaMilliseconds,
    decimal? DurationPercentDelta,
    IReadOnlyList<EvaluationCaseComparison> Cases,
    IReadOnlyList<EvaluationMetricDelta> Metrics,
    IReadOnlyList<EvaluationComparisonGate> Gates,
    EvaluationComparisonRecommendation Recommendation,
    string Advisory);
