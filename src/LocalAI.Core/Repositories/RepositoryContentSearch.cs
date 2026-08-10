using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static class RepositoryContentSearch
{
    public const int MinimumQueryLength = 2;
    public const int MaximumQueryLength = 100;
    public const int MaximumMatches = 20;
    public const int MaximumPreviewCharacters = 240;

    public static RepositoryContentSearchResponse Search(
        RepositoryContextFile file,
        string query)
    {
        ArgumentNullException.ThrowIfNull(file);
        string value = query?.Trim() ?? string.Empty;
        if (value.Length < MinimumQueryLength)
        {
            return new([], false, "Enter at least 2 characters.");
        }
        if (value.Length > MaximumQueryLength)
        {
            return new([], false, "Search cannot exceed 100 characters.");
        }

        string[] lines = file.Content.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n');
        List<RepositoryContentMatch> matches = [];
        bool truncated = false;
        for (int index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (matches.Count == MaximumMatches)
            {
                truncated = true;
                break;
            }
            string preview = lines[index].Trim();
            if (preview.Length > MaximumPreviewCharacters)
            {
                preview = preview[..MaximumPreviewCharacters] + "…";
            }
            matches.Add(new(index + 1, preview));
        }
        return new(matches, truncated, null);
    }
}
