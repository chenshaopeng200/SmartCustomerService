using System.Text;
using System.Text.Json;
using CustomerService.Models;

namespace CustomerService.Services;

public class EvalService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EvalService> _logger;

    public EvalService(IConfiguration config, IHttpClientFactory httpClientFactory,
        ILogger<EvalService> logger)
    {
        _logger = logger;
        var baseUrl = config["CliproxyApi:BaseUrl"]!;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<EvalResult> AutoEvaluateAsync(string query, string? userId = null)
    {
        var proxyRequest = new ProxyChatRequest { Message = query, UseRag = true };
        var content = new StringContent(JsonSerializer.Serialize(proxyRequest), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/v1/chat", content);
        response.EnsureSuccessStatusCode();

        var proxyResponse = await response.Content.ReadFromJsonAsync<ProxyChatResponse>();
        var answer = proxyResponse!.Reply;
        var sources = proxyResponse.Sources;
        var contextTexts = proxyResponse.ContextTexts;

        var contextStr = contextTexts.Count > 0
            ? string.Join("\n---\n", contextTexts)
            : string.Join("\n---\n", sources);

        var faithfulness = await EvaluateFaithfulnessAsync(query, answer, contextStr);
        var relevance = await EvaluateRelevanceAsync(query, answer);
        var precision = ComputeRetrievalPrecision(answer, sources.Count);

        var result = new EvalResult
        {
            Faithfulness = faithfulness,
            Relevance = relevance,
            RetrievalPrecision = precision,
            OverallScore = Math.Round((faithfulness + relevance + precision) / 3.0, 3),
            Details = $"Query: {query}\nAnswer: {answer[..Math.Min(answer.Length, 200)]}..."
        };

        _logger.LogInformation("Eval: Faith={F}, Relevance={R}, Precision={P}, Overall={O}",
            faithfulness, relevance, precision, result.OverallScore);

        return result;
    }

    public async Task<CompareResult> CompareAsync(string query,
        RagFeatureFlags? configA = null, RagFeatureFlags? configB = null)
    {
        configA ??= new RagFeatureFlags();
        configB ??= new RagFeatureFlags
        {
            EnableQueryRewriting = false,
            EnableHyDE = false,
            EnableHybridSearch = true,
            EnableReranking = false,
            EnableContextCompression = false,
            EnableSelfConsistency = false
        };

        var sideA = await RunWithConfig(query, configA, "完整流水线");
        var sideB = await RunWithConfig(query, configB, "基础检索");

        var winner = DetermineWinner(sideA.Scores, sideB.Scores);

        return new CompareResult
        {
            Query = query,
            SideA = sideA,
            SideB = sideB,
            Winner = winner,
            Analysis = $"A ({sideA.Scores.OverallScore:F2}) vs B ({sideB.Scores.OverallScore:F2}) — {winner}"
        };
    }

    private async Task<CompareSideResult> RunWithConfig(string query, RagFeatureFlags config, string label)
    {
        var proxyRequest = new ProxyChatRequest
        {
            Message = query,
            UseRag = true,
            FeatureOverrides = config
        };
        var content = new StringContent(JsonSerializer.Serialize(proxyRequest), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/v1/chat", content);
        response.EnsureSuccessStatusCode();

        var proxyResponse = await response.Content.ReadFromJsonAsync<ProxyChatResponse>();
        var answer = proxyResponse!.Reply;
        var sources = proxyResponse.Sources;
        var contextTexts = proxyResponse.ContextTexts;
        var contextStr = contextTexts.Count > 0
            ? string.Join("\n---\n", contextTexts)
            : string.Join("\n---\n", sources);

        var faithfulness = await EvaluateFaithfulnessAsync(query, answer, contextStr);
        var relevance = await EvaluateRelevanceAsync(query, answer);
        var precision = ComputeRetrievalPrecision(answer, sources.Count);

        return new CompareSideResult
        {
            Label = label,
            Config = config,
            Answer = answer,
            Sources = sources,
            Scores = new EvalResult
            {
                Faithfulness = faithfulness,
                Relevance = relevance,
                RetrievalPrecision = precision,
                OverallScore = Math.Round((faithfulness + relevance + precision) / 3.0, 3)
            }
        };
    }

    private async Task<double> EvaluateFaithfulnessAsync(string query, string answer, string context)
    {
        try
        {
            var prompt = $@"评估以下回答是否忠实于提供的参考资料，是否存在编造内容。

问题: {query}

参考资料:
{context}

回答:
{answer}

请判断回答中的每个陈述是否都能在参考资料中找到依据。如果回答中包含参考资料中没有的信息，则视为不忠实。
请只输出0到1之间的分数和简短理由。格式: 分数|理由";

            var evalResponse = await CallLLMDirectAsync(prompt);
            return ParseScore(evalResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Faithfulness evaluation failed");
            return 0.5;
        }
    }

    private async Task<double> EvaluateRelevanceAsync(string query, string answer)
    {
        try
        {
            var prompt = $@"评估以下回答是否直接、完整地回答了用户的问题。

问题: {query}

回答:
{answer}

请判断回答是否切题、是否直接解决了用户的问题。如果回答偏离主题或不完整，则降低评分。
请只输出0到1之间的分数和简短理由。格式: 分数|理由";

            var evalResponse = await CallLLMDirectAsync(prompt);
            return ParseScore(evalResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relevance evaluation failed");
            return 0.5;
        }
    }

    private double ComputeRetrievalPrecision(string answer, int retrievedCount)
    {
        if (retrievedCount == 0) return 0;
        var citationCount = 0;
        for (int i = 1; i <= 20; i++)
        {
            if (answer.Contains($"[{i}]"))
                citationCount++;
        }
        return Math.Min(1.0, Math.Round((double)citationCount / retrievedCount, 3));
    }

    private async Task<string> CallLLMDirectAsync(string prompt)
    {
        var request = new ProxyChatRequest { Message = prompt, UseRag = false };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/v1/chat", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProxyChatResponse>();
        return result!.Reply;
    }

    private double ParseScore(string response)
    {
        var parts = response.Split('|');
        if (parts.Length > 0 && double.TryParse(parts[0].Trim(), out var score))
            return Math.Clamp(Math.Round(score, 3), 0, 1);
        return 0.5;
    }

    private string DetermineWinner(EvalResult a, EvalResult b)
    {
        var diff = a.OverallScore - b.OverallScore;
        if (Math.Abs(diff) < 0.05) return "平局 (Tie)";
        return diff > 0 ? "配置 A 更优" : "配置 B 更优";
    }
}
