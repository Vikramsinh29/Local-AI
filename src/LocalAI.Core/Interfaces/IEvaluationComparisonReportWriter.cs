using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IEvaluationComparisonReportWriter
{
    EvaluationComparisonReportWriteResult Write(
        string comparisonId,
        EvaluationComparisonResult result);
}
