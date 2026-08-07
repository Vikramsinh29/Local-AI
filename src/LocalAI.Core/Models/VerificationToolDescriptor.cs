namespace LocalAI.Core.Models;

public sealed record VerificationToolDescriptor(
    VerificationToolKind Kind,
    string Name,
    string Description,
    string CommandPreview,
    bool RequiresGitRepository,
    bool RequiresSolution);

public static class VerificationTools
{
    public static IReadOnlyList<VerificationToolDescriptor> All { get; } =
    [
        new(
            VerificationToolKind.GitStatus,
            "Git status",
            "Shows the current branch and working-tree state.",
            "git -c core.fsmonitor=false status --short --branch " +
            "--untracked-files=all",
            RequiresGitRepository: true,
            RequiresSolution: false),
        new(
            VerificationToolKind.GitDiffCheck,
            "Git diff check",
            "Checks the working diff for whitespace errors.",
            "git -c core.fsmonitor=false diff --check --no-ext-diff " +
            "--no-textconv",
            RequiresGitRepository: true,
            RequiresSolution: false),
        new(
            VerificationToolKind.DotnetBuild,
            "Release build",
            "Builds the single detected solution into isolated verification " +
            "artifacts without restoring packages.",
            "dotnet build {solution} -c Release --no-restore --nologo " +
            "--artifacts-path {artifacts} " +
            "-p:BaseIntermediateOutputPath=obj\\",
            RequiresGitRepository: false,
            RequiresSolution: true),
        new(
            VerificationToolKind.DotnetTest,
            "Release tests",
            "Tests the isolated Release build without restoring packages.",
            "dotnet test {solution} -c Release --no-build --no-restore " +
            "--nologo --artifacts-path {artifacts} " +
            "-p:BaseIntermediateOutputPath=obj\\",
            RequiresGitRepository: false,
            RequiresSolution: true)
    ];

    public static VerificationToolDescriptor Get(
        VerificationToolKind kind)
    {
        return All.FirstOrDefault(tool => tool.Kind == kind) ??
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown verification tool.");
    }
}
