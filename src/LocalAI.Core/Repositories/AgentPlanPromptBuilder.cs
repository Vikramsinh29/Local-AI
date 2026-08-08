using System.Text;
using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static class AgentPlanPromptBuilder
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

        RepositoryContextFile[] files = contextFiles.ToArray();
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
            "You are Local-AI in controlled read-only agent mode.");
        builder.AppendLine(
            "Do not edit files, execute commands, commit, push, or " +
            "claim that any change was applied.");
        builder.AppendLine(
            "Use only the repository evidence included below. If " +
            "evidence is missing, state that clearly instead of guessing.");
        builder.AppendLine();
        builder.AppendLine($"Repository: {repositoryName}");
        builder.AppendLine($"Repository summary: {repositorySummary}");
        builder.AppendLine(
            files.Length == 0
                ? "Source evidence: No source files selected."
                : "Source evidence: " +
                  string.Join(
                      ", ",
                      files.Select(file => file.RelativePath)));
        builder.AppendLine();
        builder.AppendLine("Return a concise plan with exactly these headings:");
        builder.AppendLine("1. Understanding");
        builder.AppendLine("2. Evidence used");
        builder.AppendLine("3. Assumptions and unknowns");
        builder.AppendLine("4. Read-only implementation plan");
        builder.AppendLine("5. Candidate affected files");
        builder.AppendLine("6. Verification to run later");
        builder.AppendLine("7. Safety boundary");
        builder.AppendLine();
        builder.Append(repositoryPrompt);
        builder.AppendLine();
        builder.AppendLine();
        AgentVerificationEvidencePromptBuilder.AppendEvidence(
            builder,
            retainedVerificationRuns);
        AppendFinalResponseRequirements(
            builder,
            userRequest,
            retainedVerificationRuns,
            files,
            instructionSelection);

        return builder.ToString();
    }

    private static void AppendFinalResponseRequirements(
        StringBuilder builder,
        string userRequest,
        IReadOnlyList<VerificationRunResult> verificationRuns,
        IReadOnlyList<RepositoryContextFile> sourceFiles,
        ProjectInstructionSelection? instructionSelection)
    {
        builder.AppendLine("--- FINAL RESPONSE REQUIREMENTS ---");
        builder.AppendLine($"Answer this user request: {userRequest}");
        builder.AppendLine(
            "In section 2, cite every required evidence path below exactly " +
            "and do not cite any repository path that is not listed.");

        foreach (ProjectInstructionFile instruction in
                 instructionSelection?.IncludedFiles ??
                 Array.Empty<ProjectInstructionFile>())
        {
            builder.AppendLine(
                $"Required instruction evidence path: " +
                $"{instruction.RelativePath}");
        }

        foreach (RepositoryContextFile sourceFile in sourceFiles)
        {
            builder.AppendLine(
                $"Required source evidence path: " +
                $"{sourceFile.RelativePath}");
        }

        if (verificationRuns.Count == 0)
        {
            builder.AppendLine(
                "State that no retained verification evidence is available.");
        }
        else
        {
            builder.AppendLine(
                $"The evidence block contains {verificationRuns.Count} " +
                "completed verification run(s). Treat them as completed " +
                "evidence, not as future suggestions.");
            builder.AppendLine(
                "In section 2, include every required evidence citation " +
                "below exactly. Do not claim that verification commands " +
                "are missing.");

            foreach (VerificationRunResult run in verificationRuns)
            {
                builder.AppendLine(
                    $"Required evidence citation: {run.DisplayCommand} | " +
                    $"{AgentVerificationEvidencePromptBuilder.GetOutcome(run)} " +
                    $"| exit code {run.ExitCode}");
            }

            builder.AppendLine(
                "In section 6, distinguish completed checks from checks " +
                "still needed. Never move a completed check into future work.");
        }

        builder.Append(
            "Return only the seven required sections and do not invent " +
            "commands, outcomes, files, or changes.");
    }

}
