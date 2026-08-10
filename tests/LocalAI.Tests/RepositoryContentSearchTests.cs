using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Tests;

public sealed class RepositoryContentSearchTests
{
    [Fact]
    public void Search_ReturnsCaseInsensitiveLineMatches()
    {
        RepositoryContextFile file = new("README.md", "First\r\nSearch here\r\nSEARCH again", 31);
        RepositoryContentSearchResponse result = RepositoryContentSearch.Search(file, "search");
        Assert.Equal([2, 3], result.Matches.Select(match => match.LineNumber));
    }

    [Fact]
    public void Search_RejectsInvalidQueryLengths()
    {
        RepositoryContextFile file = new("a.txt", "content", 7);
        Assert.False(RepositoryContentSearch.Search(file, "x").IsSuccess);
        Assert.False(RepositoryContentSearch.Search(file, new string('x', 101)).IsSuccess);
    }

    [Fact]
    public void Search_BoundsMatchesAndPreviewLength()
    {
        string content = string.Join('\n', Enumerable.Repeat(new string('a', 300) + " match", 25));
        RepositoryContentSearchResponse result = RepositoryContentSearch.Search(new("a.txt", content, content.Length), "match");
        Assert.Equal(20, result.Matches.Count);
        Assert.True(result.IsTruncated);
        Assert.All(result.Matches, match => Assert.True(match.Preview.Length <= 241));
    }
}
