using System.Text.RegularExpressions;
using LocalAI.Core.Evaluation;
using LocalAI.Core.Models;
using LocalAI.Infrastructure.Evaluation;

namespace LocalAI.Evaluation;

public static partial class Program
{
    public static int Main(string[] args)
    {
        try
        {
            IReadOnlyDictionary<string, string> options = ParseOptions(args);
            string fixtureRoot = Require(options, "fixtures");
            string evaluationRoot = Require(options, "evaluation-root");
            string runId = Require(options, "run-id");
            string productCommit = Require(options, "product-commit");
            string modelLabel = Require(options, "model-label");
            string profileLabel = Require(options, "profile-label");

            if (!RunIdPattern().IsMatch(runId))
            {
                throw new ArgumentException(
                    "Run ID must contain only lowercase letters, digits, " +
                    "and hyphens.");
            }

            string workingRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Environment.CurrentDirectory));
            string expectedFixtureRoot = Path.Combine(
                workingRoot,
                "evaluations",
                "fixtures",
                "v1");
            string expectedEvaluationRoot = Path.Combine(
                workingRoot,
                ".local-ai",
                "evaluations");

            if (!Path.GetFullPath(fixtureRoot).Equals(
                    expectedFixtureRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(evaluationRoot).Equals(
                    expectedEvaluationRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Evaluation fixtures and reports must use the fixed " +
                    "repository-local paths.");
            }

            JsonEvaluationFixtureLoader loader = new();
            IReadOnlyList<EvaluationCaseDefinition> cases =
                loader.Load(fixtureRoot);
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

    private static IReadOnlyDictionary<string, string> ParseOptions(
        IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Count % 2 != 0)
        {
            throw new ArgumentException(
                "Use fixed --name value evaluation options.");
        }

        Dictionary<string, string> options =
            new(StringComparer.Ordinal);

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

        string[] allowed =
        [
            "fixtures",
            "evaluation-root",
            "run-id",
            "product-commit",
            "model-label",
            "profile-label"
        ];

        if (options.Keys.Any(key => !allowed.Contains(
                key,
                StringComparer.Ordinal)))
        {
            throw new ArgumentException(
                "An unsupported evaluation option was provided.");
        }

        return options;
    }

    private static string Require(
        IReadOnlyDictionary<string, string> options,
        string name)
    {
        return options.TryGetValue(name, out string? value)
            ? value
            : throw new ArgumentException(
                $"Required evaluation option is missing: --{name}");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,63}$")]
    private static partial Regex RunIdPattern();
}
