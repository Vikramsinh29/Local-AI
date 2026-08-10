using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;

namespace LocalAI.Infrastructure.Evaluation;

public sealed class LocalEvaluationReportWriter : IEvaluationReportWriter
{
    public const int MaximumReportBytes = 2_097_152;

    private readonly string _evaluationRoot;

    public LocalEvaluationReportWriter(string evaluationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluationRoot);
        _evaluationRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(evaluationRoot));
    }

    public EvaluationReportWriteResult Write(
        string outputDirectory,
        EvaluationRunReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(report);

        string output = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(outputDirectory));
        string rootPrefix = _evaluationRoot + Path.DirectorySeparatorChar;
        string? parent = Path.GetDirectoryName(output);

        if (!output.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase) ||
            parent is null ||
            !parent.Equals(
                _evaluationRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Evaluation reports must use one direct run directory under " +
                "the approved .local-ai/evaluations root.");
        }

        Directory.CreateDirectory(_evaluationRoot);
        RejectPathChain(_evaluationRoot);

        if (Directory.Exists(output) &&
            Directory.EnumerateFileSystemEntries(output).Any())
        {
            throw new IOException(
                "The evaluation run directory already contains files.");
        }

        Directory.CreateDirectory(output);
        RejectReparsePoint(output);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            report,
            CreateJsonOptions());
        byte[] markdown = Encoding.UTF8.GetBytes(BuildMarkdown(report));
        ValidateSize(json, "JSON");
        ValidateSize(markdown, "Markdown");

        string jsonPath = Path.Combine(output, "evaluation-report.json");
        string markdownPath = Path.Combine(output, "evaluation-report.md");
        WriteAtomic(jsonPath, json);
        WriteAtomic(markdownPath, markdown);

        return new EvaluationReportWriteResult(jsonPath, markdownPath);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static string BuildMarkdown(EvaluationRunReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Local-AI deterministic evaluation report");
        builder.AppendLine();
        builder.AppendLine($"- Run: `{report.RunId}`");
        builder.AppendLine(
            $"- Evaluator schema: `{report.EvaluatorSchemaVersion}`");
        builder.AppendLine($"- Product commit: `{report.ProductCommit}`");
        builder.AppendLine($"- Model label: `{report.ModelLabel}`");
        builder.AppendLine($"- Profile label: `{report.ProfileLabel}`");
        builder.AppendLine(
            $"- Duration: {report.DurationMilliseconds.ToString(CultureInfo.InvariantCulture)} ms");
        builder.AppendLine(
            $"- Cases: {report.PassedCases} passed, " +
            $"{report.FailedCases} failed, {report.Cases.Count} total");
        builder.AppendLine();
        builder.AppendLine("## Aggregate metrics");
        builder.AppendLine();
        builder.AppendLine("| Metric | Passed | Total | Rate |");
        builder.AppendLine("|---|---:|---:|---:|");

        foreach (EvaluationMetricSummary metric in report.Metrics)
        {
            builder.AppendLine(
                $"| {metric.Name} | {metric.Passed} | {metric.Total} | " +
                $"{metric.Rate.ToString("0.0000", CultureInfo.InvariantCulture)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Cases");
        builder.AppendLine();

        foreach (EvaluationCaseResult item in report.Cases)
        {
            builder.AppendLine(
                $"### {item.CaseId} — {(item.Passed ? "Passed" : "Failed")}");
            builder.AppendLine();
            builder.AppendLine($"- Category: `{item.Category}`");
            builder.AppendLine($"- Fixture: `{item.FixturePath}`");
            builder.AppendLine(
                $"- Safety labels: {string.Join(", ", item.SafetyLabels)}");

            foreach (KeyValuePair<string, bool> score in item.Scores)
            {
                builder.AppendLine(
                    $"- {score.Key}: {(score.Value ? "Passed" : "Failed")}");
            }

            foreach (string finding in item.Findings)
            {
                builder.AppendLine($"- Finding: {finding.Replace('|', '/')}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void ValidateSize(byte[] content, string label)
    {
        if (content.Length > MaximumReportBytes)
        {
            throw new InvalidDataException(
                $"The {label} evaluation report exceeds the bounded limit.");
        }
    }

    private static void WriteAtomic(string destination, byte[] content)
    {
        string temporary = destination + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, destination, false);
    }

    private static void RejectReparsePoint(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException(
                "Evaluation output cannot use a linked directory.");
        }
    }

    private static void RejectPathChain(string path)
    {
        DirectoryInfo? current = new(path);

        while (current is not null)
        {
            RejectReparsePoint(current.FullName);
            current = current.Parent;
        }
    }
}
