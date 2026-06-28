using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class HybridSearchService
{
    private readonly QdrantService _qdrantService;
    private readonly ILogger<HybridSearchService> _logger;

    // BM25 state
    private Dictionary<string, double>? _idf;
    private List<(string Id, string Text, string Source, double DocLen)>? _docStore;
    private double _avgDocLen;
    private static readonly object _lock = new();

    public HybridSearchService(QdrantService qdrantService, ILogger<HybridSearchService> logger)
    {
        _qdrantService = qdrantService;
        _logger = logger;
    }

    public async Task<List<HybridSearchResult>> SearchAsync(List<string> queries, int topK, int vectorTopK, int keywordTopK, int rrfConstant)
    {
        var allDocs = new Dictionary<string, HybridSearchResult>();
        var scores = new Dictionary<string, double>();

        foreach (var query in queries)
        {
            var vectorResults = await _qdrantService.RetrieveRelevantChunks(query, vectorTopK);
            for (int i = 0; i < vectorResults.Count; i++)
            {
                var id = vectorResults[i].Source + "_" + i;
                if (!allDocs.ContainsKey(id))
                {
                    allDocs[id] = new HybridSearchResult
                    {
                        Id = id, Text = vectorResults[i].Text,
                        Source = vectorResults[i].Source, Score = vectorResults[i].Score
                    };
                }
                scores[id] = scores.GetValueOrDefault(id, 0) + 1.0 / (rrfConstant + i + 1);
            }

            var keywordResults = await BM25SearchAsync(query, keywordTopK);
            for (int i = 0; i < keywordResults.Count; i++)
            {
                var id = keywordResults[i].Id;
                if (!allDocs.ContainsKey(id))
                    allDocs[id] = keywordResults[i];
                scores[id] = scores.GetValueOrDefault(id, 0) + 1.0 / (rrfConstant + i + 1);
            }
        }

        return scores.OrderByDescending(x => x.Value)
                     .Take(topK)
                     .Select(x => allDocs[x.Key])
                     .ToList();
    }

    private async Task<List<HybridSearchResult>> BM25SearchAsync(string query, int topK)
    {
        await EnsureIndexBuiltAsync();
        if (_docStore == null || _idf == null || _docStore.Count == 0)
            return new List<HybridSearchResult>();

        var queryTerms = Tokenize(query);
        var scored = new List<(double Score, int DocIdx)>();

        for (int docIdx = 0; docIdx < _docStore.Count; docIdx++)
        {
            var doc = _docStore[docIdx];
            double score = 0;
            foreach (var term in queryTerms)
            {
                if (!_idf.TryGetValue(term, out var idf)) continue;
                var tf = CountTerm(doc.Text, term);
                if (tf == 0) continue;
                score += idf * (tf * 2.5) / (tf + 1.5 * (1 - 0.75 + 0.75 * doc.DocLen / _avgDocLen));
            }
            if (score > 0)
                scored.Add((score, docIdx));
        }

        return scored.OrderByDescending(x => x.Score)
                     .Take(topK)
                     .Select(x => new HybridSearchResult
                     {
                         Id = _docStore[x.DocIdx].Id,
                         Text = _docStore[x.DocIdx].Text,
                         Source = _docStore[x.DocIdx].Source,
                         Score = x.Score
                     })
                     .ToList();
    }

    private async Task EnsureIndexBuiltAsync()
    {
        if (_docStore != null) return;

        lock (_lock)
        {
            if (_docStore != null) return;
        }

        var allChunks = await _qdrantService.GetAllChunksAsync();
        if (allChunks.Count == 0)
        {
            lock (_lock) { _docStore = new(); _idf = new(); }
            return;
        }

        var docStore = new List<(string, string, string, double)>();
        var df = new Dictionary<string, int>();
        int N = allChunks.Count;
        double totalLen = 0;

        foreach (var chunk in allChunks)
        {
            var terms = Tokenize(chunk.Text);
            var uniqueTerms = new HashSet<string>(terms);
            foreach (var t in uniqueTerms)
                df[t] = df.GetValueOrDefault(t) + 1;

            var docLen = (double)terms.Count;
            totalLen += docLen;
            docStore.Add((chunk.Source + "_bm25_" + docStore.Count, chunk.Text, chunk.Source, docLen));
        }

        var idf = new Dictionary<string, double>();
        foreach (var (term, docFreq) in df)
            idf[term] = Math.Log((N - docFreq + 0.5) / (docFreq + 0.5) + 1);

        lock (_lock)
        {
            _docStore = docStore;
            _idf = idf;
            _avgDocLen = N > 0 ? totalLen / N : 1;
        }

        _logger.LogInformation("BM25 index built: {N} docs, {V} terms, avgLen={Avg:F1}", N, idf.Count, _avgDocLen);
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var span = text.AsSpan();
        int start = -1;
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsLetterOrDigit(span[i]))
            {
                if (start < 0) start = i;
            }
            else
            {
                if (start >= 0)
                {
                    tokens.Add(span[start..i].ToString().ToLowerInvariant());
                    start = -1;
                }
            }
        }
        if (start >= 0)
            tokens.Add(span[start..].ToString().ToLowerInvariant());
        return tokens;
    }

    private static int CountTerm(string text, string term)
    {
        int count = 0, idx = 0;
        var lower = text.ToLowerInvariant();
        while ((idx = lower.IndexOf(term, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += term.Length;
        }
        return count;
    }
}
