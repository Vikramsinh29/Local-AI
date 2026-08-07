using System.Text;
using LocalAI.Core.Models;

namespace LocalAI.Core.Repositories;

internal static class AgentVerificationEvidencePromptBuilder
{
    private const int MaximumEvidenceCharacters = 8_000;

    public static VerificationRunResult[] RetainRecent(
        IEnumerable<VerificationRunResult>? verificationRuns)
    {
        return (verificationRuns ?? [])
            .OrderBy(run => run.CompletedAt)
            .TakeLast(3)
            .ToArray();
    }

    public static void AppendEvidence(
        StringBuilder builder,
        IReadOnlyList<VerificationRunResult> runs)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(runs);

        builder.AppendLine("--- VERIFICATION EVIDENCE ---");

        if (runs.Count == 0)
        {
            builder.AppendLine(
                "No verification tools have been run in this session.");
            builder.AppendLine("--- END VERIFICATION EVIDENCE ---");
            builder.AppendLine();
            return;
        }

        int remaining = MaximumEvidenceCharacters;

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

    public static string GetOutcome(VerificationRunResult run)
    {
        return run.WasCancelled
            ? "cancelled"
            : run.IsSuccess
                ? "passed"
                : "failed";
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
