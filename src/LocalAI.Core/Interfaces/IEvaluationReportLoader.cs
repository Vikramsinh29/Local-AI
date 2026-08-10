using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IEvaluationReportLoader
{
    EvaluationReportDocument Load(
        string evaluationRoot,
        string reportPath);
}
