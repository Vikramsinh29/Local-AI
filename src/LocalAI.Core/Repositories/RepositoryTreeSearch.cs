using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static class RepositoryTreeSearch
{
    public const int MinimumQueryLength = 2;
    public const int MaximumQueryLength = 100;
    public const int MaximumResults = 50;

    public static RepositorySearchResponse Search(
        IEnumerable<RepositoryTreeNode> rootEntries,
        string query)
    {
        ArgumentNullException.ThrowIfNull(rootEntries);

        string normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length < MinimumQueryLength)
        {
            return new([], false, "Enter at least 2 characters.");
        }

        if (normalizedQuery.Length > MaximumQueryLength)
        {
            return new([], false, "Search cannot exceed 100 characters.");
        }

        RepositorySearchResult[] matches = Flatten(rootEntries)
            .Where(node => !node.IsDirectory)
            .Select(node => new
            {
                Node = node,
                Rank = MatchRank(node, normalizedQuery)
            })
            .Where(item => item.Rank is not null)
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.Node.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            .Take(MaximumResults + 1)
            .Select(item => new RepositorySearchResult(
                item.Node.Name,
                item.Node.RelativePath,
                item.Node.SizeBytes,
                item.Node.LastModifiedUtc))
            .ToArray();

        return new(
            matches.Take(MaximumResults).ToArray(),
            matches.Length > MaximumResults,
            null);
    }

    private static int? MatchRank(
        RepositoryTreeNode node,
        string query)
    {
        if (node.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (node.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (node.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return node.RelativePath.Contains(
            query,
            StringComparison.OrdinalIgnoreCase)
                ? 3
                : null;
    }

    private static IEnumerable<RepositoryTreeNode> Flatten(
        IEnumerable<RepositoryTreeNode> nodes)
    {
        foreach (RepositoryTreeNode node in nodes)
        {
            yield return node;

            foreach (RepositoryTreeNode child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}
