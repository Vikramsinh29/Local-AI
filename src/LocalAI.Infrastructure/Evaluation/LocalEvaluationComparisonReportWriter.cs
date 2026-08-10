using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;

namespace LocalAI.Infrastructure.Evaluation;

public sealed class LocalEvaluationComparisonReportWriter :
    IEvaluationComparisonReportWriter
{
    public const int MaximumComparisonReportBytes = 2_097_152;

    private readonly string _evaluationRoot;

    public LocalEvaluationComparisonReportWriter(string evaluationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluationRoot);
        _evaluationRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(evaluationRoot));
    }

    public EvaluationComparisonReportWriteResult Write(
        string comparisonId,
        EvaluationComparisonResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonId);
        ArgumentNullException.ThrowIfNull(result);

        if (!comparisonId.Equals(
                result.ComparisonId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Comparison output ID does not match the evaluated result.");
        }

        string comparisonRoot = Path.Combine(
            _evaluationRoot,
            "comparisons");
        string output = Path.Combine(comparisonRoot, comparisonId);
        Directory.CreateDirectory(_evaluationRoot);
        RejectPathChain(_evaluationRoot);
        Directory.CreateDirectory(comparisonRoot);
        RejectPathChain(comparisonRoot);

        if (Directory.Exists(output) &&
            Directory.EnumerateFileSystemEntries(output).Any())
        {
            throw new IOException(
                "The comparison directory already contains files.");
        }

        Directory.CreateDirectory(output);
        RejectPathChain(output);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            result,
            CreateJsonOptions());
        byte[] markdown = Encoding.UTF8.GetBytes(BuildMarkdown(result));
        ValidateSize(json, "JSON");
        ValidateSize(markdown, "Markdown");

        string jsonPath = Path.Combine(output, "comparison-report.json");
        string markdownPath = Path.Combine(
            output,
            "comparison-report.md");
        WritePairAtomic(jsonPath, json, markdownPath, markdown);

        return new EvaluationComparisonReportWriteResult(
            jsonPath,
            markdownPath);
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

    private static string BuildMarkdown(EvaluationComparisonResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Local-AI deterministic candidate comparison");
        builder.AppendLine();
        builder.AppendLine($"- Comparison: `{result.ComparisonId}`");
        builder.AppendLine(
            $"- Recommendation: **{Recommendation(result.Recommendation)}**");
        AppendSource(builder, "Baseline", result.Baseline);
        AppendSource(builder, "Candidate", result.Candidate);
        builder.AppendLine(
            $"- Duration delta: {result.DurationDeltaMilliseconds.ToString(CultureInfo.InvariantCulture)} ms");
        builder.AppendLine(
            $"- Duration percent delta: {Percent(result.DurationPercentDelta)}");
        builder.AppendLine();
        builder.AppendLine("## Eligibility gates");
        builder.AppendLine();
        builder.AppendLine("| Gate | Result | Evidence |");
        builder.AppendLine("|---|---|---|");

        foreach (EvaluationComparisonGate gate in result.Gates)
        {
            builder.AppendLine(
                $"| {gate.Name} | {(gate.Passed ? "Passed" : "Failed")} | " +
                $"{Escape(gate.Evidence)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Metric deltas");
        builder.AppendLine();
        builder.AppendLine(
            "| Metric | Baseline | Candidate | Delta | Direction |");
        builder.AppendLine("|---|---:|---:|---:|---|");

        foreach (EvaluationMetricDelta metric in result.Metrics)
        {
            builder.AppendLine(
                $"| {metric.Name} | {Rate(metric.BaselineRate)} | " +
                $"{Rate(metric.CandidateRate)} | " +
                $"{SignedRate(metric.RateDelta)} | {metric.Direction} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Case results");
        builder.AppendLine();
        builder.AppendLine("| Case | Baseline | Candidate | Direction |");
        builder.AppendLine("|---|---|---|---|");

        foreach (EvaluationCaseComparison item in result.Cases)
        {
            builder.AppendLine(
                $"| {item.CaseId} | {Outcome(item.BaselinePassed)} | " +
                $"{Outcome(item.CandidatePassed)} | {item.Direction} |");
        }

        builder.AppendLine();
        builder.AppendLine($"> {result.Advisory}");
        return builder.ToString();
    }

    private static void AppendSource(
        StringBuilder builder,
        string label,
        EvaluationComparisonSource source)
    {
        builder.AppendLine(
            $"- {label}: `{source.SourcePath}` (SHA-256 `{source.Sha256}`, " +
            $"run `{source.RunId}`, commit `{source.ProductCommit}`, " +
            $"model `{source.ModelLabel}`, profile `{source.ProfileLabel}`)");
    }

    private static string Recommendation(
        EvaluationComparisonRecommendation recommendation) =>
        recommendation switch
        {
            EvaluationComparisonRecommendation.EligibleForUserReview =>
                "Eligible for user review",
            _ => "Not recommended"
        };

    private static string Outcome(bool? value) => value switch
    {
        true => "Passed",
        false => "Failed",
        null => "Missing"
    };

    private static string Percent(decimal? value) => value.HasValue
        ? value.Value.ToString(
              "+0.0000;-0.0000;0.0000",
              CultureInfo.InvariantCulture) + "%"
        : "Not defined";

    private static string Rate(decimal value) => value.ToString(
        "0.0000",
        CultureInfo.InvariantCulture);

    private static string SignedRate(decimal value) => value.ToString(
        "+0.0000;-0.0000;0.0000",
        CultureInfo.InvariantCulture);

    private static string Escape(string value) => value.Replace('|', '/');

    private static void ValidateSize(byte[] content, string label)
    {
        if (content.Length == 0 ||
            content.Length > MaximumComparisonReportBytes)
        {
            throw new InvalidDataException(
                $"The {label} comparison report is empty or exceeds the " +
                "bounded limit.");
        }
    }

    private static void WritePairAtomic(
        string jsonPath,
        byte[] json,
        string markdownPath,
        byte[] markdown)
    {
        string jsonTemporary = jsonPath + ".tmp";
        string markdownTemporary = markdownPath + ".tmp";
        bool jsonMoved = false;

        try
        {
            File.WriteAllBytes(jsonTemporary, json);
            File.WriteAllBytes(markdownTemporary, markdown);
            File.Move(jsonTemporary, jsonPath, false);
            jsonMoved = true;
            File.Move(markdownTemporary, markdownPath, false);
        }
        catch
        {
            TryDelete(jsonTemporary);
            TryDelete(markdownTemporary);

            if (jsonMoved)
            {
                TryDelete(jsonPath);
            }

            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Cleanup must not hide the original output failure.
        }
    }

    private static void RejectPathChain(string path)
    {
        DirectoryInfo? current = new(path);

        while (current is not null)
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException(
                    "Comparison output cannot use a linked directory.");
            }

            current = current.Parent;
        }
    }
}
