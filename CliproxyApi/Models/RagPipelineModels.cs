namespace CliproxyApi.Models;

public class RagPipelineConfig
{
    public bool EnableQueryRewriting { get; set; } = true;
    public bool EnableHyDE { get; set; } = true;
    public bool EnableHybridSearch { get; set; } = true;
    public bool EnableReranking { get; set; } = true;
    public bool EnableContextCompression { get; set; } = true;
    public bool EnableSelfConsistency { get; set; } = true;
}

public class HybridSearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class RagContext
{
    public string OriginalQuery { get; set; } = string.Empty;
    public List<string> RewrittenQueries { get; set; } = new();
    public List<QdrantSearchResult> RetrievedDocs { get; set; } = new();
    public string CompressedContext { get; set; } = string.Empty;
    public string FinalAnswer { get; set; } = string.Empty;
    public List<string> Citations { get; set; } = new();
}
