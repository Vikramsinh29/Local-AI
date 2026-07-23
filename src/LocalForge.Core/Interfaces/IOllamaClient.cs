namespace LocalForge.Core.Interfaces;

public interface IOllamaClient
{
    Task<bool> IsAvailableAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetModelsAsync(
        CancellationToken cancellationToken = default);

    Task<string> GenerateAsync(
        string model,
        string prompt,
        CancellationToken cancellationToken = default);
}
