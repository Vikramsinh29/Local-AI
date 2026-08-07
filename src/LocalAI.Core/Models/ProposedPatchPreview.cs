namespace LocalAI.Core.Models;

public sealed record ProposedPatchPreview(
    string Summary,
    IReadOnlyList<ProposedPatchFile> Files,
    string UnifiedDiff)
{
    public int AddedLineCount =>
        Files.Sum(file => file.AddedLineCount);

    public int RemovedLineCount =>
        Files.Sum(file => file.RemovedLineCount);
}
