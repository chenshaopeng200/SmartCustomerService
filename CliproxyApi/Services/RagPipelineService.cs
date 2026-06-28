using CliproxyApi.Models;
using Prometheus;

namespace CliproxyApi.Services;

public class RagPipelineService
{
    private readonly QdrantService _qdrantService;
    private readonly LLMService _llmService;
    private readonly QueryRewriterService _queryRewriter;
    private readonly HybridSearchService _hybridSearch;
    private readonly RerankerService _reranker;
    private readonly ContextCompressorService _compressor;
    private readonly SelfConsistencyService _selfConsistency;
    private readonly FunctionCallingService _functionCalling;
    private readonly ToolRegistry _toolRegistry;
    private readonly RagPipelineConfig _config;
    private readonly int _finalTopK;
    private readonly int _vectorTopK;
    private readonly int _keywordTopK;
    private readonly int _rrfConstant;
    private readonly ILogger<RagPipelineService> _logger;

    public RagPipelineService(
        QdrantService qdrantService, LLMService llmService,
        QueryRewriterService queryRewriter, HybridSearchService hybridSearch,
        RerankerService reranker, ContextCompressorService compressor,
        SelfConsistencyService selfConsistency, FunctionCallingService functionCalling,
        ToolRegistry toolRegistry, IConfiguration config,
        ILogger<RagPipelineService> logger)
    {
        _qdrantService = qdrantService;
        _llmService = llmService;
        _queryRewriter = queryRewriter;
        _hybridSearch = hybridSearch;
        _reranker = reranker;
        _compressor = compressor;
        _selfConsistency = selfConsistency;
        _functionCalling = functionCalling;
        _toolRegistry = toolRegistry;
        _logger = logger;

        _config = config.GetSection("RAG:Features").Get<RagPipelineConfig>() ?? new RagPipelineConfig();
        _finalTopK = int.Parse(config["RAG:TopK"] ?? "5");
        _vectorTopK = int.Parse(config["RAG:HybridSearch:VectorTopK"] ?? "10");
        _keywordTopK = int.Parse(config["RAG:HybridSearch:KeywordTopK"] ?? "10");
        _rrfConstant = int.Parse(config["RAG:HybridSearch:RrfConstant"] ?? "60");
    }

    public async Task<(string Reply, List<string> Sources, List<string> Citations, List<string> ContextTexts)> ExecuteAsync(
        string query, List<(string Role, string Content)>? history = null, RagFeatureOverrides? featureOverrides = null)
    {
        _logger.LogInformation("RAG pipeline started");
        using var timer = PrometheusMetrics.RagPipelineDuration.NewTimer();

        var enableTools = featureOverrides?.EnableTools ?? false;
        if (enableTools)
        {
            _logger.LogInformation("Function Calling mode enabled (lightweight, no RAG preload)");
            var messages = BuildToolModeMessages(query, history);
            return await _functionCalling.RunAsync(messages);
        }

        var contextResult = await BuildContextAsync(query, history, featureOverrides);
        if (contextResult == null)
        {
            var fallbackReply = await _llmService.ChatDirect(query);
            return (fallbackReply, new List<string>(), new List<string>(), new List<string>());
        }

        var (context, _, _) = contextResult.Value;

        var answer = await _llmService.ChatWithAnchoredContext(context, query);

        var enableSelfConsistency = featureOverrides?.EnableSelfConsistency ?? _config.EnableSelfConsistency;
        if (enableSelfConsistency)
        {
            var (isSupported, verifiedAnswer) = await _selfConsistency.VerifyAsync(answer, context);
            answer = verifiedAnswer;
            _logger.LogInformation("Self-consistency: supported={Supported}", isSupported);
        }

        var sources = contextResult.Value.Docs.Select(d => d.Source).Distinct().ToList();
        var contextTexts = contextResult.Value.Docs.Select(d => d.Text).ToList();
        var citations = ExtractCitations(answer);

        return (answer, sources, citations, contextTexts);
    }

