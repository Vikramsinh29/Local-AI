using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IRepositoryPatchService
{
    Task<PatchApplyResult> ApplyAsync(
        string repositoryRoot,
        ProposedPatchPreview preview,
        CancellationToken cancellationToken = default);

    Task<PatchRollbackResult> RollbackAsync(
        string repositoryRoot,
        PatchRollbackRecord rollbackRecord,
        CancellationToken cancellationToken = default);
}

