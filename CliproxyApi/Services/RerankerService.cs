using System.Text;
using System.Text.Json;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class RerankerService
{
    private readonly LLMService _llmService;
    private readonly ILogger<RerankerService> _logger;

    public RerankerService(LLMService llmService, ILogger<RerankerService> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<List<QdrantSearchResult>> RerankAsync(string query, List<QdrantSearchResult> candidates, int topK)
    {
        if (candidates.Count <= topK)
            return candidates;

        try
        {
            var docList = new StringBuilder();
            for (int i = 0; i < candidates.Count; i++)
            {
                var text = candidates[i].Text ?? "";
                var truncated = text.Length > 300 ? text[..300] : text;
                docList.AppendLine($"[{i}] {truncated}");
            }

            var prompt = $@"请对以下文档按与问题的相关性从高到低排序，只输出排序后的文档编号JSON数组。
问题: {query}

文档列表:
{docList}

请只输出一个JSON数组格式，例如: [3, 0, 5, 1, 2, 4]";

            var response = await _llmService.ChatWithMessages(new List<LLMChatMessage>
            {
                new() { Role = "system", Content = "你是一个文档排序专家。只输出JSON数组，不要包含其他文字。" },
                new() { Role = "user", Content = prompt }
            });

            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response[jsonStart..(jsonEnd + 1)];
                var order = JsonSerializer.Deserialize<List<int>>(json);
                if (order != null)
                {
                    return order.Where(i => i >= 0 && i < candidates.Count)
                                .Take(topK)
                                .Select(i => candidates[i])
                                .ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reranking failed, falling back to original score ordering");
        }

        return candidates.OrderByDescending(c => c.Score).Take(topK).ToList();
    }
}
