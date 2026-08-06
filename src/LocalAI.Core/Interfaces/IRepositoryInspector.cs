using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IRepositoryInspector
{
    Task<RepositoryInfo> InspectAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}
