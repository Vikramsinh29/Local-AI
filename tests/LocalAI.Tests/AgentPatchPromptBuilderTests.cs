using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Tests;

public sealed class AgentPatchPromptBuilderTests
{
    [Fact]
    public void Build_RequiresStructuredPreviewOnlyResponse()
    {
        RepositoryContextFile file = new(
            "src\\Program.cs",
            "return 42;",
            10);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        VerificationRunResult verification = new(
            VerificationToolKind.DotnetTest,
            "dotnet test Sample.slnx -c Release --no-build",
            now,
            now.AddSeconds(1),
            ExitCode: 0,
            WasCancelled: false,
            Output: "Passed: 1");

        string prompt = AgentPatchPromptBuilder.Build(
            "Change the return value.",
            "Sample",
            "Git repository",
            [file],
            maximumContextTokens: 1_000,
            verificationRuns: [verification]);

        Assert.Contains("controlled patch-preview mode", prompt);
        Assert.Contains(ProposedPatchParser.StartMarker, prompt);
        Assert.Contains(ProposedPatchParser.OriginalMarker, prompt);
        Assert.Contains(ProposedPatchParser.ReplacementMarker, prompt);
        Assert.Contains(ProposedPatchParser.EndFileMarker, prompt);
        Assert.Contains(ProposedPatchParser.EndMarker, prompt);
        Assert.Contains("src/Program.cs", prompt);
        Assert.DoesNotContain("src\\Program.cs", prompt);
        Assert.Contains("Passed: 1", prompt);
        Assert.Contains("Preview only", prompt);
        Assert.Contains("Do not use Markdown fences", prompt);
    }

    [Fact]
    public void Build_RejectsRequestWithoutSourceEvidence()
    {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => AgentPatchPromptBuilder.Build(
                    "Propose a patch.",
                    "Sample",
                    "Git repository",
                    [],
                    maximumContextTokens: 1_000));

        Assert.Contains(
            "at least one source file",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }
}
