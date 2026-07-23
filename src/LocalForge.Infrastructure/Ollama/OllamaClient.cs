using System.Net.Http.Json;
using LocalForge.Core.Interfaces;

namespace LocalForge.Infrastructure.Ollama;

public sealed class OllamaClient : IOllamaClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public OllamaClient(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;

        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/"),
            Timeout = TimeSpan.FromMinutes(30)
        };

        _httpClient.BaseAddress ??=
            new Uri("http://127.0.0.1:11434/");
    }

    public async Task<bool> IsAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(
                    "api/version",
                    cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        TagsResponse? response =
            await _httpClient.GetFromJsonAsync<TagsResponse>(
                "api/tags",
                cancellationToken);

        return response?.Models?
                   .Where(model =>
                       !string.IsNullOrWhiteSpace(model.Name))
                   .Select(model => model.Name)
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                   .ToArray()
               ?? [];
    }

    public async Task<string> GenerateAsync(
        string model,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        GenerateRequest request = new(
            Model: model,
            Prompt: prompt,
            Stream: false);

        using HttpResponseMessage response =
            await _httpClient.PostAsJsonAsync(
                "api/generate",
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        GenerateResponse? result =
            await response.Content.ReadFromJsonAsync<GenerateResponse>(
                cancellationToken);

        if (result is null)
        {
            throw new InvalidOperationException(
                "Ollama returned an empty response.");
        }

        return result.Response ?? string.Empty;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed record TagsResponse(
        IReadOnlyList<ModelResponse>? Models);

    private sealed record ModelResponse(string Name);

    private sealed record GenerateRequest(
        string Model,
        string Prompt,
        bool Stream);

    private sealed record GenerateResponse(string? Response);
}
