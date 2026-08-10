namespace LocalAI.Core.Models;

public sealed record EvaluationRunRequest(
    string RunId,
    string EvaluatorSchemaVersion,
    string ProductCommit,
    string ModelLabel,
    string ProfileLabel,
    string FixtureRoot,
    DateTimeOffset StartedAtUtc);
