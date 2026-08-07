namespace LocalAI.Core.Models;

public sealed record VerificationRunResult(
    VerificationToolKind Tool,
    string DisplayCommand,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int ExitCode,
    bool WasCancelled,
    string Output)
{
    public bool IsSuccess => !WasCancelled && ExitCode == 0;
}
