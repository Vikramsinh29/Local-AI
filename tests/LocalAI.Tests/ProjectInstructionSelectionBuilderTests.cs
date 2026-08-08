using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Tests;

public sealed class ProjectInstructionSelectionBuilderTests
{
    [Fact]
    public void Build_IncludesAgentRulesAndAtMostOneSelectedSkill()
    {
        ProjectInstructionManifest manifest = new(
            [
                Instruction(
                    ProjectInstructionKind.AgentRules,
                    "AGENTS.md",
                    "agent rules"),
                Instruction(
                    ProjectInstructionKind.Skill,
                    "skills/alpha/SKILL.md",
                    "alpha rules"),
                Instruction(
                    ProjectInstructionKind.Skill,
                    "skills/beta/SKILL.md",
                    "beta rules")
            ],
            []);

        ProjectInstructionSelection selection =
            ProjectInstructionSelectionBuilder.Build(
                manifest,
                "skills/beta/SKILL.md");

        Assert.Equal(
            ["AGENTS.md", "skills/beta/SKILL.md"],
            selection.IncludedFiles
                .Select(file => file.RelativePath)
                .ToArray());
        Assert.Contains(
            selection.Items,
            item => item.File.RelativePath ==
                    "skills/alpha/SKILL.md" &&
                !item.IsIncluded &&
                item.StateReason.Contains(
                    "Not selected",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_ExcludesWholeSelectedSkillWhenCombinedBudgetIsExceeded()
    {
        string agentContent = new('a', 7_000);
        string skillContent = new('b', 2_000);
        ProjectInstructionManifest manifest = new(
            [
                Instruction(
                    ProjectInstructionKind.AgentRules,
                    "AGENTS.md",
                    agentContent),
                Instruction(
                    ProjectInstructionKind.Skill,
                    "skills/large/SKILL.md",
                    skillContent)
            ],
            []);

        ProjectInstructionSelection selection =
            ProjectInstructionSelectionBuilder.Build(
                manifest,
                "skills/large/SKILL.md");

        Assert.Single(selection.IncludedFiles);
        Assert.Equal("AGENTS.md", selection.IncludedFiles[0].RelativePath);
        Assert.Contains(
            selection.Items,
            item => item.File.Kind == ProjectInstructionKind.Skill &&
                !item.IsIncluded &&
                item.StateReason.Contains(
                    "budget",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_RejectsDuplicatePathsWithoutIncludingEitherCopy()
    {
        ProjectInstructionFile first = Instruction(
            ProjectInstructionKind.Skill,
            "skills/review/SKILL.md",
            "first");
        ProjectInstructionFile second = first with
        {
            Content = "second"
        };
        ProjectInstructionManifest manifest = new(
            [first, second],
            []);

        ProjectInstructionSelection selection =
            ProjectInstructionSelectionBuilder.Build(
                manifest,
                "skills/review/SKILL.md");

        Assert.Empty(selection.IncludedFiles);
        Assert.All(
            selection.Items,
            item => Assert.Contains(
                "Duplicate",
                item.StateReason,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AgentEvidencePrompt_UsesPrecedenceAndOmitsExcludedContent()
    {
        ProjectInstructionManifest manifest = new(
            [
                Instruction(
                    ProjectInstructionKind.AgentRules,
                    "AGENTS.md",
                    "AGENT_SENTINEL"),
                Instruction(
                    ProjectInstructionKind.Skill,
                    "skills/chosen/SKILL.md",
                    "SKILL_SENTINEL"),
                Instruction(
                    ProjectInstructionKind.Skill,
                    "skills/ignored/SKILL.md",
                    "EXCLUDED_SENTINEL")
            ],
            []);
        ProjectInstructionSelection selection =
            ProjectInstructionSelectionBuilder.Build(
                manifest,
                "skills/chosen/SKILL.md");
        RepositoryContextFile source = new(
            "src/Program.cs",
            "SOURCE_SENTINEL",
            15);

        string prompt = AgentEvidencePromptBuilder.Build(
            "USER_SENTINEL",
            [source],
            maximumContextTokens: 1_000,
            instructionSelection: selection);

        int user = prompt.IndexOf("USER_SENTINEL", StringComparison.Ordinal);
        int agents = prompt.IndexOf("AGENT_SENTINEL", StringComparison.Ordinal);
        int skill = prompt.IndexOf("SKILL_SENTINEL", StringComparison.Ordinal);
        int sourceIndex = prompt.IndexOf(
            "SOURCE_SENTINEL",
            StringComparison.Ordinal);

        Assert.True(user >= 0);
        Assert.True(agents > user);
        Assert.True(skill > agents);
        Assert.True(sourceIndex > skill);
        Assert.DoesNotContain("EXCLUDED_SENTINEL", prompt);
        Assert.Contains("subordinate", prompt);
    }

    private static ProjectInstructionFile Instruction(
        ProjectInstructionKind kind,
        string path,
        string content)
    {
        return new ProjectInstructionFile(
            kind,
            path,
            content.Length,
            Math.Max(1, (content.Length + 3) / 4),
            content,
            ExclusionReason: null);
    }
}
