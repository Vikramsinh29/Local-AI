using LocalForge.Core.Models;

namespace LocalForge.Core.Interfaces;

public interface IRepositoryFileContextService
{
    long MaximumFileBytes { get; }

    long MaximumTotalBytes { get; }

    Task<RepositoryContextReadResult> ReadAsync(
        string repositoryRoot,
        string relativePath,
        long currentTotalBytes,
        CancellationToken cancellationToken = default);
}
