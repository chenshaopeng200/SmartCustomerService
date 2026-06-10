using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class SelfConsistencyService
{
    private readonly LLMService _llmService;
    private readonly ILogger<SelfConsistencyService> _logger;

    public SelfConsistencyService(LLMService llmService, ILogger<SelfConsistencyService> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<(bool IsSupported, string VerifiedAnswer)> VerifyAsync(string answer, string context)
    {
        try
        {
            var prompt = $@"请判断以下回答是否完全由参考资料支持。

参考资料:
{context}

回答:
{answer}

如果回答中的每一句话都能在参考资料中找到依据，请输出 YES。
如果回答中包含编造的信息、与参考资料矛盾的内容、或参考资料中不存在的信息，请输出 NO。

只输出 YES 或 NO。";

            var isSupported = await _llmService.EvaluateAsync(prompt);

            if (isSupported)
                return (true, answer);

            return (false, "资料不足，无法回答该问题。请提供更多相关信息以便为您准确解答。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Self-consistency check failed, returning original answer");
            return (true, answer);
        }
    }
}
