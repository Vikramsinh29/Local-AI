namespace LocalAI.Core.Models;

public sealed record EvaluationCaseComparison(
    string CaseId,
    EvaluationCategory? BaselineCategory,
    EvaluationCategory? CandidateCategory,
    bool? BaselinePassed,
    bool? CandidatePassed,
    string Direction);
