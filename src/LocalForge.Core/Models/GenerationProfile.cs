namespace LocalForge.Core.Models;

public sealed record GenerationProfile(
    string Name,
    string Description,
    int MaximumOutputTokens,
    int ContextWindowTokens,
    int MaximumRepositoryContextTokens,
    double Temperature);

public static class GenerationProfiles
{
    public static GenerationProfile Fast { get; } = new(
        "Fast",
        "Lower latency and shorter output.",
        MaximumOutputTokens: 256,
        ContextWindowTokens: 8_192,
        MaximumRepositoryContextTokens: 4_000,
        Temperature: 0.2);

    public static GenerationProfile Balanced { get; } = new(
        "Balanced",
        "Default context and response length.",
        MaximumOutputTokens: 512,
        ContextWindowTokens: 8_192,
        MaximumRepositoryContextTokens: 6_000,
        Temperature: 0.3);

    public static GenerationProfile Accurate { get; } = new(
        "Accurate",
        "More context and generation time.",
        MaximumOutputTokens: 1_024,
        ContextWindowTokens: 8_192,
        MaximumRepositoryContextTokens: 8_000,
        Temperature: 0.2);

    public static IReadOnlyList<GenerationProfile> All { get; } =
        [Fast, Balanced, Accurate];
}
