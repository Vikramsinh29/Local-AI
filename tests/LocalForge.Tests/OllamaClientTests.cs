using System.Net;
using System.Text;
using LocalForge.Infrastructure.Ollama;

namespace LocalForge.Tests;

public sealed class OllamaClientTests
{
    [Fact]
    public async Task GetModelsAsync_ReturnsSortedDistinctNames()
    {
        const string json =
            """
            {
              "models": [
                { "name": "zeta:latest" },
                { "name": "alpha:latest" },
                { "name": "alpha:latest" }
              ]
            }
            """;

        using HttpClient httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            });

        using OllamaClient client = new(httpClient);

        IReadOnlyList<string> models =
            await client.GetModelsAsync();

        Assert.Equal(
            new[] { "alpha:latest", "zeta:latest" },
            models);
    }

    [Fact]
    public async Task StreamGenerateAsync_ReturnsAllResponseChunks()
    {
        const string responseBody =
            """
            {"response":"Hello","done":false}
            {"response":" world","done":false}
            {"response":"","done":true}
            """;

        using HttpClient httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "application/x-ndjson")
            });

        using OllamaClient client = new(httpClient);

        List<string> chunks = [];

        await foreach (string chunk in
            client.StreamGenerateAsync(
                "test-model",
                "test prompt"))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(
            new[] { "Hello", " world" },
            chunks);
    }

    private static HttpClient CreateHttpClient(
        HttpResponseMessage response)
    {
        StubHttpMessageHandler handler = new(
            _ => response);

        return new HttpClient(handler)
        {
            BaseAddress =
                new Uri("http://127.0.0.1:11434/")
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
