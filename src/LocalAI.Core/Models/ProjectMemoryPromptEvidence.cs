namespace LocalAI.Core.Models;

public sealed record ProjectMemoryPromptEvidence(
    Guid Id,
    ProjectMemoryCategory Category,
    string Title,
    string Content,
    long SizeBytes,
    int EstimatedTokens,
    DateTimeOffset UpdatedAtUtc)
{
    public string EvidenceIdentity => $"project-memory:{Id:D}";

    public static ProjectMemoryPromptEvidence FromEntry(
        ProjectMemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ProjectMemoryPromptEvidence(
            entry.Id,
            entry.Category,
            entry.Title,
            entry.Content,
            entry.SizeBytes,
            entry.EstimatedTokens,
            entry.UpdatedAtUtc);
    }

    public bool Matches(ProjectMemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Id == entry.Id &&
               Category == entry.Category &&
               string.Equals(Title, entry.Title, StringComparison.Ordinal) &&
               string.Equals(Content, entry.Content, StringComparison.Ordinal) &&
               SizeBytes == entry.SizeBytes &&
               EstimatedTokens == entry.EstimatedTokens &&
               UpdatedAtUtc.Equals(entry.UpdatedAtUtc);
    }
}
