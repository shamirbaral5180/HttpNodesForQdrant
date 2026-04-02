using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HttpNodesForQdrant.Models;
using Microsoft.Extensions.Options;

namespace HttpNodesForQdrant.Services;

public interface IQdrantService
{
    Task StoreConversationAsync(StoreConversationRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationSearchResult>> SearchConversationsAsync(SearchConversationRequest request, CancellationToken cancellationToken);
}

public sealed class QdrantService : IQdrantService
{
    private readonly HttpClient _httpClient;
    private readonly QdrantOptions _options;

    public QdrantService(HttpClient httpClient, IOptions<QdrantOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task StoreConversationAsync(StoreConversationRequest request, CancellationToken cancellationToken)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var textForEmbedding = $"{request.UserPrompt}\n{request.AgentResponse}";
        var vector = CreateEmbedding(textForEmbedding, _options.VectorSize);

        var payload = new
        {
            points = new[]
            {
                new
                {
                    id = Guid.NewGuid().ToString(),
                    vector,
                    payload = new
                    {
                        user_prompt = request.UserPrompt,
                        agent_response = request.AgentResponse,
                        timestamp = request.Timestamp,
                        wh_id = request.WhId
                    }
                }
            }
        };

        using var response = await _httpClient.PutAsJsonAsync($"collections/{_options.CollectionName}/points?wait=true", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ConversationSearchResult>> SearchConversationsAsync(SearchConversationRequest request, CancellationToken cancellationToken)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var vector = CreateEmbedding(request.UserPrompt, _options.VectorSize);
        var limit = request.Top <= 0 ? 10 : Math.Min(request.Top, 100);

        object body = string.IsNullOrWhiteSpace(request.WhId)
            ? new
            {
                vector,
                limit,
                with_payload = true
            }
            : new
            {
                vector,
                limit,
                with_payload = true,
                filter = new
                {
                    must = new[]
                    {
                        new
                        {
                            key = "wh_id",
                            match = new
                            {
                                value = request.WhId
                            }
                        }
                    }
                }
            };

        using var response = await _httpClient.PostAsJsonAsync($"collections/{_options.CollectionName}/points/search", body, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("result", out var resultArray) || resultArray.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ConversationSearchResult>();
        }

        var results = new List<ConversationSearchResult>();
        foreach (var item in resultArray.EnumerateArray())
        {
            if (!item.TryGetProperty("payload", out var payloadElement))
            {
                continue;
            }

            var userPrompt = payloadElement.TryGetProperty("user_prompt", out var up) ? up.GetString() : null;
            var agentResponse = payloadElement.TryGetProperty("agent_response", out var ar) ? ar.GetString() : null;
            var whId = payloadElement.TryGetProperty("wh_id", out var wh) ? wh.GetString() : null;

            if (string.IsNullOrWhiteSpace(userPrompt) || string.IsNullOrWhiteSpace(agentResponse) || string.IsNullOrWhiteSpace(whId))
            {
                continue;
            }

            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            if (payloadElement.TryGetProperty("timestamp", out var ts))
            {
                if (ts.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ts.GetString(), out var parsed))
                {
                    timestamp = parsed;
                }
            }

            var score = item.TryGetProperty("score", out var scoreElement) && scoreElement.TryGetDouble(out var parsedScore)
                ? parsedScore
                : 0;

            results.Add(new ConversationSearchResult
            {
                UserPrompt = userPrompt,
                AgentResponse = agentResponse,
                Timestamp = timestamp,
                WhId = whId,
                Score = score
            });
        }

        return results;
    }

    private async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CollectionName))
        {
            throw new InvalidOperationException("Qdrant collection name is missing in configuration.");
        }

        using var existsResponse = await _httpClient.GetAsync($"collections/{_options.CollectionName}", cancellationToken);
        if (existsResponse.IsSuccessStatusCode)
        {
            return;
        }

        if (existsResponse.StatusCode != HttpStatusCode.NotFound)
        {
            existsResponse.EnsureSuccessStatusCode();
        }

        var createPayload = new
        {
            vectors = new
            {
                size = _options.VectorSize,
                distance = _options.Distance
            }
        };

        using var createResponse = await _httpClient.PutAsJsonAsync($"collections/{_options.CollectionName}", createPayload, cancellationToken);
        createResponse.EnsureSuccessStatusCode();
    }

    private static float[] CreateEmbedding(string text, int vectorSize)
    {
        var source = Encoding.UTF8.GetBytes(text);
        var vector = new float[vectorSize];

        var index = 0;
        var nonce = 0;
        while (index < vectorSize)
        {
            var combined = source.Concat(BitConverter.GetBytes(nonce++)).ToArray();
            var hash = SHA256.HashData(combined);

            for (var i = 0; i < hash.Length && index < vectorSize; i++)
            {
                vector[index++] = (hash[i] / 127.5f) - 1f;
            }
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm <= 0f)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }
}
