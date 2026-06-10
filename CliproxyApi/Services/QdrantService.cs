using Qdrant.Client;
using Qdrant.Client.Grpc;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class QdrantService
{
    private readonly QdrantClient _client;
    private readonly string _collectionName;
    private readonly LLMService _llmService;
    private readonly ILogger<QdrantService> _logger;

    public QdrantService(IConfiguration config, LLMService llmService, ILogger<QdrantService> logger)
    {
        _llmService = llmService;
        _logger = logger;
        var host = config["Qdrant:Host"]!;
        var port = int.Parse(config["Qdrant:Port"]!);
        _collectionName = config["Qdrant:CollectionName"]!;
        _client = new QdrantClient(host, port);
    }

    public List<QdrantSearchResult> GetAllChunks()
    {
        try
        {
            var results = new List<QdrantSearchResult>();
            var response = _client.ScrollAsync(_collectionName, limit: 1000).GetAwaiter().GetResult();
            foreach (var p in response.Result)
            {
                results.Add(new QdrantSearchResult
                {
                    Text = p.Payload["text"].StringValue,
                    Source = p.Payload.TryGetValue("source", out var src) ? src.StringValue : "unknown",
                    Score = 0
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scroll all chunks from Qdrant");
            return new List<QdrantSearchResult>();
        }
    }

    public async Task<List<QdrantSearchResult>> RetrieveRelevantChunks(string query, int topK = 5)
    {
        try
        {
            var queryVector = await _llmService.GetEmbedding(query);
            var searchResult = await _client.SearchAsync(_collectionName, queryVector.ToArray(), limit: (ulong)topK);

            return searchResult.Select(r => new QdrantSearchResult
            {
                Text = r.Payload["text"].StringValue,
                Source = r.Payload.TryGetValue("source", out var src) ? src.StringValue : "unknown",
                Score = r.Score
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant retrieval failed, returning empty results");
            return new List<QdrantSearchResult>();
        }
    }
}
