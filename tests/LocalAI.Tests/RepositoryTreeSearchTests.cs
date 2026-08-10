using LocalAI.Core.Models;
using LocalAI.Core.Repositories;

namespace LocalAI.Tests;

public sealed class RepositoryTreeSearchTests
{
    [Fact]
    public void Search_RanksFileNamesBeforePathMatches()
    {
        RepositoryTreeNode[] tree =
        [
            Directory("src", [
                File("SearchService.cs", "src/SearchService.cs"),
                File("Other.cs", "src/search/Other.cs"),
                File("MySearch.cs", "src/MySearch.cs")])
        ];

        RepositorySearchResponse response =
            RepositoryTreeSearch.Search(tree, "search");

        Assert.True(response.IsSuccess);
        Assert.Equal(
            ["src/SearchService.cs", "src/MySearch.cs", "src/search/Other.cs"],
            response.Results.Select(result => result.RelativePath));
    }

    [Fact]
    public void Search_ReturnsFilesOnlyAndTrimsQuery()
    {
        RepositoryTreeNode[] tree =
        [
            Directory("Search", [File("Search.cs", "Search/Search.cs")])
        ];

        RepositorySearchResponse response =
            RepositoryTreeSearch.Search(tree, "  search  ");

        Assert.Single(response.Results);
        Assert.Equal("Search/Search.cs", response.Results[0].RelativePath);
    }

    [Fact]
    public void Search_RejectsInvalidQueryLengths()
    {
        Assert.False(RepositoryTreeSearch.Search([], "x").IsSuccess);
        Assert.False(
            RepositoryTreeSearch.Search([], new string('x', 101)).IsSuccess);
    }

    [Fact]
    public void Search_IsDeterministicAndBounded()
    {
        RepositoryTreeNode[] tree = Enumerable.Range(0, 60)
            .Select(index => File($"Match{index:00}.cs", $"src/Match{index:00}.cs"))
            .Reverse()
            .ToArray();

        RepositorySearchResponse response =
            RepositoryTreeSearch.Search(tree, "match");

        Assert.Equal(RepositoryTreeSearch.MaximumResults, response.Results.Count);
        Assert.True(response.IsTruncated);
        Assert.Equal("src/Match00.cs", response.Results[0].RelativePath);
        Assert.Equal("src/Match49.cs", response.Results[^1].RelativePath);
    }

    private static RepositoryTreeNode File(string name, string path) =>
        new(name, path, false, 100, DateTime.UnixEpoch, []);

    private static RepositoryTreeNode Directory(
        string name,
        IReadOnlyList<RepositoryTreeNode> children) =>
        new(name, name, true, null, DateTime.UnixEpoch, children);
}
