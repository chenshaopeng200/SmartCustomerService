using System.Text;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class ContextCompressorService
{
    private readonly LLMService _llmService;
    private readonly ILogger<ContextCompressorService> _logger;

    public ContextCompressorService(LLMService llmService, ILogger<ContextCompressorService> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<string> CompressAsync(string query, List<QdrantSearchResult> docs)
    {
        if (docs == null || docs.Count == 0)
            return string.Empty;

        try
        {
            var combinedDocs = new StringBuilder();
            foreach (var doc in docs)
            {
                combinedDocs.AppendLine($"[{combinedDocs.Length}] {(doc.Text ?? "")}");
            }

            var prompt = "请从以下文档中，只提取与用户问题直接相关的句子。保留原始编号标记。\n" +
                         $"用户问题: {query}\n\n" +
                         $"文档:\n{combinedDocs}\n" +
                         "请只输出提取的相关句子，保留编号标记。如果没有相关句子，输出: 无相关内容。";

            var response = await _llmService.ChatWithMessages(new List<LLMChatMessage>
            {
                new() { Role = "system", Content = "你是一个信息提取专家。只提取与问题相关的句子，保留编号标记。" },
                new() { Role = "user", Content = prompt }
            });

            if (response.Contains("无相关内容"))
                return string.Join("\n---\n", docs.Select(d => d.Text));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Context compression failed, using original documents");
            return string.Join("\n---\n", docs.Select(d => d.Text));
        }
    }
}
