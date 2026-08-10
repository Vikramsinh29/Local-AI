namespace LocalAI.Core.Models;

public sealed record EvaluationReportDocument(
    string SourcePath,
    string Sha256,
    string CaseSetIdentity,
    EvaluationRunReport Report);
