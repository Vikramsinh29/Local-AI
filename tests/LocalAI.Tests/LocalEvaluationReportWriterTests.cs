using System.Text.Json;
using LocalAI.Core.Evaluation;
using LocalAI.Core.Models;
using LocalAI.Infrastructure.Evaluation;

namespace LocalAI.Tests;

public sealed class LocalEvaluationReportWriterTests
{
    [Fact]
    public void Write_CreatesBoundedJsonAndMarkdownReports()
    {
        using TemporaryDirectory temporary = new();
        string root = System.IO.Path.Combine(temporary.Path, "evaluations");
        string output = System.IO.Path.Combine(root, "run-001");

        EvaluationReportWriteResult result =
            new LocalEvaluationReportWriter(root).Write(
                output,
                CreateReport());

        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.MarkdownPath));
        using JsonDocument json = JsonDocument.Parse(
            File.ReadAllText(result.JsonPath));
        Assert.Equal("run-001",
            json.RootElement.GetProperty("runId").GetString());
        Assert.Contains(
            "# Local-AI deterministic evaluation report",
            File.ReadAllText(result.MarkdownPath));
    }

    [Fact]
    public void Write_NonEmptyRunDirectoryIsRejected()
    {
        using TemporaryDirectory temporary = new();
        string root = System.IO.Path.Combine(temporary.Path, "evaluations");
        string output = System.IO.Path.Combine(root, "run-001");
        Directory.CreateDirectory(output);
        File.WriteAllText(System.IO.Path.Combine(output, "existing.txt"), "x");

        Assert.Throws<IOException>(
            () => new LocalEvaluationReportWriter(root).Write(
                output,
                CreateReport()));
    }

    [Fact]
    public void Write_NestedOutputDirectoryIsRejected()
    {
        using TemporaryDirectory temporary = new();
        string root = System.IO.Path.Combine(temporary.Path, "evaluations");
        string output = System.IO.Path.Combine(root, "nested", "run-001");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportWriter(root).Write(
                output,
                CreateReport()));

        Assert.Contains("direct run directory", exception.Message);
    }

    private static EvaluationRunReport CreateReport()
    {
        EvaluationCaseResult result = new(
            "case-001",
            EvaluationCategory.FileSelection,
            "case-001.case.json",
            "inputs/request.txt",
            "responses/output.txt",
            true,
            new Dictionary<string, bool>
            {
                ["fileSelectionPrecision"] = true
            },
            [],
            ["offline"]);
        DateTimeOffset started =
            new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

        return new EvaluationRunReport(
            1,
            "run-001",
            DeterministicEvaluationRunner.EvaluatorSchemaVersion,
            "commit-abc",
            "recorded-fixture",
            "deterministic",
            "fixtures",
            started,
            started.AddMilliseconds(10),
            10,
            [result],
            [new EvaluationMetricSummary(
                "fileSelectionPrecision",
                1,
                1,
                1m)]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"local-ai-writer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
