namespace LocalAI.Core.Models;

public sealed record ProposedPatchFile(
    string RelativePath,
    int AddedLineCount,
    int RemovedLineCount);
