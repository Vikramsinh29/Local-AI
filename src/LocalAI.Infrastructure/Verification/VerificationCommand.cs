namespace LocalAI.Infrastructure.Verification;

public sealed record VerificationCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string DisplayText);
