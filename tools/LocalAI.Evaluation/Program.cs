using System.Text.RegularExpressions;
using LocalAI.Core.Evaluation;
using LocalAI.Core.Models;
using LocalAI.Infrastructure.Evaluation;

namespace LocalAI.Evaluation;

public static partial class Program
{
    private static readonly string[] RunOptions =
    [
        "fixtures",
        "evaluation-root",
        "run-id",
        "product-commit",
        "model-label",
        "profile-label"
    ];

    private static readonly string[] CompareOptions =
    [
        "evaluation-root",
        "comparison-id",
        "baseline-report",
        "candidate-report"
    ];

    public static int Main(string[] args)
    {
        try
        {
            return args.Length > 0 &&
                   args[0].Equals("compare", StringComparison.Ordinal)
                ? Compare(args[1..])
                : Run(args);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            DirectoryNotFoundException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException)
        {
            Console.Error.WriteLine("LOCAL_AI_EVALUATION_ERROR");
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int Run(IReadOnlyList<string> args)
    {
        IReadOnlyDictionary<string, string> options = ParseOptions(
            args,
            RunOptions);
        string fixtureRoot = Require(options, "fixtures");
        string evaluationRoot = Require(options, "evaluation-root");
        string runId = Require(options, "run-id");
        string productCommit = Require(options, "product-commit");
        string modelLabel = Require(options, "model-label");
        string profileLabel = Require(options, "profile-label");

        ValidateIdentifier(runId, "Run ID");
        string workingRoot = WorkingRoot();
        ValidateFixedPath(
            fixtureRoot,
            Path.Combine(workingRoot, "evaluations", "fixtures", "v1"),
            "Evaluation fixtures");
        ValidateFixedPath(
            evaluationRoot,
            FixedEvaluationRoot(workingRoot),
            "Evaluation reports");

        JsonEvaluationFixtureLoader loader = new();
        IReadOnlyList<EvaluationCaseDefinition> cases = loader.Load(
            fixtureRoot);
        EvaluationRunRequest request = new(
            runId,
            DeterministicEvaluationRunner.EvaluatorSchemaVersion,
            productCommit,
            modelLabel,
            profileLabel,
            Path.GetFullPath(fixtureRoot),
            DateTimeOffset.UtcNow);
        DeterministicEvaluationRunner runner = new();
        EvaluationRunReport report = runner.Run(request, cases);
        string outputDirectory = Path.Combine(evaluationRoot, runId);
        LocalEvaluationReportWriter writer = new(evaluationRoot);
        EvaluationReportWriteResult written = writer.Write(
            outputDirectory,
            report);

        Console.WriteLine("LOCAL_AI_EVALUATION_COMPLETED");
        Console.WriteLine($"runId={report.RunId}");
        Console.WriteLine($"cases={report.Cases.Count}");
        Console.WriteLine($"passed={report.PassedCases}");
        Console.WriteLine($"failed={report.FailedCases}");
        Console.WriteLine($"json={written.JsonPath}");
        Console.WriteLine($"markdown={written.MarkdownPath}");
        return 0;
    }

    private static int Compare(IReadOnlyList<string> args)
    {
        IReadOnlyDictionary<string, string> options = ParseOptions(
            args,
            CompareOptions);
        string evaluationRoot = Require(options, "evaluation-root");
        string comparisonId = Require(options, "comparison-id");
        string baselinePath = Require(options, "baseline-report");
        string candidatePath = Require(options, "candidate-report");

        ValidateIdentifier(comparisonId, "Comparison ID");
        ValidateFixedPath(
            evaluationRoot,
            FixedEvaluationRoot(WorkingRoot()),
            "Evaluation reports");

        LocalEvaluationReportLoader loader = new();
        EvaluationReportDocument baseline = loader.Load(
            evaluationRoot,
            baselinePath);
        EvaluationReportDocument candidate = loader.Load(
            evaluationRoot,
            candidatePath);
        DeterministicEvaluationComparisonService service = new();
        EvaluationComparisonResult comparison = service.Compare(new(
            comparisonId,
            DateTimeOffset.UtcNow,
            baseline,
            candidate));
        LocalEvaluationComparisonReportWriter writer = new(evaluationRoot);
        EvaluationComparisonReportWriteResult written = writer.Write(
            comparisonId,
            comparison);

        Console.WriteLine("LOCAL_AI_EVALUATION_COMPARISON_COMPLETED");
        Console.WriteLine($"comparisonId={comparison.ComparisonId}");
        Console.WriteLine($"baselineRunId={comparison.Baseline.RunId}");
        Console.WriteLine($"candidateRunId={comparison.Candidate.RunId}");
        Console.WriteLine(
            $"recommendation={Recommendation(comparison.Recommendation)}");
        Console.WriteLine($"json={written.JsonPath}");
        Console.WriteLine($"markdown={written.MarkdownPath}");
        return 0;
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(
        IReadOnlyList<string> args,
        IReadOnlyCollection<string> allowed)
    {
        if (args.Count == 0 || args.Count % 2 != 0)
        {
            throw new ArgumentException(
                "Use fixed --name value evaluation options.");
        }

        Dictionary<string, string> options = new(StringComparer.Ordinal);

        for (int index = 0; index < args.Count; index += 2)
        {
            string name = args[index];

            if (!name.StartsWith("--", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(args[index + 1]) ||
                !options.TryAdd(name[2..], args[index + 1]))
            {
                throw new ArgumentException(
                    "Evaluation options are malformed or duplicated.");
            }
        }

        if (options.Keys.Any(key => !allowed.Contains(key)))
        {
            throw new ArgumentException(
                "An unsupported evaluation option was provided.");
        }

        return options;
    }

    private static string Require(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        options.TryGetValue(name, out string? value)
            ? value
            : throw new ArgumentException(
                $"Required evaluation option is missing: --{name}");

    private static void ValidateIdentifier(string value, string label)
    {
        if (!IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException(
                $"{label} must contain only lowercase letters, digits, and " +
                "hyphens.");
        }
    }

    private static string WorkingRoot() =>
        Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Environment.CurrentDirectory));

    private static string FixedEvaluationRoot(string workingRoot) =>
        Path.Combine(workingRoot, ".local-ai", "evaluations");

    private static void ValidateFixedPath(
        string actual,
        string expected,
        string label)
    {
        if (!Path.GetFullPath(actual).Equals(
                expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"{label} must use the fixed repository-local path.");
        }
    }

    private static string Recommendation(
        EvaluationComparisonRecommendation value) => value switch
        {
            EvaluationComparisonRecommendation.EligibleForUserReview =>
                "eligibleForUserReview",
            _ => "notRecommended"
        };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,63}$")]
    private static partial Regex IdentifierPattern();
}
