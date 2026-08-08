using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static class ProjectInstructionSelectionBuilder
{
    public const long MaximumInstructionBytes = 8 * 1024;

    public const int MaximumInstructionTokens = 2_000;

    public static ProjectInstructionSelection Build(
        ProjectInstructionManifest manifest,
        string? selectedSkillRelativePath = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        ProjectInstructionFile[] files = manifest.Files
            .OrderBy(file => file.Kind)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        HashSet<string> duplicatePaths = files
            .GroupBy(
                file => file.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<ProjectInstructionSelectionItem> items = [];
        long includedBytes = 0;
        int includedTokens = 0;

        foreach (ProjectInstructionFile file in files)
        {
            if (duplicatePaths.Contains(file.RelativePath))
            {
                items.Add(
                    new ProjectInstructionSelectionItem(
                        file,
                        IsIncluded: false,
                        "Duplicate instruction paths are not included."));
                continue;
            }

            bool wantsInclusion = file.Kind == ProjectInstructionKind.AgentRules ||
                (!string.IsNullOrWhiteSpace(selectedSkillRelativePath) &&
                 file.RelativePath.Equals(
                     selectedSkillRelativePath,
                     StringComparison.OrdinalIgnoreCase));

            if (!file.IsEligible)
            {
                items.Add(
                    new ProjectInstructionSelectionItem(
                        file,
                        IsIncluded: false,
                        file.ExclusionReason ?? "Instruction is unavailable."));
                continue;
            }

            if (!wantsInclusion)
            {
                items.Add(
                    new ProjectInstructionSelectionItem(
                        file,
                        IsIncluded: false,
                        "Not selected."));
                continue;
            }

            if (includedBytes + file.SizeBytes > MaximumInstructionBytes ||
                includedTokens + file.EstimatedTokens >
                    MaximumInstructionTokens)
            {
                items.Add(
                    new ProjectInstructionSelectionItem(
                        file,
                        IsIncluded: false,
                        "Excluded because the combined 8 KB / ~2,000-token " +
                        "instruction budget would be exceeded."));
                continue;
            }

            includedBytes += file.SizeBytes;
            includedTokens += file.EstimatedTokens;
            items.Add(
                new ProjectInstructionSelectionItem(
                    file,
                    IsIncluded: true,
                    file.Kind == ProjectInstructionKind.AgentRules
                        ? "Included by default before any selected skill."
                        : "Included by explicit one-skill selection."));
        }

        return new ProjectInstructionSelection(
            items,
            includedBytes,
            includedTokens);
    }
}
