using System.Text;
using LocalAI.Core.Models;
using LocalAI.Infrastructure.Evaluation;

namespace LocalAI.Tests;

public sealed class LocalEvaluationReportLoaderTests
{
    [Fact]
    public void Load_ValidReportReturnsHashProvenanceAndCaseIdentity()
    {
        using TemporaryDirectory temporary = new();
        string report = WriteReport(temporary.Path);

        EvaluationReportDocument document =
            new LocalEvaluationReportLoader().Load(temporary.Path, report);

        Assert.Equal("run/evaluation-report.json", document.SourcePath);
        Assert.Equal(64, document.Sha256.Length);
        Assert.Equal(64, document.CaseSetIdentity.Length);
        Assert.Equal("baseline-run", document.Report.RunId);
    }

    [Fact]
    public void Load_Utf8BomReportIsAccepted()
    {
        using TemporaryDirectory temporary = new();
        byte[] report = EvaluationComparisonTestData.Serialize(
            EvaluationComparisonTestData.CreateReport());
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. report];
        string path = WriteBytes(temporary.Path, withBom);

        EvaluationReportDocument document =
            new LocalEvaluationReportLoader().Load(temporary.Path, path);

        Assert.Equal("baseline-run", document.Report.RunId);
    }

    [Fact]
    public void Load_MalformedJsonIsRejected()
    {
        using TemporaryDirectory temporary = new();
        string path = WriteBytes(
            temporary.Path,
            Encoding.UTF8.GetBytes("{ malformed"));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportLoader().Load(
                temporary.Path,
                path));

        Assert.Contains("malformed", exception.Message);
    }

    [Fact]
    public void Load_UnsupportedSchemaIsRejected()
    {
        using TemporaryDirectory temporary = new();
        EvaluationRunReport report =
            EvaluationComparisonTestData.CreateReport() with
            {
                SchemaVersion = 2
            };
        string path = WriteReport(temporary.Path, report);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportLoader().Load(
                temporary.Path,
                path));

        Assert.Contains("unsupported schema version", exception.Message);

        report = EvaluationComparisonTestData.CreateReport() with
        {
            EvaluatorSchemaVersion = "unsupported-evaluator"
        };
        path = WriteReport(temporary.Path, report);

        exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportLoader().Load(
                temporary.Path,
                path));

        Assert.Contains("unsupported evaluator schema", exception.Message);
    }

    [Fact]
    public void Load_DuplicateCaseIdentifiersAreRejected()
    {
        using TemporaryDirectory temporary = new();
        EvaluationRunReport report =
            EvaluationComparisonTestData.CreateReport();
        report = report with
        {
            Cases =
            [
                report.Cases[0],
                report.Cases[0],
                .. report.Cases.Skip(1)
            ]
        };
        string path = WriteReport(temporary.Path, report);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportLoader().Load(
                temporary.Path,
                path));

        Assert.Contains("duplicate case ID", exception.Message);
    }

    [Fact]
    public void Load_InconsistentMetricIsRejected()
    {
        using TemporaryDirectory temporary = new();
        EvaluationRunReport report =
            EvaluationComparisonTestData.CreateReport();
        EvaluationMetricSummary first = report.Metrics[0] with { Passed = 0 };
        report = report with
        {
            Metrics = [first, .. report.Metrics.Skip(1)]
        };
        string path = WriteReport(temporary.Path, report);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportLoader().Load(
                temporary.Path,
                path));

        Assert.Contains("inconsistent", exception.Message);
    }

    [Fact]
    public void Load_OutsideRootCasePathIsRejected()
    {
        using TemporaryDirectory temporary = new();
        EvaluationRunReport report =
            EvaluationComparisonTestData.CreateReport();
        EvaluationCaseResult first = report.Cases[0] with
        {
            FixturePath = "../outside.case.json"
        };
        report = report with
        {
            Cases = [first, .. report.Cases.Skip(1)]
        };
        string path = WriteReport(temporary.Path, report);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportLoader().Load(
                temporary.Path,
                path));

        Assert.Contains("invalid fixture path", exception.Message);
    }

    [Fact]
    public void Load_ReportOutsideEvaluationRootIsRejected()
    {
        using TemporaryDirectory temporary = new();
        using TemporaryDirectory outside = new();
        string path = WriteReport(outside.Path);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportLoader().Load(
                temporary.Path,
                path));

        Assert.Contains("outside", exception.Message);
    }

    [Fact]
    public void Load_LinkedEvaluationRootIsRejected()
    {
        using TemporaryDirectory temporary = new();
        using TemporaryDirectory container = new();
        WriteReport(temporary.Path);
        string linkedRoot = System.IO.Path.Combine(
            container.Path,
            "linked-evaluations");
        CreateDirectoryJunction(linkedRoot, temporary.Path);
        string linkedReport = System.IO.Path.Combine(
            linkedRoot,
            "run",
            "evaluation-report.json");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportLoader().Load(
                linkedRoot,
                linkedReport));

        Assert.Contains("linked", exception.Message);
    }

    [Fact]
    public void Load_OversizedReportIsRejected()
    {
        using TemporaryDirectory temporary = new();
        string path = WriteBytes(
            temporary.Path,
            new byte[checked(
                (int)LocalEvaluationReportLoader.MaximumReportBytes + 1)]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new LocalEvaluationReportLoader().Load(
                temporary.Path,
                path));

        Assert.Contains("bounded limit", exception.Message);
    }

    private static string WriteReport(
        string root,
        EvaluationRunReport? report = null) => WriteBytes(
        root,
        EvaluationComparisonTestData.Serialize(
            report ?? EvaluationComparisonTestData.CreateReport()));

    private static string WriteBytes(string root, byte[] content)
    {
        string directory = System.IO.Path.Combine(root, "run");
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(
            directory,
            "evaluation-report.json");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static void CreateDirectoryJunction(
        string junctionPath,
        string targetPath)
    {
        System.Diagnostics.ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            $"New-Item -ItemType Junction -Path " +
            $"'{junctionPath.Replace("'", "''")}' -Target " +
            $"'{targetPath.Replace("'", "''")}' | Out-Null");

        using System.Diagnostics.Process process =
            System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"local-ai-comparison-loader-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // Cleanup must not hide test results.
            }
        }
    }
}
