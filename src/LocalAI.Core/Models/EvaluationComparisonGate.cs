namespace LocalAI.Core.Models;

public sealed record EvaluationComparisonGate(
    string Name,
    bool Passed,
    string Evidence);
