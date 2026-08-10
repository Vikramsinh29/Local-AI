using LocalAI.Core.Models;
using LocalAI.Infrastructure.Evaluation;

namespace LocalAI.Tests;

public sealed class JsonEvaluationFixtureLoaderTests
{
    [Fact]
    public void Load_ValidFixtureReturnsStableDefinition()
    {
        using FixtureDirectory fixture = CreateValidFixture();

        IReadOnlyList<EvaluationCaseDefinition> cases =
            new JsonEvaluationFixtureLoader().Load(fixture.Path);

        EvaluationCaseDefinition item = Assert.Single(cases);
        Assert.Equal("file-selection-basic", item.Id);
        Assert.Equal("case.case.json", item.FixturePath);
    }

    [Fact]
    public void Load_DuplicateIdentifiersAreRejected()
    {
        using FixtureDirectory fixture = CreateValidFixture();
        fixture.Write("duplicate.case.json", ValidCaseJson);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new JsonEvaluationFixtureLoader().Load(fixture.Path));

        Assert.Contains("Duplicate", exception.Message);
    }

    [Fact]
    public void Load_UnsupportedSchemaIsRejected()
    {
        using FixtureDirectory fixture = CreateValidFixture();
        fixture.Write(
            "case.case.json",
            ValidCaseJson.Replace(
                "\"schemaVersion\": 1",
                "\"schemaVersion\": 2",
                StringComparison.Ordinal));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new JsonEvaluationFixtureLoader().Load(fixture.Path));

        Assert.Contains("unsupported schema version", exception.Message);
    }

    [Fact]
    public void Load_MalformedJsonIsRejected()
    {
        using FixtureDirectory fixture = CreateValidFixture();
        fixture.Write("case.case.json", "{ malformed");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new JsonEvaluationFixtureLoader().Load(fixture.Path));

        Assert.Contains("malformed", exception.Message);
    }

    [Fact]
    public void Load_OutsideRootEvidencePathIsRejected()
    {
        using FixtureDirectory fixture = CreateValidFixture();
        fixture.Write(
            "case.case.json",
            ValidCaseJson.Replace(
                "inputs/request.txt",
                "../request.txt",
                StringComparison.Ordinal));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new JsonEvaluationFixtureLoader().Load(fixture.Path));

        Assert.Contains("outside-root", exception.Message);
    }

    [Fact]
    public void Load_MissingEvidenceIsRejected()
    {
        using FixtureDirectory fixture = CreateValidFixture();
        File.Delete(System.IO.Path.Combine(
            fixture.Path,
            "responses/output.txt"));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new JsonEvaluationFixtureLoader().Load(fixture.Path));

        Assert.Contains("missing or oversized", exception.Message);
    }

    [Fact]
    public void Load_OversizedDefinitionIsRejected()
    {
        using FixtureDirectory fixture = CreateValidFixture();
        fixture.Write(
            "case.case.json",
            new string(' ', (int)
                JsonEvaluationFixtureLoader.MaximumDefinitionBytes + 1));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new JsonEvaluationFixtureLoader().Load(fixture.Path));

        Assert.Contains("oversized", exception.Message);
    }

    [Fact]
    public void Load_AmbiguousCategoryInputsAreRejected()
    {
        using FixtureDirectory fixture = CreateValidFixture();
        fixture.Write(
            "case.case.json",
            ValidCaseJson.Replace(
                "\"expectedCandidateFiles\": [\"src/Program.cs\"]",
                "\"requiredEvidencePaths\": [\"AGENTS.md\"], " +
                "\"expectedCandidateFiles\": [\"src/Program.cs\"]",
                StringComparison.Ordinal));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => new JsonEvaluationFixtureLoader().Load(fixture.Path));

        Assert.Contains("ambiguous", exception.Message);
    }

    private static FixtureDirectory CreateValidFixture()
    {
        FixtureDirectory fixture = new();
        fixture.Write("inputs/request.txt", "Select the smallest file.");
        fixture.Write("responses/output.txt", "CANDIDATE_FILE: src/Program.cs");
        fixture.Write("case.case.json", ValidCaseJson);
        return fixture;
    }

    private const string ValidCaseJson =
        """
        {
          "schemaVersion": 1,
          "id": "file-selection-basic",
          "category": "fileSelection",
          "inputEvidencePath": "inputs/request.txt",
          "recordedOutputPath": "responses/output.txt",
          "repositoryRootPath": null,
          "allowedSourcePaths": null,
          "expected": {
            "expectedCandidateFiles": ["src/Program.cs"]
          },
          "safetyLabels": ["offline"]
        }
        """;

    private sealed class FixtureDirectory : IDisposable
    {
        public FixtureDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"local-ai-loader-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string relativePath, string content)
        {
            string fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
