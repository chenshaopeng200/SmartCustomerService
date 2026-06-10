using Prometheus;

namespace CliproxyApi.Services;

public static class PrometheusMetrics
{
    public static readonly Counter LlmCallsTotal = Metrics
        .CreateCounter("llm_calls_total", "Total LLM API calls", new[] { "type" });

    public static readonly Histogram RagRetrievalCount = Metrics
        .CreateHistogram("rag_retrieval_count", "Documents retrieved per RAG query",
            new HistogramConfiguration { Buckets = new[] { 1.0, 3.0, 5.0, 10.0, 15.0, 20.0, 30.0 } });

    public static readonly Histogram RagPipelineDuration = Metrics
        .CreateHistogram("rag_pipeline_duration_seconds", "RAG pipeline duration",
            new HistogramConfiguration { Buckets = new[] { 0.5, 1.0, 2.0, 5.0, 10.0, 20.0 } });

    public static readonly Counter ChatRequestsTotal = Metrics
        .CreateCounter("proxy_chat_requests_total", "Total proxy chat requests", new[] { "status" });
}
