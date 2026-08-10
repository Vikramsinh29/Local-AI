namespace LocalAI.Core.Models;

public sealed record EvaluationMetricDelta(
    string Name,
    int BaselinePassed,
    int BaselineTotal,
    decimal BaselineRate,
    int CandidatePassed,
    int CandidateTotal,
    decimal CandidateRate,
    int PassedDelta,
    decimal RateDelta,
    string Direction);
