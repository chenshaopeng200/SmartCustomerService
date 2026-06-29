using System.Text.Json.Serialization;

namespace CliproxyApi.Models;

/// <summary>
/// Configuration toggles for each stage of the RAG pipeline.
/// Defaults are applied from appsettings.json / environment variables.
/// </summary>
public class RagPipelineConfig
{
    public bool EnableQueryRewriting { get; set; } = true;
    public bool EnableHyDE { get; set; } = true;
    public bool EnableHybridSearch { get; set; } = true;
    public bool EnableReranking { get; set; } = true;
    public bool EnableContextCompression { get; set; } = true;
    public bool EnableSelfConsistency { get; set; } = true;
}

/// <summary>
/// Result from hybrid (vector + keyword) search, before reranking.
/// </summary>
public class HybridSearchResult
{
    /// <summary>Unique document identifier (collection_id:point_id or similar).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The full text content of the retrieved chunk.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Source collection or file name.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Combined RRF score.</summary>
    public double Score { get; set; }
}

/// <summary>
/// Complete context payload assembled by the RAG pipeline.
/// Used when returning structured results to the client.
/// </summary>
public class RagContext
{
    /// <summary>The original user query that triggered the pipeline.</summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>All rewritten queries produced (multi-query + HyDE).</summary>
    public List<string> RewrittenQueries { get; set; } = new();

    /// <summary>Raw documents retrieved before reranking / compression.</summary>
    public List<QdrantSearchResult> RetrievedDocs { get; set; } = new();

    /// <summary>Final compressed context string sent to the LLM.</summary>
    public string CompressedContext { get; set; } = string.Empty;

    /// <summary>The LLM's final answer.</summary>
    public string FinalAnswer { get; set; } = string.Empty;

    /// <summary>Citation markers found in the answer, e.g. ["[1]", "[3]"].</summary>
    public List<string> Citations { get; set; } = new();

    /// <summary>Timing metadata for each pipeline stage (populated when requested).</summary>
    [JsonPropertyName("timing")]
    public RagTiming? Timing { get; set; }
}

/// <summary>
/// Per-stage timing information for the RAG pipeline execution.
/// </summary>
public class RagTiming
{
    [JsonPropertyName("query_rewriting_ms")]
    public long QueryRewritingMs { get; set; }

    [JsonPropertyName("retrieval_ms")]
    public long RetrievalMs { get; set; }

    [JsonPropertyName("reranking_ms")]
    public long RerankingMs { get; set; }

    [JsonPropertyName("compression_ms")]
    public long CompressionMs { get; set; }

    [JsonPropertyName("generation_ms")]
    public long GenerationMs { get; set; }

    [JsonPropertyName("total_ms")]
    public long TotalMs { get; set; }
}
