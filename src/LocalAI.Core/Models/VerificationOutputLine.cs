namespace LocalAI.Core.Models;

public sealed record VerificationOutputLine(
    string Text,
    bool IsError);
