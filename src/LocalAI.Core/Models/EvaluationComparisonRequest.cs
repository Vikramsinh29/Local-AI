namespace LocalAI.Core.Models;

public sealed record EvaluationComparisonRequest(
    string ComparisonId,
    DateTimeOffset ComparedAtUtc,
    EvaluationReportDocument Baseline,
    EvaluationReportDocument Candidate);
