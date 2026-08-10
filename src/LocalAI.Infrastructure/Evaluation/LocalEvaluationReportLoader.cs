using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalAI.Core.Evaluation;
using LocalAI.Core.Interfaces;
using LocalAI.Core.Models;

namespace LocalAI.Infrastructure.Evaluation;

public sealed class LocalEvaluationReportLoader : IEvaluationReportLoader
{
    public const int SupportedReportSchemaVersion = 1;
    public const long MaximumReportBytes = 2_097_152;
    public const int MaximumCases = 100;
    public const long MaximumDurationMilliseconds = 86_400_000;

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public EvaluationReportDocument Load(
        string evaluationRoot,
        string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(evaluationRoot));
        string reportFile = Path.GetFullPath(reportPath);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Evaluation root was not found: {root}");
        }

        RejectPathChain(root, root, "Evaluation root");
        EnsureContained(root, reportFile);

        if (!File.Exists(reportFile))
        {
            throw new FileNotFoundException(
                "Evaluation report was not found.",
                reportFile);
        }

        RejectPathChain(root, reportFile, "Evaluation report");
        FileInfo information = new(reportFile);

        if (information.Length == 0 ||
            information.Length > MaximumReportBytes)
        {
            throw new InvalidDataException(
                "Evaluation report is empty or exceeds the bounded limit.");
        }

        byte[] content = File.ReadAllBytes(reportFile);
        EvaluationRunReport report = Deserialize(content, reportFile);
        ValidateReport(report);
        string sha256 = Convert.ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();

        return new EvaluationReportDocument(
            Path.GetRelativePath(root, reportFile).Replace('\\', '/'),
            sha256,
            BuildCaseSetIdentity(report),
            report);
    }

    private static EvaluationRunReport Deserialize(
        byte[] content,
        string reportFile)
    {
        ReadOnlySpan<byte> json = content;

        if (json.Length >= 3 &&
            json[0] == 0xEF &&
            json[1] == 0xBB &&
            json[2] == 0xBF)
        {
            json = json[3..];
        }

        try
        {
            return JsonSerializer.Deserialize<EvaluationRunReport>(
                       json,
                       JsonOptions) ??
                   throw new InvalidDataException(
                       $"Evaluation report is empty: " +
                       $"{Path.GetFileName(reportFile)}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Evaluation report is malformed: " +
                $"{Path.GetFileName(reportFile)}",
                exception);
        }
    }

    private static void ValidateReport(EvaluationRunReport report)
    {
        if (report.SchemaVersion != SupportedReportSchemaVersion)
        {
            throw new InvalidDataException(
                $"Evaluation report uses unsupported schema version " +
                $"{report.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(report.RunId) ||
            string.IsNullOrWhiteSpace(report.EvaluatorSchemaVersion) ||
            string.IsNullOrWhiteSpace(report.ProductCommit) ||
            string.IsNullOrWhiteSpace(report.ModelLabel) ||
            string.IsNullOrWhiteSpace(report.ProfileLabel) ||
            string.IsNullOrWhiteSpace(report.FixtureRoot) ||
            report.Cases is null ||
            report.Metrics is null)
        {
            throw new InvalidDataException(
                "Evaluation report provenance is incomplete.");
        }

        if (!report.EvaluatorSchemaVersion.Equals(
                DeterministicEvaluationRunner.EvaluatorSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Evaluation report uses unsupported evaluator schema " +
                $"'{report.EvaluatorSchemaVersion}'.");
        }

        if (report.Cases.Count == 0 || report.Cases.Count > MaximumCases)
        {
            throw new InvalidDataException(
                $"Evaluation report must contain between 1 and " +
                $"{MaximumCases} cases.");
        }

        if (report.DurationMilliseconds < 0 ||
            report.DurationMilliseconds > MaximumDurationMilliseconds ||
            report.CompletedAtUtc < report.StartedAtUtc)
        {
            throw new InvalidDataException(
                "Evaluation report duration is invalid.");
        }

        ValidateCases(report.Cases);
        ValidateMetrics(report.Cases, report.Metrics);
    }

    private static void ValidateCases(
        IReadOnlyList<EvaluationCaseResult> cases)
    {
        HashSet<string> identifiers = new(StringComparer.Ordinal);

        foreach (EvaluationCaseResult item in cases)
        {
            if (string.IsNullOrWhiteSpace(item.CaseId) ||
                !identifiers.Add(item.CaseId))
            {
                throw new InvalidDataException(
                    "Evaluation report contains a missing or duplicate case ID.");
            }

            ValidateRelativePath(item.FixturePath, item.CaseId, "fixture");
            ValidateRelativePath(
                item.InputEvidencePath,
                item.CaseId,
                "input evidence");
            ValidateRelativePath(
                item.RecordedOutputPath,
                item.CaseId,
                "recorded output");

            if (item.Scores is null ||
                item.Findings is null ||
                item.Findings.Any(string.IsNullOrWhiteSpace) ||
                item.SafetyLabels is null ||
                item.SafetyLabels.Count == 0 ||
                item.SafetyLabels.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException(
                    $"Evaluation case '{item.CaseId}' is invalid, skipped, " +
                    "or unscored.");
            }

            string[] expectedScores = ExpectedScores(item.Category);
            string[] actualScores = item.Scores.Keys
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (!expectedScores.OrderBy(name => name, StringComparer.Ordinal)
                    .SequenceEqual(actualScores, StringComparer.Ordinal) ||
                item.Passed != item.Scores.Values.All(value => value))
            {
                throw new InvalidDataException(
                    $"Evaluation case '{item.CaseId}' is invalid, skipped, " +
                    "or unscored.");
            }
        }
    }

    private static void ValidateMetrics(
        IReadOnlyList<EvaluationCaseResult> cases,
        IReadOnlyList<EvaluationMetricSummary> metrics)
    {
        if (metrics.Count !=
            DeterministicEvaluationComparisonService.MetricNames.Count ||
            metrics.Select(item => item.Name).Distinct(StringComparer.Ordinal)
                .Count() != metrics.Count)
        {
            throw new InvalidDataException(
                "Evaluation report metrics are missing or duplicated.");
        }

        IReadOnlyDictionary<string, EvaluationMetricSummary> byName = metrics
            .ToDictionary(item => item.Name, StringComparer.Ordinal);

        foreach (string name in
                 DeterministicEvaluationComparisonService.MetricNames)
        {
            if (!byName.TryGetValue(name, out EvaluationMetricSummary? metric))
            {
                throw new InvalidDataException(
                    $"Evaluation report metric is missing: {name}");
            }

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

            if (values.Length == 0 ||
                metric.Passed != passed ||
                metric.Total != values.Length ||
                metric.Rate != rate)
            {
                throw new InvalidDataException(
                    $"Evaluation report metric '{name}' is incomplete or " +
                    "inconsistent with its case scores.");
            }
        }
    }

    private static string[] ExpectedScores(EvaluationCategory category) =>
        category switch
        {
            EvaluationCategory.GroundedPlanning =>
                ["planCorrectness", "evidenceGrounding"],
            EvaluationCategory.EvidenceCitation => ["evidenceGrounding"],
            EvaluationCategory.FileSelection => ["fileSelectionPrecision"],
            EvaluationCategory.StructuredPatchValidity => ["patchValidity"],
            EvaluationCategory.UnsafeActionRejection =>
                ["unsafeActionRejection"],
            _ => throw new InvalidDataException(
                "Evaluation report contains an unsupported case category.")
        };

    private static void ValidateRelativePath(
        string path,
        string caseId,
        string label)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.Replace('\\', '/').Split('/').Any(segment =>
                segment is "" or "." or ".."))
        {
            throw new InvalidDataException(
                $"Evaluation case '{caseId}' contains an invalid {label} " +
                "path.");
        }
    }

    private static string BuildCaseSetIdentity(EvaluationRunReport report)
    {
        StringBuilder builder = new();
        builder.Append("fixture-root|")
            .Append(report.FixtureRoot.Replace('\\', '/'))
            .Append('\n');

        foreach (EvaluationCaseResult item in report.Cases.OrderBy(
                     item => item.CaseId,
                     StringComparer.Ordinal))
        {
            builder.Append(item.CaseId).Append('|')
                .Append(item.Category).Append('|')
                .Append(item.FixturePath.Replace('\\', '/')).Append('|')
                .Append(item.InputEvidencePath.Replace('\\', '/')).Append('|')
                .Append(item.RecordedOutputPath.Replace('\\', '/')).Append('|')
                .AppendJoin(
                    ',',
                    item.SafetyLabels.OrderBy(
                        label => label,
                        StringComparer.Ordinal))
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void EnsureContained(string root, string path)
    {
        string prefix = root + Path.DirectorySeparatorChar;

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Evaluation report path is outside the approved root.");
        }
    }

    private static void RejectPathChain(
        string root,
        string fullPath,
        string label)
    {
        FileSystemInfo? current = File.Exists(fullPath)
            ? new FileInfo(fullPath)
            : new DirectoryInfo(fullPath);
        bool reachedRoot = false;

        while (current is not null)
        {
            if (current.Exists && current.Attributes.HasFlag(
                    FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException(
                    $"{label} is linked or unsafe.");
            }

            if (current.FullName.Equals(
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                reachedRoot = true;
            }

            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }

        if (!reachedRoot)
        {
            throw new InvalidDataException(
                $"{label} containment is ambiguous.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
