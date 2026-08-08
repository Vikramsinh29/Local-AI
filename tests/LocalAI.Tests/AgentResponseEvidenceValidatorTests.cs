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
