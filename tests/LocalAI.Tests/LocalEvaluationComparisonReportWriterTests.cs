using System.Text.Json;
using LocalAI.Core.Evaluation;
using LocalAI.Core.Models;
using LocalAI.Infrastructure.Evaluation;

namespace LocalAI.Tests;

public sealed class LocalEvaluationComparisonReportWriterTests
{
    [Fact]
    public void Write_CreatesBoundedJsonAndMarkdownReports()
    {
        using TemporaryDirectory temporary = new();
        EvaluationComparisonResult comparison = CreateComparison();

        EvaluationComparisonReportWriteResult result =
            new LocalEvaluationComparisonReportWriter(temporary.Path).Write(
                comparison.ComparisonId,
                comparison);

        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.MarkdownPath));
        using JsonDocument json = JsonDocument.Parse(
            File.ReadAllText(result.JsonPath));
        Assert.Equal(
            "eligibleForUserReview",
            json.RootElement.GetProperty("recommendation").GetString());
        Assert.Contains(
            "Eligible for user review",
            File.ReadAllText(result.MarkdownPath));
        Assert.Contains(
            comparison.Baseline.Sha256,
            File.ReadAllText(result.MarkdownPath));
    }

    [Fact]
    public void Write_MismatchedComparisonIdIsRejected()
    {
        using TemporaryDirectory temporary = new();
        EvaluationComparisonResult comparison = CreateComparison();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationComparisonReportWriter(
                temporary.Path).Write("different-id", comparison));

        Assert.Contains("does not match", exception.Message);
    }

    [Fact]
    public void Write_NonEmptyComparisonDirectoryIsRejected()
    {
        using TemporaryDirectory temporary = new();
        EvaluationComparisonResult comparison = CreateComparison();
        string output = System.IO.Path.Combine(
            temporary.Path,
            "comparisons",
            comparison.ComparisonId);
        Directory.CreateDirectory(output);
        File.WriteAllText(System.IO.Path.Combine(output, "existing.txt"), "x");

        Assert.Throws<IOException>(
            () => new LocalEvaluationComparisonReportWriter(
                temporary.Path).Write(
                    comparison.ComparisonId,
                    comparison));
    }

    [Fact]
    public void Write_NotRecommendedResultIsLabeledHonestly()
    {
        using TemporaryDirectory temporary = new();
        EvaluationRunReport baseline =
            EvaluationComparisonTestData.CreateReport();
        EvaluationRunReport candidate =
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b",
                plan: false);
        EvaluationComparisonResult comparison =
            new DeterministicEvaluationComparisonService().Compare(new(
                "comparison-002",
                DateTimeOffset.UtcNow,
                EvaluationComparisonTestData.Document(baseline, "aaaa"),
                EvaluationComparisonTestData.Document(candidate, "bbbb")));

        EvaluationComparisonReportWriteResult written =
            new LocalEvaluationComparisonReportWriter(temporary.Path).Write(
                comparison.ComparisonId,
                comparison);

        Assert.Contains(
            "Not recommended",
            File.ReadAllText(written.MarkdownPath));
    }

    private static EvaluationComparisonResult CreateComparison()
    {
        EvaluationRunReport baseline =
            EvaluationComparisonTestData.CreateReport(plan: false);
        EvaluationRunReport candidate =
            EvaluationComparisonTestData.CreateReport(
                runId: "candidate-run",
                modelLabel: "model-b",
                durationMilliseconds: 1_100);
        return new DeterministicEvaluationComparisonService().Compare(new(
            "comparison-001",
            new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero),
            EvaluationComparisonTestData.Document(baseline, "aaaa"),
            EvaluationComparisonTestData.Document(candidate, "bbbb")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"local-ai-comparison-writer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
