using LocalAI.Core.Models;

namespace LocalAI.Desktop.ViewModels;

public sealed class VerificationAuditEntryViewModel
{
    public VerificationAuditEntryViewModel(
        VerificationToolDescriptor tool,
        VerificationRunResult result)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(result);

        Result = result;
        ToolName = tool.Name;
        Outcome = result.WasCancelled
            ? "Cancelled"
            : result.IsSuccess
                ? "Passed"
                : $"Failed ({result.ExitCode})";

        Summary =
            $"{result.CompletedAt:HH:mm:ss} • {ToolName} • {Outcome}";
    }

    public VerificationRunResult Result { get; }

    public string ToolName { get; }

    public string Outcome { get; }

    public string Summary { get; }

    public string Command => Result.DisplayCommand;

    public string Output => Result.Output;
}
