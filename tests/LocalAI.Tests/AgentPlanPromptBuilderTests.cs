using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Tests;

public sealed class AgentPlanPromptBuilderTests
{
    [Fact]
    public void Build_IncludesReadOnlyBoundaryAndEvidence()
    {
        RepositoryContextFile file =
            new("src/Program.cs", "return 42;", 10);

        string prompt = AgentPlanPromptBuilder.Build(
            "Plan the requested feature.",
            "Sample",
            "Git repository • 1 solution file(s) • 1 project file(s)",
            [file],
            maximumContextTokens: 1_000);

        Assert.Contains("controlled read-only agent mode", prompt);
        Assert.Contains("Do not edit files", prompt);
        Assert.Contains("Repository: Sample", prompt);
        Assert.Contains("2. Evidence used", prompt);
        Assert.Contains("7. Safety boundary", prompt);
        Assert.Contains("--- FILE: src/Program.cs ---", prompt);
        Assert.EndsWith("Plan the requested feature.", prompt);
    }

    [Fact]
    public void Build_RejectsContextOverSelectedBudget()
    {
        string content = new('a', 4_001);
        RepositoryContextFile file =
            new("large.txt", content, content.Length);

        Assert.Throws<InvalidOperationException>(
            () => AgentPlanPromptBuilder.Build(
                "Plan this work.",
                "Sample",
                "Git repository",
                [file],
                maximumContextTokens: 1_000));
    }
}
