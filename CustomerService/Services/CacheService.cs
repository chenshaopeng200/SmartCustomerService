using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace CustomerService.Services;

public class CacheService
{
    private readonly IDatabase? _db;
    private readonly bool _enabled;
    private readonly double _similarityThreshold;
    private readonly int _ttlMinutes;
    private readonly HttpClient? _httpClient;
    private long _cacheHits;
    private long _cacheMisses;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CacheService(IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _enabled = bool.Parse(config["Cache:Enabled"] ?? "true");
        _similarityThreshold = double.Parse(config["Cache:SimilarityThreshold"] ?? "0.95");
        _ttlMinutes = int.Parse(config["Cache:TtlMinutes"] ?? "60");

        if (_enabled)
        {
            var connString = config["Redis:ConnectionString"] ?? "localhost:6379";
            var redis = ConnectionMultiplexer.Connect(connString);
            _db = redis.GetDatabase();

            var baseUrl = config["CliproxyApi:BaseUrl"]!;
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri(baseUrl);
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
                if (entry != null && CosineSimilarity(query, entry.Query) >= _similarityThreshold)
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

    private double CosineSimilarity(string a, string b)
    {
        var bigramsA = GetBigramFreq(a);
        var bigramsB = GetBigramFreq(b);
        var allBigrams = new HashSet<string>(bigramsA.Keys);
        allBigrams.UnionWith(bigramsB.Keys);

        double dot = 0, magA = 0, magB = 0;
        foreach (var bg in allBigrams)
        {
            var va = bigramsA.GetValueOrDefault(bg);
            var vb = bigramsB.GetValueOrDefault(bg);
            dot += va * vb;
            magA += va * va;
            magB += vb * vb;
        }

        if (magA == 0 || magB == 0) return 0;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    private Dictionary<string, double> GetBigramFreq(string text)
    {
        var freq = new Dictionary<string, double>();
        var lower = text.ToLowerInvariant();
        for (int i = 0; i < lower.Length - 1; i++)
        {
            if (!char.IsWhiteSpace(lower[i]) && !char.IsWhiteSpace(lower[i + 1]))
            {
                var bg = lower.Substring(i, 2);
                freq[bg] = freq.GetValueOrDefault(bg) + 1;
            }
        }
        return freq;
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
