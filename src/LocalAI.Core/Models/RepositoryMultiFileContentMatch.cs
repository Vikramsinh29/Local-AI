namespace LocalAI.Core.Models;

public sealed record RepositoryMultiFileContentMatch(
    string RelativePath,
    int LineNumber,
    string Preview);
