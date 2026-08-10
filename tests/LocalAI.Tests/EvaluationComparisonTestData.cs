using System.Text.Json;
using System.Text.Json.Serialization;
using LocalAI.Core.Evaluation;
using LocalAI.Core.Models;

namespace LocalAI.Tests;

internal static class EvaluationComparisonTestData
{
    public static EvaluationRunReport CreateReport(
        string runId = "baseline-run",
        string modelLabel = "model-a",
        string profileLabel = "balanced",
        string productCommit = "commit-abc",
        string evaluatorSchema =
            DeterministicEvaluationRunner.EvaluatorSchemaVersion,
        bool plan = true,
        bool grounding = true,
        bool fileSelection = true,
        bool patch = true,
        bool safety = true,
        long durationMilliseconds = 1_000,
        bool omitFileSelectionCase = false)
    {
        DateTimeOffset started =
            new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        List<EvaluationCaseResult> cases =
        [
            Case(
                "grounded-plan",
                EvaluationCategory.GroundedPlanning,
                new Dictionary<string, bool>
                {
                    ["planCorrectness"] = plan,
                    ["evidenceGrounding"] = grounding
                }),
            Case(
                "structured-patch",
                EvaluationCategory.StructuredPatchValidity,
                new Dictionary<string, bool>
                {
                    ["patchValidity"] = patch
                }),
            Case(
                "unsafe-action",
                EvaluationCategory.UnsafeActionRejection,
                new Dictionary<string, bool>
                {
                    ["unsafeActionRejection"] = safety
                })
        ];

        if (!omitFileSelectionCase)
        {
            cases.Add(Case(
                "file-selection",
                EvaluationCategory.FileSelection,
                new Dictionary<string, bool>
                {
                    ["fileSelectionPrecision"] = fileSelection
                }));
        }

        cases = cases.OrderBy(
                item => item.CaseId,
                StringComparer.Ordinal)
            .ToList();
        IReadOnlyList<EvaluationMetricSummary> metrics =
            DeterministicEvaluationComparisonService.MetricNames
                .Select(name => Metric(cases, name))
                .ToArray();

        return new EvaluationRunReport(
            1,
            runId,
            evaluatorSchema,
            productCommit,
            modelLabel,
            profileLabel,
            "evaluations/fixtures/v1",
            started,
            started.AddMilliseconds(durationMilliseconds),
            durationMilliseconds,
            cases,
            metrics);
    }

    public static EvaluationReportDocument Document(
        EvaluationRunReport report,
        string sha256,
        string caseSetIdentity = "stable-case-set") =>
        new(
            $"{report.RunId}/evaluation-report.json",
            sha256,
            caseSetIdentity,
            report);

    public static byte[] Serialize(EvaluationRunReport report)
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return JsonSerializer.SerializeToUtf8Bytes(report, options);
    }

    private static EvaluationCaseResult Case(
        string id,
        EvaluationCategory category,
        IReadOnlyDictionary<string, bool> scores) =>
        new(
            id,
            category,
            $"{id}.case.json",
            $"inputs/{id}.txt",
            $"responses/{id}.txt",
            scores.Values.All(value => value),
            scores,
            [],
            ["offline", "no-network"]);

    private static EvaluationMetricSummary Metric(
        IReadOnlyList<EvaluationCaseResult> cases,
        string name)
    {
        bool[] values = cases
            .Where(item => item.Scores.ContainsKey(name))
            .Select(item => item.Scores[name])
            .ToArray();
        int passed = values.Count(value => value);
        decimal rate = values.Length == 0
            ? 0m
            : decimal.Round(
                (decimal)passed / values.Length,
                4,
                MidpointRounding.AwayFromZero);
        return new EvaluationMetricSummary(name, passed, values.Length, rate);
    }
}
