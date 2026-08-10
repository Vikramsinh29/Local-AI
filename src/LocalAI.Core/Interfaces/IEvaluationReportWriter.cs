using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IEvaluationReportWriter
{
    EvaluationReportWriteResult Write(
        string outputDirectory,
        EvaluationRunReport report);
}
