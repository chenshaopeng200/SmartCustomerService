using System.Text;
using System.Text.Json;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class LLMService
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _embeddingHttpClient;
    private readonly string _chatModel;
    private readonly string _embeddingModel;
    private readonly ILogger<LLMService> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public LLMService(IConfiguration config, ILogger<LLMService> logger)
    {
        _logger = logger;
        var baseUrl = config["CliproxyApi:BaseUrl"]!;
        var apiKey = config["CliproxyApi:ApiKey"]!;
        _chatModel = config["CliproxyApi:ChatModel"]!;
        _embeddingModel = config["EmbeddingApi:Model"] ?? config["CliproxyApi:EmbeddingModel"]!;

        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var embeddingBaseUrl = config["EmbeddingApi:BaseUrl"]!;
        var embeddingApiKey = config["EmbeddingApi:ApiKey"]!;
        _embeddingHttpClient = new HttpClient { BaseAddress = new Uri(embeddingBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _embeddingHttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {embeddingApiKey}");
    }

    public async Task<float[]> GetEmbedding(string text, string inputType = "passage")
    {
        PrometheusMetrics.LlmCallsTotal.WithLabels("embedding").Inc();
        var request = new EmbeddingRequest { Model = _embeddingModel, Input = text, InputType = inputType };
        var content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
        var response = await _embeddingHttpClient.PostAsync("embeddings", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(_jsonOptions);
        if (result is null || result.Data is null || result.Data.Count == 0)
        {
            _logger.LogError("Embedding API returned unexpected response: result={ResultIsNull}, dataCount={DataCount}",
                result is null, result?.Data?.Count ?? 0);
            throw new InvalidOperationException(
                "Embedding API returned an unexpected response. Please check the embedding service is healthy.");
        }
        return result.Data[0].Embedding.ToArray();
    }

    public async Task<string> ChatWithContext(string context, string userMessage)
    {
        var messages = new List<LLMChatMessage>
        {
            new() { Role = "system", Content = $"你是一个智能客服助手。请基于以下参考知识回答用户问题。如果参考知识中没有相关信息，请如实告知。\n\n参考知识：\n{context}" },
            new() { Role = "user", Content = userMessage }
        };

        return await Chat(messages) ?? string.Empty;
    }

    public async Task<string> ChatWithAnchoredContext(string context, string userMessage)
    {
        var messages = new List<LLMChatMessage>
        {
            new() { Role = "system", Content = $"你是一个智能客服助手。请严格遵守以下规则：\n1. 仅依据提供的参考资料回答，如果资料中未包含答案，请回复\"资料不足，无法回答\"，严禁编造。\n2. 回答中请在引用的地方标注参考编号，如 [1]、[2]。\n\n参考知识：\n{context}" },
            new() { Role = "user", Content = userMessage }
        };

        return await Chat(messages) ?? string.Empty;
    }

    public async Task<string> ChatDirect(string userMessage)
    {
        var messages = new List<LLMChatMessage>
        {
            new() { Role = "user", Content = userMessage }
        };

        return await Chat(messages) ?? string.Empty;
    }

    public async Task<string> ChatWithMessages(List<LLMChatMessage> messages)
    {
        return await Chat(messages) ?? string.Empty;
    }

    public async Task<LLMChatResponse> ChatWithTools(
        List<LLMChatMessage> messages,
        List<ToolDefinition>? tools,
        string? toolChoice = null)
    {
        PrometheusMetrics.LlmCallsTotal.WithLabels("chat").Inc();
        var request = new LLMChatRequest
        {
            Model = _chatModel,
            Messages = messages,
            Tools = tools,
            ToolChoice = toolChoice
        };
        var content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LLMChatResponse>(_jsonOptions);
        if (result is null || result.Choices is null || result.Choices.Count == 0)
        {
            _logger.LogError("Chat API returned unexpected response for {MessageCount} messages", messages.Count);
            throw new InvalidOperationException(
                "Chat API returned an unexpected response. Please check the chat service is healthy.");
        }
        return result;
    }

    public async Task<bool> EvaluateAsync(string prompt)
    {
        var messages = new List<LLMChatMessage>
        {
            new() { Role = "system", Content = "你是一个严格的审核员。请只回答 YES 或 NO。" },
            new() { Role = "user", Content = prompt }
        };

        var result = await Chat(messages);
        return (result ?? string.Empty).Trim().ToUpperInvariant().StartsWith("YES");
    }

    public async IAsyncEnumerable<string> StreamChatAsync(List<LLMChatMessage> messages)
    {
        var requestObj = new { model = _chatModel, messages, stream = true };
        var json = JsonSerializer.Serialize(requestObj, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = content };
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            if (!line.StartsWith("data: ")) continue;
            var data = line[6..];
            if (data == "[DONE]") break;

            var text = ExtractDeltaContent(data);
            if (text != null)
                yield return text;
        }
    }

    private static string? ExtractDeltaContent(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;
            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var contentEl))
            {
                var text = contentEl.GetString();
                return !string.IsNullOrEmpty(text) ? text : null;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> Chat(List<LLMChatMessage> messages)
    {
        PrometheusMetrics.LlmCallsTotal.WithLabels("chat").Inc();
        var request = new LLMChatRequest { Model = _chatModel, Messages = messages };
        var content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LLMChatResponse>(_jsonOptions);
        if (result is null)
        {
            _logger.LogWarning("Chat API returned null response for {MessageCount} messages", messages.Count);
            return null;
        }
        if (result.Choices is null || result.Choices.Count == 0)
        {
            _logger.LogWarning("Chat API returned empty choices for {MessageCount} messages", messages.Count);
            return null;
        }
        var message = result.Choices[0].Message;
        if (message is null)
        {
            _logger.LogWarning("Chat API returned null message in choice 0 for {MessageCount} messages", messages.Count);
            return null;
        }
        var replyContent = message.Content;
        if (string.IsNullOrEmpty(replyContent))
        {
            _logger.LogWarning("Chat API returned empty content in choice 0 for {MessageCount} messages", messages.Count);
            return null;
        }
        return replyContent;
    }
}
