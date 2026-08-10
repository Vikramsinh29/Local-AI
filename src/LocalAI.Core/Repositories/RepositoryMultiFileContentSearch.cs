using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static class RepositoryMultiFileContentSearch
{
    public const int MaximumFiles = 5;
    public const int MaximumMatchesPerFile = 10;
    public const int MaximumMatches = 50;

    public static RepositoryMultiFileContentSearchResponse Search(
        IReadOnlyList<RepositoryContextFile> files,
        string query)
    {
        ArgumentNullException.ThrowIfNull(files);
        string value = query?.Trim() ?? string.Empty;
        if (value.Length < RepositoryContentSearch.MinimumQueryLength)
        {
            return new([], false, "Enter at least 2 characters.");
        }
        if (value.Length > RepositoryContentSearch.MaximumQueryLength)
        {
            return new([], false, "Search cannot exceed 100 characters.");
        }
        if (files.Count == 0 || files.Count > MaximumFiles)
        {
            return new([], false, "Select between 1 and 5 context files.");
        }

        List<RepositoryMultiFileContentMatch> matches = [];
        bool truncated = false;
        foreach (RepositoryContextFile file in files
                     .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            RepositoryContentSearchResponse response =
                RepositoryContentSearch.Search(file, value);
            foreach (RepositoryContentMatch match in response.Matches
                         .Take(MaximumMatchesPerFile))
            {
                if (matches.Count == MaximumMatches)
                {
                    truncated = true;
                    break;
                }
                matches.Add(new(
                    file.RelativePath,
                    match.LineNumber,
                    match.Preview));
            }
            truncated |= response.Matches.Count > MaximumMatchesPerFile ||
                         response.IsTruncated;
            if (matches.Count == MaximumMatches)
            {
                truncated = true;
                break;
            }
        }
        return new(matches, truncated, null);
    }
}
