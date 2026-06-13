using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace CustomerService.Services;

public class CacheService
{
    private readonly IDatabase? _db;
    private readonly bool _enabled;
    private readonly int _ttlMinutes;
    private long _cacheHits;
    private long _cacheMisses;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CacheService(IConfiguration config)
    {
        _enabled = bool.Parse(config["Cache:Enabled"] ?? "true");
        _ttlMinutes = int.Parse(config["Cache:TtlMinutes"] ?? "60");

        if (_enabled)
        {
            var connString = config["Redis:ConnectionString"] ?? "localhost:6379";
            var redis = ConnectionMultiplexer.Connect(connString);
            _db = redis.GetDatabase();
        }
    }

    public async Task<(bool Hit, string? Answer, List<string>? Sources, List<string>? Citations)> TryGetAsync(string query)
    {
        if (!_enabled)
            return (false, null, null, null);

        try
        {
            var queryHash = ComputeHash(query);
            var cacheKey = $"cache:{queryHash}";
            var cached = await _db!.StringGetAsync(cacheKey);
            if (!cached.IsNull)
            {
                var entry = JsonSerializer.Deserialize<CacheEntry>(cached!, _jsonOptions);
                if (entry != null)
                {
                    Interlocked.Increment(ref _cacheHits);
                    UpdateCacheHitRatio();
                    return (true, entry.Answer, entry.Sources, entry.Citations);
                }
            }
        }
        catch
        {
            // Cache miss on error
        }

        Interlocked.Increment(ref _cacheMisses);
        UpdateCacheHitRatio();
        return (false, null, null, null);
    }

    public async Task SetAsync(string query, string answer, List<string> sources, List<string> citations)
    {
        if (!_enabled) return;

        try
        {
            var queryHash = ComputeHash(query);
            var cacheKey = $"cache:{queryHash}";
            var entry = new CacheEntry
            {
                Query = query,
                Answer = answer,
                Sources = sources,
                Citations = citations,
                Timestamp = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(entry, _jsonOptions);
            await _db!.StringSetAsync(cacheKey, json, TimeSpan.FromMinutes(_ttlMinutes));
        }
        catch
        {
            // Cache set failure is non-critical
        }
    }

    private string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16];
    }

    private void UpdateCacheHitRatio()
    {
        var total = Interlocked.Read(ref _cacheHits) + Interlocked.Read(ref _cacheMisses);
        if (total > 0)
            PrometheusMetrics.CacheHitRatio.Set((double)Interlocked.Read(ref _cacheHits) / total);
    }

    private class CacheEntry
    {
        public string Query { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public List<string> Sources { get; set; } = new();
        public List<string> Citations { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
}
