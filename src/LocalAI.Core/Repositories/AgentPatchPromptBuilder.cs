using System.Text;
using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static class AgentPatchPromptBuilder
{
    public static string Build(
        string userRequest,
        string repositoryName,
        string repositorySummary,
        IEnumerable<RepositoryContextFile> contextFiles,
        int maximumContextTokens,
        IEnumerable<VerificationRunResult>? verificationRuns = null,
        ProjectInstructionSelection? instructionSelection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySummary);
        ArgumentNullException.ThrowIfNull(contextFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumContextTokens);

        RepositoryContextFile[] files = contextFiles
            .Select(file => file with
            {
                RelativePath = NormalizePromptPath(file.RelativePath)
            })
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                "Select at least one source file before requesting a " +
                "patch preview.");
        }

        VerificationRunResult[] retainedVerificationRuns =
            AgentVerificationEvidencePromptBuilder.RetainRecent(
                verificationRuns);
        string repositoryPrompt = AgentEvidencePromptBuilder.Build(
            userRequest,
            files,
            maximumContextTokens,
            instructionSelection);

        StringBuilder builder = new();
        builder.AppendLine(
            "You are Local-AI in controlled patch-preview mode.");
        builder.AppendLine(
            "Propose a patch only. Do not edit files, execute commands, " +
            "commit, push, or claim that the patch was applied.");
        builder.AppendLine(
            "Use only the repository and verification evidence below. " +
            "Do not invent existing code, files, APIs, or outcomes.");
        builder.AppendLine(
            "Propose replacements only in the selected existing source " +
            "files. Do not create, delete, or rename files.");
        builder.AppendLine();
        builder.AppendLine($"Repository: {repositoryName}");
        builder.AppendLine($"Repository summary: {repositorySummary}");
        builder.AppendLine(
            "Source evidence: " +
            string.Join(", ", files.Select(file => file.RelativePath)));
        builder.AppendLine();
        builder.Append(repositoryPrompt);
        builder.AppendLine();
        builder.AppendLine();
        AgentVerificationEvidencePromptBuilder.AppendEvidence(
            builder,
            retainedVerificationRuns);
        AppendFormatRequirements(
            builder,
            userRequest,
            retainedVerificationRuns);

        return builder.ToString();
    }

    private static void AppendFormatRequirements(
        StringBuilder builder,
        string userRequest,
        IReadOnlyList<VerificationRunResult> verificationRuns)
    {
        builder.AppendLine("--- PATCH RESPONSE REQUIREMENTS ---");
        builder.AppendLine($"Requested change: {userRequest}");
        builder.AppendLine(
            "Return only one structured patch using exactly this format:");
        builder.AppendLine("Preview only — not applied.");
        builder.AppendLine(ProposedPatchParser.StartMarker);
        builder.AppendLine("SUMMARY:");
        builder.AppendLine("One concise summary grounded in the evidence.");
        builder.AppendLine("<<<FILE:relative/path/to/file>>>");
        builder.AppendLine(ProposedPatchParser.OriginalMarker);
        builder.AppendLine(ProposedPatchParser.ReplacementMarker);
        builder.AppendLine(ProposedPatchParser.EndFileMarker);
        builder.AppendLine(ProposedPatchParser.EndMarker);
        builder.AppendLine();
        builder.AppendLine(
            "Repeat the FILE block for each changed selected file. After " +
            "ORIGINAL, copy one exact non-empty source fragment from that " +
            "file. After REPLACEMENT, provide its non-empty replacement. " +
            "Local-AI will reject text that is absent or appears more than " +
            "once in the selected file.");
        builder.AppendLine(
            "Put the text immediately after each marker with no blank line. " +
            "Do not prefix either text with + or -.");
        builder.AppendLine(
            "Use forward slashes (/) in every proposed path. The FILE path " +
            "must identify one of the selected source-evidence files.");
        builder.AppendLine(
            "Do not use Markdown fences or add prose outside the markers.");

        if (verificationRuns.Count == 0)
        {
            builder.AppendLine(
                "No completed verification evidence is available; do not " +
                "claim that checks passed.");
        }
        else
        {
            foreach (VerificationRunResult run in verificationRuns)
            {
                builder.AppendLine(
                    $"Completed evidence: {run.DisplayCommand} | " +
                    $"{AgentVerificationEvidencePromptBuilder.GetOutcome(run)} " +
                    $"| exit code {run.ExitCode}");
            }
        }

        builder.Append(
            "The result is preview-only and must never state that source " +
            "changes were applied.");
    }

    private static string NormalizePromptPath(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }
}
