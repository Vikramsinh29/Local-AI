namespace LocalForge.Core.Models;

public sealed record RepositoryTreeNode(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long? SizeBytes,
    DateTime? LastModifiedUtc,
    IReadOnlyList<RepositoryTreeNode> Children);
