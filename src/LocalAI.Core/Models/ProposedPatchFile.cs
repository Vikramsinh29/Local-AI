namespace LocalAI.Core.Models;

public sealed record ProposedPatchFile(
    string RelativePath,
    string OriginalText,
    string ReplacementText,
    string SourceSha256,
    int AddedLineCount,
    int RemovedLineCount);
