using System.Net;
using System.Text;
using System.Text.Json;
using LocalForge.Core.Models;
using LocalForge.Infrastructure.Ollama;

namespace LocalForge.Tests;

public sealed class OllamaClientTests
{
    [Fact]
    public async Task IsAvailableAsync_ReturnsFalseForConnectionFailure()
    {
        using HttpClient httpClient = CreateHttpClient(
            (_, _) => throw new HttpRequestException("Connection refused."));
        using OllamaClient client = new(httpClient);

        bool isAvailable = await client.IsAvailableAsync();

        Assert.False(isAvailable);
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalseForUnavailableService()
    {
        using HttpClient httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using OllamaClient client = new(httpClient);

        bool isAvailable = await client.IsAvailableAsync();

        Assert.False(isAvailable);
    }

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
            CreateStreamingResponse(responseBody));
        using OllamaClient client = new(httpClient);

        List<string> chunks = [];

        await foreach (string chunk in
            client.StreamGenerateAsync(
                "test-model",
                "test prompt",
                GenerationProfiles.Balanced))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(
            new[] { "Hello", " world" },
            chunks);
    }

    [Fact]
    public async Task StreamGenerateAsync_KeepsSelectedModelWarm()
    {
        string? requestJson = null;

        using HttpClient httpClient = CreateHttpClient(
            (request, _) =>
            {
                requestJson = request.Content!
                    .ReadAsStringAsync()
                    .GetAwaiter()
                    .GetResult();

                return CreateStreamingResponse(
                    """{"response":"","done":true}""");
            });
        using OllamaClient client = new(httpClient);

        await ReadAllChunksAsync(client);

        using JsonDocument request =
            JsonDocument.Parse(requestJson!);

        Assert.Equal(
            "30m",
            request.RootElement
                .GetProperty("keep_alive")
                .GetString());
    }

    [Theory]
    [MemberData(nameof(GenerationProfileCases))]
    public async Task StreamGenerateAsync_AppliesGenerationProfile(
        GenerationProfile profile)
    {
        string? requestJson = null;

        using HttpClient httpClient = CreateHttpClient(
            (request, _) =>
            {
                requestJson = request.Content!
                    .ReadAsStringAsync()
                    .GetAwaiter()
                    .GetResult();

                return CreateStreamingResponse(
                    """{"response":"","done":true}""");
            });
        using OllamaClient client = new(httpClient);

        await ReadAllChunksAsync(
            client,
            profile: profile);

        using JsonDocument request =
            JsonDocument.Parse(requestJson!);
        JsonElement options =
            request.RootElement.GetProperty("options");

        Assert.Equal(
            profile.MaximumOutputTokens,
            options.GetProperty("num_predict").GetInt32());
        Assert.Equal(
            profile.ContextWindowTokens,
            options.GetProperty("num_ctx").GetInt32());
        Assert.Equal(
            profile.Temperature,
            options.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task StreamGenerateAsync_ThrowsForHttpError()
    {
        using HttpClient httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using OllamaClient client = new(httpClient);

        HttpRequestException exception =
            await Assert.ThrowsAsync<HttpRequestException>(
                () => ReadAllChunksAsync(client));

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            exception.StatusCode);
    }

    [Fact]
    public async Task StreamGenerateAsync_ThrowsForMalformedResponse()
    {
        using HttpClient httpClient = CreateHttpClient(
            CreateStreamingResponse("not-json"));
        using OllamaClient client = new(httpClient);

        await Assert.ThrowsAsync<JsonException>(
            () => ReadAllChunksAsync(client));
    }

    [Fact]
    public async Task StreamGenerateAsync_ThrowsForIncompleteResponse()
    {
        const string responseBody =
            """
            {"response":"partial","done":false}
            """;

        using HttpClient httpClient = CreateHttpClient(
            CreateStreamingResponse(responseBody));
        using OllamaClient client = new(httpClient);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => ReadAllChunksAsync(client));

        Assert.Contains(
            "before generation completed",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamGenerateAsync_HonorsCancellationBeforeRequest()
    {
        using HttpClient httpClient = CreateHttpClient(
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        using OllamaClient client = new(httpClient);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ReadAllChunksAsync(client, cancellation.Token));
    }

    [Fact]
    public async Task StreamGenerateAsync_HonorsCancellationDuringStreaming()
    {
        using CancellationTokenSource cancellation = new();
        using HttpClient httpClient = CreateHttpClient(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new CancelableStreamingResponse())
            });
        using OllamaClient client = new(httpClient);

        await using IAsyncEnumerator<string> chunks =
            client.StreamGenerateAsync(
                    "test-model",
                    "test prompt",
                    GenerationProfiles.Balanced,
                    cancellation.Token)
                .GetAsyncEnumerator();

        Assert.True(await chunks.MoveNextAsync());
        Assert.Equal("first", chunks.Current);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await chunks.MoveNextAsync());
    }

    private static async Task ReadAllChunksAsync(
        OllamaClient client,
        CancellationToken cancellationToken = default,
        GenerationProfile? profile = null)
    {
        await foreach (string _ in
            client.StreamGenerateAsync(
                "test-model",
                "test prompt",
                profile ?? GenerationProfiles.Balanced,
                cancellationToken))
        {
        }
    }

    public static TheoryData<GenerationProfile>
        GenerationProfileCases =>
        new()
        {
            GenerationProfiles.Fast,
            GenerationProfiles.Balanced,
            GenerationProfiles.Accurate
        };

    private static HttpResponseMessage CreateStreamingResponse(
        string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                "application/x-ndjson")
        };

    private static HttpClient CreateHttpClient(
        HttpResponseMessage response) =>
        CreateHttpClient((_, _) => response);

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>
            responder)
    {
        StubHttpMessageHandler handler = new(responder);

        return new HttpClient(handler)
        {
            BaseAddress =
                new Uri("http://127.0.0.1:11434/")
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>
            responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                responder(request, cancellationToken));
        }
    }

    private sealed class CancelableStreamingResponse : Stream
    {
        private static readonly byte[] FirstChunk =
            Encoding.UTF8.GetBytes(
                "{\"response\":\"first\",\"done\":false}\n");

        private bool _firstChunkWritten;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_firstChunkWritten)
            {
                _firstChunkWritten = true;
                FirstChunk.CopyTo(buffer);
                return FirstChunk.Length;
            }

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);

            return 0;
        }

        public override void Flush() =>
            throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}
