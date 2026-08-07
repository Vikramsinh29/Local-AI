using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IVerificationToolRunner
{
    Task<VerificationRunResult> RunAsync(
        VerificationToolKind tool,
        string repositoryRoot,
        string? solutionRelativePath,
        IProgress<VerificationOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}
