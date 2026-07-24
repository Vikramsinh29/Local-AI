namespace LocalForge.Core.Models;

public sealed record RepositoryContextFile(
    string RelativePath,
    string Content,
    long SizeBytes)
{
    public int EstimatedTokens =>
        Math.Max(1, (Content.Length + 3) / 4);
}
