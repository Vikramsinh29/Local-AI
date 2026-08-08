namespace LocalAI.Core.Models;

public sealed record ProjectInstructionManifest(
    IReadOnlyList<ProjectInstructionFile> Files,
    IReadOnlyList<string> DiscoveryIssues)
{
    public static ProjectInstructionManifest Empty { get; } =
        new([], []);
}
