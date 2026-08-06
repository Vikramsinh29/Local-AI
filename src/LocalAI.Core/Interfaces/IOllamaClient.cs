using LocalAI.Core.Models;

namespace LocalAI.Core.Interfaces;

public interface IOllamaClient
{
    Task<bool> IsAvailableAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetModelsAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamGenerateAsync(
        string model,
        string prompt,
        GenerationProfile profile,
        CancellationToken cancellationToken = default);
}
