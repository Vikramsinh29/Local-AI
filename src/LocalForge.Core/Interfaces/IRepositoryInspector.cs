using LocalForge.Core.Models;

namespace LocalForge.Core.Interfaces;

public interface IRepositoryInspector
{
    Task<RepositoryInfo> InspectAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}
