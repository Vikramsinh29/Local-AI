using LocalAI.Core.Models;

namespace LocalAI.Desktop.ViewModels;

public sealed class ProjectMemoryEntryViewModel
{
    public ProjectMemoryEntryViewModel(ProjectMemoryEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    public ProjectMemoryEntry Entry { get; }

    public Guid Id => Entry.Id;

    public ProjectMemoryCategory Category => Entry.Category;

    public string CategoryText => Entry.Category switch
    {
        ProjectMemoryCategory.KnownIssue => "Known issue",
        _ => Entry.Category.ToString()
    };

    public string Title => Entry.Title;

    public string Content => Entry.Content;

    public string SizeText =>
        $"{Entry.SizeBytes:N0} B • ~{Entry.EstimatedTokens:N0} tokens";

    public string PromptSelectionText =>
        $"{CategoryText} • {Title} • {SizeText}";

    public string UpdatedText =>
        $"Updated {Entry.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}
