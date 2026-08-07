using System.Text;
using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

public static class AgentPlanPromptBuilder
{
    private const int MaximumVerificationEvidenceCharacters = 8_000;

    public static string Build(
        string userRequest,
        string repositoryName,
        string repositorySummary,
        IEnumerable<RepositoryContextFile> contextFiles,
        int maximumContextTokens,
        IEnumerable<VerificationRunResult>? verificationRuns = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySummary);
        ArgumentNullException.ThrowIfNull(contextFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumContextTokens);

        RepositoryContextFile[] files = contextFiles.ToArray();
        VerificationRunResult[] retainedVerificationRuns =
            (verificationRuns ?? [])
                .OrderBy(run => run.CompletedAt)
                .TakeLast(3)
                .ToArray();

        string repositoryPrompt = RepositoryContextPromptBuilder.Build(
            userRequest,
            files,
            maximumContextTokens);

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
        AppendVerificationEvidence(
            builder,
            retainedVerificationRuns);
        AppendFinalResponseRequirements(
            builder,
            userRequest,
            retainedVerificationRuns);

        return builder.ToString();
    }

    private static void AppendVerificationEvidence(
        StringBuilder builder,
        IEnumerable<VerificationRunResult> verificationRuns)
    {
        VerificationRunResult[] runs = verificationRuns.ToArray();

        builder.AppendLine("--- VERIFICATION EVIDENCE ---");

        if (runs.Length == 0)
        {
            builder.AppendLine(
                "No verification tools have been run in this session.");
            builder.AppendLine("--- END VERIFICATION EVIDENCE ---");
            builder.AppendLine();
            return;
        }

        int remaining = MaximumVerificationEvidenceCharacters;

        foreach (VerificationRunResult run in runs)
        {
            string header =
                $"Command: {run.DisplayCommand}{Environment.NewLine}" +
                $"Outcome: {GetOutcome(run)}; exit code: " +
                $"{run.ExitCode}{Environment.NewLine}";

            AppendWithinLimit(builder, header, ref remaining);

            if (remaining <= 0)
            {
                break;
            }

            string output = string.IsNullOrWhiteSpace(run.Output)
                ? "[No command output]"
                : run.Output;

            AppendWithinLimit(
                builder,
                $"Output:{Environment.NewLine}{output}" +
                Environment.NewLine,
                ref remaining);

            if (remaining <= 0)
            {
                break;
            }
        }

        if (remaining <= 0)
        {
            builder.AppendLine(
                "[Verification evidence truncated to Local-AI limit]");
        }

        builder.AppendLine("--- END VERIFICATION EVIDENCE ---");
        builder.AppendLine();
    }

    private static void AppendFinalResponseRequirements(
        StringBuilder builder,
        string userRequest,
        IReadOnlyList<VerificationRunResult> verificationRuns)
    {
        builder.AppendLine("--- FINAL RESPONSE REQUIREMENTS ---");
        builder.AppendLine($"Answer this user request: {userRequest}");

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
                    $"{GetOutcome(run)} | exit code {run.ExitCode}");
            }

            builder.AppendLine(
                "In section 6, distinguish completed checks from checks " +
                "still needed. Never move a completed check into future work.");
        }

        builder.Append(
            "Return only the seven required sections and do not invent " +
            "commands, outcomes, files, or changes.");
    }

    private static string GetOutcome(VerificationRunResult run)
    {
        if (run.WasCancelled)
        {
            return "cancelled";
        }

        return run.IsSuccess ? "passed" : "failed";
    }

    private static void AppendWithinLimit(
        StringBuilder builder,
        string value,
        ref int remaining)
    {
        if (remaining <= 0)
        {
            return;
        }

        int length = Math.Min(value.Length, remaining);
        builder.Append(value.AsSpan(0, length));
        remaining -= length;
    }
}
