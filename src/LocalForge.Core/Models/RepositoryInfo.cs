namespace LocalForge.Core.Models;

public sealed record RepositoryInfo(
    string RootPath,
    bool IsGitRepository,
    IReadOnlyList<string> SolutionFiles,
    IReadOnlyList<string> ProjectFiles);
