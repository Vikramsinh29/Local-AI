using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LocalForge.Core.Interfaces;

namespace LocalForge.Infrastructure.Ollama;

public sealed class OllamaClient : IOllamaClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public OllamaClient(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;

        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/"),
            Timeout = Timeout.InfiniteTimeSpan
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
                   .OrderBy(
                       name => name,
                       StringComparer.OrdinalIgnoreCase)
                   .ToArray()
               ?? [];
    }

    public async IAsyncEnumerable<string> StreamGenerateAsync(
        string model,
        string prompt,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        GenerateRequest body = new(
            Model: model,
            Prompt: prompt,
            Stream: true);

        using HttpRequestMessage request =
            new(HttpMethod.Post, "api/generate")
            {
                Content = JsonContent.Create(body)
            };

        using HttpResponseMessage response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        await using Stream responseStream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using StreamReader reader = new(responseStream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line =
                await reader.ReadLineAsync(cancellationToken);

            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            GenerateStreamResponse? chunk =
                JsonSerializer.Deserialize<GenerateStreamResponse>(
                    line,
                    JsonOptions);

            if (!string.IsNullOrEmpty(chunk?.Response))
            {
                yield return chunk.Response;
            }

            if (chunk?.Done == true)
            {
                break;
            }
        }
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

    private sealed record GenerateStreamResponse(
        string? Response,
        bool Done);
}
