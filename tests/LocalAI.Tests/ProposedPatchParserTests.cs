using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Tests;

public sealed class ProposedPatchParserTests : IDisposable
{
    private const string ProgramPath = "src/Program.cs";
    private const string OriginalText = "return 42;";
    private const string ReplacementText = "return 43;";

    private readonly string _repositoryRoot;

    public ProposedPatchParserTests()
    {
        _repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"LocalAI-Patch-{Guid.NewGuid():N}");

        Directory.CreateDirectory(
            Path.Combine(_repositoryRoot, "src"));
        File.WriteAllText(
            Path.Combine(_repositoryRoot, "src", "Program.cs"),
            "public static int GetValue() => return 42;\n");
    }

    [Fact]
    public void Parse_AcceptsGroundedReplacementAndBuildsPreview()
    {
        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            BuildProposal(
                @"src\Program.cs",
                OriginalText,
                ReplacementText),
            _repositoryRoot,
            [ProgramPath]);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal("Change the return value.", result.Preview!.Summary);
        ProposedPatchFile file = Assert.Single(result.Preview.Files);
        Assert.Equal(
            Path.Combine("src", "Program.cs"),
            file.RelativePath);
        Assert.Equal(1, file.AddedLineCount);
        Assert.Equal(1, file.RemovedLineCount);
        Assert.Equal(OriginalText, file.OriginalText);
        Assert.Equal(ReplacementText, file.ReplacementText);
        Assert.Equal(64, file.SourceSha256.Length);
        Assert.Contains("--- a/src/Program.cs", result.Preview.UnifiedDiff);
        Assert.Contains("-public static int", result.Preview.UnifiedDiff);
        Assert.Contains("return 43;", result.Preview.UnifiedDiff);
    }

    [Fact]
    public void Parse_AcceptsUniqueMatchWithDifferentLeadingIndentation()
    {
        File.WriteAllText(
            Path.Combine(_repositoryRoot, "src", "Program.cs"),
            "        builder.AppendLine(\n" +
            "            \"return 42;\");\n");
        const string original =
            "builder.AppendLine(\n" +
            "    \"return 42;\");";
        const string replacement =
            "builder.AppendLine(\n" +
            "    \"return 43;\");";

        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            BuildProposal(ProgramPath, original, replacement),
            _repositoryRoot,
            [ProgramPath]);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "+            \"return 43;\");",
            result.Preview!.UnifiedDiff);
    }

    [Fact]
    public void Parse_AcceptsOneProposalWithSurroundingPlainText()
    {
        string response =
            "Preview only -- not applied.\n" +
            BuildProposal(
                ProgramPath,
                OriginalText,
                ReplacementText) +
            "\nReview the preview before approving any future action.";

        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            response,
            _repositoryRoot,
            [ProgramPath]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "Change the return value.",
            result.Preview!.Summary);
    }

    [Fact]
    public void Parse_RejectsEmptyResponse()
    {
        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            "   ",
            _repositoryRoot,
            [ProgramPath]);

        Assert.False(result.IsSuccess);
        Assert.Contains("empty", result.Error!);
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("C:/outside.cs")]
    [InlineData(".local-ai/verification/output.cs")]
    [InlineData("src/bin/Generated.cs")]
    [InlineData("src/.env")]
    [InlineData("src/Program.cs.")]
    [InlineData("src/Program.cs ")]
    [InlineData("src/CON.cs")]
    public void Parse_RejectsUnsafePaths(string relativePath)
    {
        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            BuildProposal(
                relativePath,
                OriginalText,
                ReplacementText),
            _repositoryRoot,
            [relativePath]);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_RejectsOverlongPath()
    {
        string relativePath = $"src/{new string('a', 510)}.cs";

        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            BuildProposal(
                relativePath,
                OriginalText,
                ReplacementText),
            _repositoryRoot,
            [relativePath]);

        Assert.False(result.IsSuccess);
        Assert.Contains("unsafe", result.Error!);
    }

    [Fact]
    public void Parse_RejectsPathThatWasNotSelectedAsEvidence()
    {
        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            BuildProposal(
                "src/Other.cs",
                OriginalText,
                ReplacementText),
            _repositoryRoot,
            [ProgramPath]);

        Assert.False(result.IsSuccess);
        Assert.Contains("not selected", result.Error!);
    }

    [Fact]
    public void Parse_RejectsOriginalTextMissingFromSource()
    {
        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            BuildProposal(
                ProgramPath,
                "return 99;",
                ReplacementText),
            _repositoryRoot,
            [ProgramPath]);

        Assert.False(result.IsSuccess);
        Assert.Contains("does not exist", result.Error!);
    }

    [Fact]
    public void Parse_RejectsAmbiguousOriginalText()
    {
        File.WriteAllText(
            Path.Combine(_repositoryRoot, "src", "Program.cs"),
            "return 42;\nreturn 42;\n");

        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            BuildProposal(
                ProgramPath,
                OriginalText,
                ReplacementText),
            _repositoryRoot,
            [ProgramPath]);

        Assert.False(result.IsSuccess);
        Assert.Contains("ambiguous", result.Error!);
    }

    [Fact]
    public void Parse_RejectsMarkdownWrappedResponse()
    {
        string response =
            $"```text{Environment.NewLine}" +
            BuildProposal(
                ProgramPath,
                OriginalText,
                ReplacementText) +
            $"{Environment.NewLine}```";

        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            response,
            _repositoryRoot,
            [ProgramPath]);

        Assert.False(result.IsSuccess);
        Assert.Contains("Markdown fences", result.Error!);
    }

    [Fact]
    public void Parse_RejectsDuplicateProposalEnvelopes()
    {
        string proposal = BuildProposal(
            ProgramPath,
            OriginalText,
            ReplacementText);

        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            proposal + "\n" + proposal,
            _repositoryRoot,
            [ProgramPath]);

        Assert.False(result.IsSuccess);
        Assert.Contains("one complete", result.Error!);
    }

    [Fact]
    public void Parse_RejectsDuplicateFileBlocks()
    {
        string first = BuildProposal(
            ProgramPath,
            OriginalText,
            ReplacementText);
        string duplicateBlock =
            $"<<<FILE:{ProgramPath}>>>\n" +
            "<<<ORIGINAL>>>\n" +
            "return 42;\n" +
            "<<<REPLACEMENT>>>\n" +
            "return 44;\n" +
            "<<<END_FILE>>>\n";
        string response = first.Replace(
            ProposedPatchParser.EndMarker,
            duplicateBlock + ProposedPatchParser.EndMarker,
            StringComparison.Ordinal);

        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            response,
            _repositoryRoot,
            [ProgramPath]);

        Assert.False(result.IsSuccess);
        Assert.Contains("more than once", result.Error!);
    }

    [Fact]
    public void Parse_RejectsMalformedReplacementBlock()
    {
        string response = BuildProposal(
                ProgramPath,
                OriginalText,
                ReplacementText)
            .Replace(
                ProposedPatchParser.ReplacementMarker,
                "<<<UPDATED>>>",
                StringComparison.Ordinal);

        ProposedPatchParseResult result = ProposedPatchParser.Parse(
            response,
            _repositoryRoot,
            [ProgramPath]);

        Assert.False(result.IsSuccess);
        Assert.Contains("malformed", result.Error!);
    }

    private static string BuildProposal(
        string relativePath,
        string original,
        string replacement)
    {
        return
            "<<<LOCAL_AI_PATCH_V1>>>\n" +
            "SUMMARY:\n" +
            "Change the return value.\n" +
            $"<<<FILE:{relativePath}>>>\n" +
            "<<<ORIGINAL>>>\n" +
            original + "\n" +
            "<<<REPLACEMENT>>>\n" +
            replacement + "\n" +
            "<<<END_FILE>>>\n" +
            "<<<END_LOCAL_AI_PATCH>>>";
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
        catch
        {
            // Cleanup must not hide test results.
        }
    }
}