    public async Task<List<LLMChatMessage>> BuildMessagesAsync(
        string query, List<(string Role, string Content)>? history = null, RagFeatureOverrides? featureOverrides = null)
    {
        var contextResult = await BuildContextAsync(query, history, featureOverrides);
        if (contextResult == null)
            return new List<LLMChatMessage> { new() { Role = "user", Content = query } };

        var (context, _, _) = contextResult.Value;

        var historyStr = "";
        if (history != null && history.Count > 0)
        {
            historyStr = "之前的对话：\n" + string.Join("\n", history.Select(h => $"{h.Role}: {h.Content}")) + "\n\n";
        }

        return new List<LLMChatMessage>
        {
            new() { Role = "system", Content = $@"你是一个智能客服助手。请严格遵守以下规则：
1. 仅依据提供的参考资料回答，如果资料中未包含答案，请回复""资料不足，无法回答""，严禁编造。
2. 回答中请在引用的地方标注参考编号，如 [1]、[2]。
{historyStr}
参考知识：
{context}" },
            new() { Role = "user", Content = query }
        };
    }

    private static List<LLMChatMessage> BuildToolModeMessages(
        string query, List<(string Role, string Content)>? history)
    {
        var historyStr = "";
        if (history != null && history.Count > 0)
            historyStr = "之前的对话：\n" + string.Join("\n", history.Select(h => $"{h.Role}: {h.Content}")) + "\n\n";

        var systemPrompt = $"你是智能客服助手。如需查询知识库，使用 search_knowledge_base 工具。\n{historyStr}".TrimEnd();
        return new List<LLMChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = query }
        };
    }

    private async Task<(string Context, List<QdrantSearchResult> Docs, List<string> Sources)?> BuildContextAsync(
        string query, List<(string Role, string Content)>? history, RagFeatureOverrides? featureOverrides = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("BuildContextAsync called with empty query");
            return null;
        }
        var fo = featureOverrides;
        var enableRewriting = fo?.EnableQueryRewriting ?? _config.EnableQueryRewriting;
        var enableHyDE = fo?.EnableHyDE ?? _config.EnableHyDE;
        var enableHybrid = fo?.EnableHybridSearch ?? _config.EnableHybridSearch;
        var enableReranking = fo?.EnableReranking ?? _config.EnableReranking;
        var enableCompression = fo?.EnableContextCompression ?? _config.EnableContextCompression;
        var enableSelfConsistency = fo?.EnableSelfConsistency ?? _config.EnableSelfConsistency;

        // Step 1: Query Rewriting (with history context, Multi-Query + HyDE parallel)
        var queries = new List<string> { query };
        if (enableRewriting)
        {
            var historyContext = history != null && history.Count > 0
                ? "\n对话历史：" + string.Join("\n", history.TakeLast(6).Select(h => $"{h.Role}: {h.Content}"))
                : "";
            var rewriteInput = historyContext.Length > 0
                ? $"结合以下对话历史，改写当前问题：{historyContext}\n当前问题：{query}"
                : query;

            var multiQueryTask = _queryRewriter.MultiQueryRewriteAsync(rewriteInput);
            var hydeTask = enableHyDE ? _queryRewriter.GenerateHyDEAsync(query) : Task.FromResult<string?>(null);

            await Task.WhenAll(multiQueryTask, hydeTask);

            var rewritten = await multiQueryTask;
            if (rewritten.Count > 0)
                queries = rewritten;
            else
                queries = new List<string> { query };

            var hyde = await hydeTask;
            if (!string.IsNullOrWhiteSpace(hyde))
                queries.Add(hyde);

            _logger.LogInformation("Query rewriting produced {Count} queries (parallel)", queries.Count);
        }

        // Step 2: Retrieval
        List<HybridSearchResult> retrieved;
        if (enableHybrid)
        {
            retrieved = await _hybridSearch.SearchAsync(queries, _finalTopK * 3, _vectorTopK, _keywordTopK, _rrfConstant);
        }
        else
        {
            var vectorResults = await _qdrantService.RetrieveRelevantChunks(query, _finalTopK * 3);
            retrieved = vectorResults.Select(r => new HybridSearchResult
            {
                Id = r.Source + "_" + Guid.NewGuid().ToString("N")[..6],
                Text = r.Text, Source = r.Source, Score = r.Score
            }).ToList();
        }
        _logger.LogInformation("Retrieval returned {Count} candidates", retrieved.Count);
        PrometheusMetrics.RagRetrievalCount.Observe(retrieved.Count);

        if (retrieved.Count == 0)
            return null;

        // Step 3: Reranking
        var docs = retrieved.Select(r => new QdrantSearchResult
        {
            Text = r.Text, Source = r.Source, Score = r.Score
        }).ToList();

        if (enableReranking)
        {
            docs = await _reranker.RerankAsync(query, docs, _finalTopK);
        }
        else
        {
            docs = docs.OrderByDescending(d => d.Score).Take(_finalTopK).ToList();
        }

        // Step 4: Context Compression
        string context;
        if (enableCompression)
        {
            context = await _compressor.CompressAsync(query, docs);
        }
        else
        {
            context = string.Join("\n---\n", docs.Select(d => d.Text));
        }

        // Step 5: Self-consistency check
        if (enableSelfConsistency)
        {
            _logger.LogInformation("Self-consistency enabled (applied after answer generation)");
        }

        var sources = docs.Select(d => d.Source).Distinct().ToList();
        return (context, docs, sources);
    }

    private List<string> ExtractCitations(string answer)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(answer, @"\[\d+\]");
        var seen = new HashSet<string>();
        var citations = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var marker = m.Value;
            if (seen.Add(marker))
                citations.Add(marker);
        }
        return citations;
    }
}
