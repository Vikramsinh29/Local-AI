using System.Text;
using LocalForge.Core.Models;

namespace LocalForge.Core.Repositories;

public static class RepositoryContextPromptBuilder
{
    public static string Build(
        string userPrompt,
        IEnumerable<RepositoryContextFile> contextFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentNullException.ThrowIfNull(contextFiles);

        RepositoryContextFile[] files = contextFiles.ToArray();

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
}
