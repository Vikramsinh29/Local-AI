namespace LocalAI.Core.Models;

public sealed record ProjectInstructionSelection(
    IReadOnlyList<ProjectInstructionSelectionItem> Items,
    long IncludedBytes,
    int IncludedTokens)
{
    public IReadOnlyList<ProjectInstructionFile> IncludedFiles =>
        Items
            .Where(item => item.IsIncluded)
            .Select(item => item.File)
            .ToArray();
}
