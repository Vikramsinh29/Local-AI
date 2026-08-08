namespace LocalAI.Core.Models;

public sealed record ProjectInstructionSelectionItem(
    ProjectInstructionFile File,
    bool IsIncluded,
    string StateReason);
