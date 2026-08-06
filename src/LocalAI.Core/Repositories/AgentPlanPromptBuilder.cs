using System.Text;
using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static class AgentPlanPromptBuilder
{
    public static string Build(
        string userRequest,
        string repositoryName,
        string repositorySummary,
        IEnumerable<RepositoryContextFile> contextFiles,
        int maximumContextTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySummary);
        ArgumentNullException.ThrowIfNull(contextFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumContextTokens);

        RepositoryContextFile[] files = contextFiles.ToArray();
        string repositoryPrompt = RepositoryContextPromptBuilder.Build(
            userRequest,
            files,
            maximumContextTokens);

        StringBuilder builder = new();
        builder.AppendLine(
            "You are Local-AI in controlled read-only agent mode.");
        builder.AppendLine(
            "Do not edit files, execute commands, commit, push, or " +
            "claim that any change was applied.");
        builder.AppendLine(
            "Use only the repository evidence included below. If " +
            "evidence is missing, state that clearly instead of guessing.");
        builder.AppendLine();
        builder.AppendLine($"Repository: {repositoryName}");
        builder.AppendLine($"Repository summary: {repositorySummary}");
        builder.AppendLine();
        builder.AppendLine("Return a concise plan with exactly these headings:");
        builder.AppendLine("1. Understanding");
        builder.AppendLine("2. Evidence used");
        builder.AppendLine("3. Assumptions and unknowns");
        builder.AppendLine("4. Read-only implementation plan");
        builder.AppendLine("5. Candidate affected files");
        builder.AppendLine("6. Verification to run later");
        builder.AppendLine("7. Safety boundary");
        builder.AppendLine();
        builder.Append(repositoryPrompt);

        return builder.ToString();
    }
}
