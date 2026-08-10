using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IEvaluationFixtureLoader
{
    IReadOnlyList<EvaluationCaseDefinition> Load(string fixtureRoot);
}
