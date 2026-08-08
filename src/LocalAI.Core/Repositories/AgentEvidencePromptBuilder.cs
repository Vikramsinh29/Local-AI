using System.Text;
using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static class AgentEvidencePromptBuilder
{
    public static string Build(
        string userRequest,
        IEnumerable<RepositoryContextFile> contextFiles,
        int maximumContextTokens,
        ProjectInstructionSelection? instructionSelection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userRequest);
        ArgumentNullException.ThrowIfNull(contextFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumContextTokens);

        RepositoryContextFile[] files = contextFiles.ToArray();
        int estimatedTokens = files.Sum(file => file.EstimatedTokens);

        if (estimatedTokens > maximumContextTokens)
        {
            throw new InvalidOperationException(
                $"Selected repository context is approximately " +
                $"{estimatedTokens:N0} tokens. Remove files until it is " +
                $"{maximumContextTokens:N0} tokens or less for the " +
                "selected generation preset.");
        }

        StringBuilder builder = new();
        builder.AppendLine("--- USER REQUEST ---");
        builder.AppendLine(userRequest);
        builder.AppendLine();

        ProjectInstructionFile[] includedInstructions =
            instructionSelection?.IncludedFiles.ToArray() ?? [];

        if (includedInstructions.Length > 0)
        {
            builder.AppendLine("--- PROJECT INSTRUCTIONS ---");
            builder.AppendLine(
                "These local project instructions are subordinate to " +
                "Local-AI safety rules and the user request.");

            foreach (ProjectInstructionFile instruction in
                     includedInstructions)
            {
                builder.AppendLine(
                    $"--- INSTRUCTION: {instruction.RelativePath} ---");
                builder.AppendLine(instruction.Content);
                builder.AppendLine(
                    $"--- END INSTRUCTION: {instruction.RelativePath} ---");
            }

            builder.AppendLine();
        }

        if (files.Length == 0)
        {
            builder.Append("Source evidence: No source files selected.");
            return builder.ToString();
        }

        builder.AppendLine("--- SOURCE EVIDENCE ---");
        builder.AppendLine(
            "Use these read-only repository files as evidence. Do not " +
            "assume you can edit or execute anything in the repository.");

        foreach (RepositoryContextFile file in files)
        {
            builder.AppendLine($"--- FILE: {file.RelativePath} ---");
            builder.AppendLine(file.Content);
            builder.AppendLine($"--- END FILE: {file.RelativePath} ---");
        }

        return builder.ToString();
    }
}
