using System.Text.Json;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class QueryRewriterService
{
    private readonly LLMService _llmService;
    private readonly ILogger<QueryRewriterService> _logger;

    public QueryRewriterService(LLMService llmService, ILogger<QueryRewriterService> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<List<string>> RewriteAsync(string query, bool useHyDE)
    {
        var queries = new List<string>();

        try
        {
            var rewritten = await MultiQueryRewriteAsync(query);
            queries.AddRange(rewritten);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Multi-Query rewriting failed, using original query only");
        }

        if (queries.Count == 0)
            queries.Add(query);

        if (useHyDE)
        {
            try
            {
                var hyde = await GenerateHyDEAsync(query);
                if (!string.IsNullOrWhiteSpace(hyde))
                    queries.Add(hyde);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HyDE generation failed, continuing without it");
            }
        }

        return queries;
    }

    public async Task<List<string>> MultiQueryRewriteAsync(string query)
    {
        var rewritePrompt = $@"你是一名搜索专家。请将以下用户问题改写为3个更具体、包含专业术语的检索查询。
原始问题: {query}
请只输出一个JSON数组，不要包含其他文字。例如: [""查询1"", ""查询2"", ""查询3""]";

        var response = await _llmService.ChatWithMessages(new List<LLMChatMessage>
        {
            new() { Role = "system", Content = "你是一个搜索查询改写专家。只输出JSON数组，不要包含其他文字。" },
            new() { Role = "user", Content = rewritePrompt }
        });

        var jsonStart = response.IndexOf('[');
        var jsonEnd = response.LastIndexOf(']');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var json = response[jsonStart..(jsonEnd + 1)].Trim();
            return JsonSerializer.Deserialize<List<string>>(json)?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
        }
        return new List<string>();
    }

    public async Task<string?> GenerateHyDEAsync(string query)
    {
        var hydePrompt = $@"请根据以下问题，生成一个假设性的回答文档。这个回答不需要真实，但应包含相关领域的专业术语和典型表达方式。
问题: {query}
假设性回答:";

        var hydeAnswer = await _llmService.ChatWithMessages(new List<LLMChatMessage>
        {
            new() { Role = "user", Content = hydePrompt }
        });

        return string.IsNullOrWhiteSpace(hydeAnswer) ? null : hydeAnswer;
    }
}
