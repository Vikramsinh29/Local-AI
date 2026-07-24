using System.Text;
using LocalForge.Core.Models;

namespace LocalForge.Core.Repositories;

public static class RepositoryContextPromptBuilder
{
    public const int SlowContextThresholdTokens = 4_000;

    public const int MaximumContextTokens = 16_000;

    public static string Build(
        string userPrompt,
        IEnumerable<RepositoryContextFile> contextFiles,
        int maximumContextTokens = MaximumContextTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentNullException.ThrowIfNull(contextFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumContextTokens);

        RepositoryContextFile[] files = contextFiles.ToArray();
        int estimatedTokens =
            files.Sum(file => file.EstimatedTokens);

        if (estimatedTokens > maximumContextTokens)
        {
            throw new InvalidOperationException(
                $"Selected repository context is approximately " +
                $"{estimatedTokens:N0} tokens. Remove files until it is " +
                $"{maximumContextTokens:N0} tokens or less for the " +
                "selected generation preset.");
        }

        if (files.Length == 0)
        {
            return userPrompt;
        }

        StringBuilder builder = new();
        builder.AppendLine(
            "Use the following read-only repository files as context.");
        builder.AppendLine(
            "Do not assume you can edit or execute anything in the repository.");
        builder.AppendLine();

        foreach (RepositoryContextFile file in files)
        {
            builder.AppendLine($"--- FILE: {file.RelativePath} ---");
            builder.AppendLine(file.Content);
            builder.AppendLine($"--- END FILE: {file.RelativePath} ---");
            builder.AppendLine();
        }

        builder.AppendLine("--- USER REQUEST ---");
        builder.Append(userPrompt);

        return builder.ToString();
    }

    public static bool IsLikelyToSlowGeneration(
        IEnumerable<RepositoryContextFile> contextFiles)
    {
        ArgumentNullException.ThrowIfNull(contextFiles);

        return contextFiles.Sum(file => file.EstimatedTokens) >=
               SlowContextThresholdTokens;
    }
}
