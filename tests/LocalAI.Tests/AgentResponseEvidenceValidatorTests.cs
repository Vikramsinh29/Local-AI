using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Tests;

public sealed class AgentResponseEvidenceValidatorTests
{
    [Fact]
    public void Validate_AcceptsOnlyExactExpectedEvidencePaths()
    {
        ProjectInstructionSelection instructions =
            CreateInstructionSelection();
        RepositoryContextFile[] sourceFiles =
            [new("Sample.cs", "source", 6)];
        string response =
            "### Evidence Used\n" +
            "- AGENTS.md\n" +
            "- skills/review/SKILL.md\n" +
            "- Sample.cs";

        AgentResponseEvidenceValidationResult result =
            AgentResponseEvidenceValidator.Validate(
                response,
                sourceFiles,
                instructions);

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingRequiredPaths);
        Assert.Empty(result.UnexpectedPaths);
    }

    [Fact]
    public void Validate_RejectsMissingAndUnlistedEvidencePaths()
    {
        ProjectInstructionSelection instructions =
            CreateInstructionSelection();
        RepositoryContextFile[] sourceFiles =
            [new("Sample.cs", "source", 6)];
        string response =
            "### Evidence Used\n" +
            "- AGENTS.md\n" +
            "- SKILL.md\n" +
            "- README.md";

        AgentResponseEvidenceValidationResult result =
            AgentResponseEvidenceValidator.Validate(
                response,
                sourceFiles,
                instructions);

        Assert.False(result.IsValid);
        Assert.Equal(
            new[] { "skills/review/SKILL.md", "Sample.cs" },
            result.MissingRequiredPaths);
        Assert.Equal(
            new[] { "SKILL.md", "README.md" },
            result.UnexpectedPaths);
    }

    [Fact]
    public void Validate_RequiresExactSelectedMemoryIdentity()
    {
        ProjectMemoryPromptEvidence memory = new(
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            ProjectMemoryCategory.Decision,
            "Decision",
            "Use the local greeting.",
            40,
            10,
            DateTimeOffset.Parse("2026-08-08T12:00:00+00:00"));

        AgentResponseEvidenceValidationResult missing =
            AgentResponseEvidenceValidator.Validate(
                "### Evidence Used\n- Sample.cs",
                [new RepositoryContextFile("Sample.cs", "source", 6)],
                memoryEvidence: memory);
        AgentResponseEvidenceValidationResult included =
            AgentResponseEvidenceValidator.Validate(
                $"### Evidence Used\n- Sample.cs\n- {memory.EvidenceIdentity}",
                [new RepositoryContextFile("Sample.cs", "source", 6)],
                memoryEvidence: memory);

        Assert.False(missing.IsValid);
        Assert.Equal(
            new[] { memory.EvidenceIdentity },
            missing.MissingRequiredPaths);
        Assert.True(included.IsValid);
    }

    private static ProjectInstructionSelection
        CreateInstructionSelection()
    {
        ProjectInstructionFile agentRules = new(
            ProjectInstructionKind.AgentRules,
            "AGENTS.md",
            10,
            3,
            "rules",
            ExclusionReason: null);
        ProjectInstructionFile skill = new(
            ProjectInstructionKind.Skill,
            "skills/review/SKILL.md",
            10,
            3,
            "skill",
            ExclusionReason: null);

        return ProjectInstructionSelectionBuilder.Build(
            new ProjectInstructionManifest(
                [agentRules, skill],
                []),
            skill.RelativePath);
    }
}
