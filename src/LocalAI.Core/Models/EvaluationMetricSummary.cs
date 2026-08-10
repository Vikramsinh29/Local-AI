namespace LocalAI.Core.Models;

public sealed record EvaluationMetricSummary(
    string Name,
    int Passed,
    int Total,
    decimal Rate);
