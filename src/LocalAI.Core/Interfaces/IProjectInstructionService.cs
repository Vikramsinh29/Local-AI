using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IProjectInstructionService
{
    Task<ProjectInstructionManifest> DiscoverAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);
}
