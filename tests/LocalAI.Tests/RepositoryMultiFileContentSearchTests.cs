using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Tests;

public sealed class RepositoryMultiFileContentSearchTests
{
    [Fact]
    public void Search_OrdersFilesAndReturnsPathsAndLines()
    {
        RepositoryContextFile[] files =
        [
            new("z.txt", "match", 5),
            new("a.txt", "none\nMATCH", 10)
        ];
        RepositoryMultiFileContentSearchResponse result =
            RepositoryMultiFileContentSearch.Search(files, "match");
        Assert.Equal(["a.txt", "z.txt"], result.Matches.Select(x => x.RelativePath));
        Assert.Equal([2, 1], result.Matches.Select(x => x.LineNumber));
    }

    [Fact]
    public void Search_RejectsInvalidFileAndQueryLimits()
    {
        RepositoryContextFile file = new("a.txt", "match", 5);
        Assert.False(RepositoryMultiFileContentSearch.Search([], "match").IsSuccess);
        Assert.False(RepositoryMultiFileContentSearch.Search(
            Enumerable.Repeat(file, 6).ToArray(), "match").IsSuccess);
        Assert.False(RepositoryMultiFileContentSearch.Search([file], "x").IsSuccess);
    }

    [Fact]
    public void Search_BoundsMatchesPerFileAndTotal()
    {
        string content = string.Join('\n', Enumerable.Repeat("match", 20));
        RepositoryContextFile[] files = Enumerable.Range(1, 5)
            .Select(index => new RepositoryContextFile($"{index}.txt", content, content.Length))
            .ToArray();
        RepositoryMultiFileContentSearchResponse result =
            RepositoryMultiFileContentSearch.Search(files, "match");
        Assert.Equal(50, result.Matches.Count);
        Assert.True(result.IsTruncated);
        Assert.All(
            result.Matches.GroupBy(match => match.RelativePath),
            group => Assert.Equal(10, group.Count()));
    }
}
