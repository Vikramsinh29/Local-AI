using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Tests;

public sealed class AgentPlanPromptBuilderTests
{
    [Fact]
    public void Build_RequiresExactIncludedInstructionAndSourceCitations()
    {
        ProjectInstructionFile agentRules = new(
            ProjectInstructionKind.AgentRules,
            "AGENTS.md",
            24,
            6,
            "ROOT_RULES_ACTIVE",
            ExclusionReason: null);
        ProjectInstructionFile selectedSkill = new(
            ProjectInstructionKind.Skill,
            "skills/review/SKILL.md",
            28,
            7,
            "REVIEW_SKILL_ACTIVE",
            ExclusionReason: null);
        ProjectInstructionSelection selection =
            ProjectInstructionSelectionBuilder.Build(
                new ProjectInstructionManifest(
                    [agentRules, selectedSkill],
                    []),
                selectedSkill.RelativePath);

        string prompt = AgentPlanPromptBuilder.Build(
            "Improve the greeting.",
            "Sample",
            "Git repository",
            [new RepositoryContextFile("Sample.cs", "source", 6)],
            maximumContextTokens: 1_000,
            instructionSelection: selection);

        Assert.Contains(
            "Required instruction evidence path: AGENTS.md",
            prompt);
        Assert.Contains(
            "Required instruction evidence path: " +
            "skills/review/SKILL.md",
            prompt);
        Assert.Contains(
            "Required source evidence path: Sample.cs",
            prompt);
        Assert.Contains(
            "do not cite any repository path that is not listed",
            prompt);
        Assert.DoesNotContain("README.md", prompt);
    }

    [Fact]
    public void Build_RequiresExactSelectedMemoryEvidenceIdentity()
    {
        ProjectMemoryPromptEvidence memory = new(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ProjectMemoryCategory.Decision,
            "Greeting convention",
            "Use HELLO_LOCAL_AI.",
            48,
            12,
            DateTimeOffset.Parse("2026-08-08T12:00:00+00:00"));

        string prompt = AgentPlanPromptBuilder.Build(
            "Plan the greeting change.",
            "Sample",
            "Git repository",
            [new RepositoryContextFile("Sample.cs", "source", 6)],
            maximumContextTokens: 1_000,
            memoryEvidence: memory);

        Assert.Contains(
            $"Required project-memory evidence identity: " +
            memory.EvidenceIdentity,
            prompt);
        Assert.Contains("Use HELLO_LOCAL_AI.", prompt);
    }

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
        Assert.Contains(
            "Answer this user request: Plan the requested feature.",
            prompt);
        Assert.EndsWith(
            "commands, outcomes, files, or changes.",
            prompt);
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

    [Fact]
    public void Build_IncludesRetainedVerificationEvidence()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        VerificationRunResult verification = new(
            VerificationToolKind.DotnetTest,
            "dotnet test Sample.slnx -c Release --no-build --no-restore",
            startedAt,
            startedAt.AddSeconds(2),
            ExitCode: 1,
            WasCancelled: false,
            Output: "Test summary: total: 2, failed: 1");

        string prompt = AgentPlanPromptBuilder.Build(
            "Explain the failed verification.",
            "Sample",
            "Git repository",
            [],
            maximumContextTokens: 1_000,
            verificationRuns: [verification]);

        Assert.Contains("--- VERIFICATION EVIDENCE ---", prompt);
        Assert.Contains("Outcome: failed; exit code: 1", prompt);
        Assert.Contains("Test summary: total: 2, failed: 1", prompt);
        Assert.Contains("No source files selected", prompt);
        Assert.Contains(
            "Required evidence citation: dotnet test Sample.slnx " +
            "-c Release --no-build --no-restore | failed | exit code 1",
            prompt);
        Assert.Contains(
            "Do not claim that verification commands are missing",
            prompt);
    }

    [Fact]
    public void Build_TruncatesLargeVerificationEvidence()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        VerificationRunResult verification = new(
            VerificationToolKind.DotnetBuild,
            "dotnet build Sample.slnx -c Release --no-restore",
            now,
            now.AddSeconds(3),
            ExitCode: 1,
            WasCancelled: false,
            Output: new string('x', 12_000));

        string prompt = AgentPlanPromptBuilder.Build(
            "Explain the build failure.",
            "Sample",
            "Git repository",
            [],
            maximumContextTokens: 1_000,
            verificationRuns: [verification]);

        Assert.Contains(
            "[Verification evidence truncated to Local-AI limit]",
            prompt);
        Assert.DoesNotContain(new string('x', 8_001), prompt);
    }

    [Fact]
    public void Build_RepeatsCompletedVerificationAfterRepositoryContext()
    {
        RepositoryContextFile file = new(
            "src/Program.cs",
            "public static class Program { }",
            31);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        VerificationRunResult verification = new(
            VerificationToolKind.GitStatus,
            "git status --short --branch",
            now,
            now.AddSeconds(1),
            ExitCode: 0,
            WasCancelled: false,
            Output: "## feature/example");

        string prompt = AgentPlanPromptBuilder.Build(
            "Assess the repository state.",
            "Sample",
            "Git repository",
            [file],
            maximumContextTokens: 1_000,
            verificationRuns: [verification]);

        int fileEnd = prompt.IndexOf(
            "--- END FILE: src/Program.cs ---",
            StringComparison.Ordinal);
        int requiredCitation = prompt.IndexOf(
            "Required evidence citation: git status --short --branch " +
            "| passed | exit code 0",
            StringComparison.Ordinal);

        Assert.True(fileEnd >= 0);
        Assert.True(requiredCitation > fileEnd);
        Assert.Contains(
            "Treat them as completed evidence, not as future suggestions",
            prompt);
    }
}
