using Prometheus;

namespace CustomerService.Services;

public static class PrometheusMetrics
{
    public static readonly Counter ChatRequestsTotal = Metrics
        .CreateCounter("chat_requests_total", "Total chat requests", new[] { "status" });

    public static readonly Histogram ChatRequestDuration = Metrics
        .CreateHistogram("chat_request_duration_seconds", "Chat request duration",
            new HistogramConfiguration { Buckets = new[] { 0.1, 0.5, 1, 2, 5, 10, 30 } });

    public static readonly Gauge CacheHitRatio = Metrics
        .CreateGauge("cache_hit_ratio", "Cache hit ratio");

    public static readonly Gauge ActiveSessions = Metrics
        .CreateGauge("active_sessions", "Active session count");
}
