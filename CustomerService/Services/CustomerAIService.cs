using System.Text;
using System.Text.Json;
using CustomerService.Models;

namespace CustomerService.Services;

public class CustomerAIService
{
    private readonly HttpClient _httpClient;
    private readonly SessionService _sessionService;
    private readonly CacheService _cacheService;
    private readonly ILogger<CustomerAIService> _logger;

    public CustomerAIService(IConfiguration config, IHttpClientFactory httpClientFactory,
        SessionService sessionService, CacheService cacheService, ILogger<CustomerAIService> logger)
    {
        _sessionService = sessionService;
        _cacheService = cacheService;
        _logger = logger;
        var baseUrl = config["CliproxyApi:BaseUrl"]!;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<CustomerChatResponse> ChatAsync(string userId, string userMessage)
    {
        var sessionId = $"sess_{userId}_{Guid.NewGuid():N}"[..16];

        // Check cache
        var (cacheHit, cachedReply, cachedSources, cachedCitations) = await _cacheService.TryGetAsync(userMessage);
        if (cacheHit)
        {
            _logger.LogInformation("Cache hit for query from user {UserId}", userId);
            await _sessionService.AddMessageAsync(userId, "user", userMessage);
            await _sessionService.AddMessageAsync(userId, "assistant", cachedReply!);
            return new CustomerChatResponse
            {
                Reply = cachedReply!,
                SessionId = sessionId,
                Sources = cachedSources ?? new(),
                Citations = cachedCitations ?? new()
            };
        }

        var history = await _sessionService.GetMessagesAsync(userId);

        var proxyRequest = new ProxyChatRequest
        {
            Message = userMessage,
            UseRag = true,
            History = history.Select(h => (h.Role, h.Content)).TakeLast(10).ToList()
        };
        var content = new StringContent(JsonSerializer.Serialize(proxyRequest), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/api/v1/chat", content);
        response.EnsureSuccessStatusCode();

        var proxyResponse = await response.Content.ReadFromJsonAsync<ProxyChatResponse>();

        await _sessionService.AddMessageAsync(userId, "user", userMessage);
        await _sessionService.AddMessageAsync(userId, "assistant", proxyResponse!.Reply);

        // Store in cache
        await _cacheService.SetAsync(userMessage, proxyResponse.Reply, proxyResponse.Sources, proxyResponse.Citations);

        return new CustomerChatResponse
        {
            Reply = proxyResponse.Reply,
            SessionId = sessionId,
            Sources = proxyResponse.Sources,
            Citations = proxyResponse.Citations
        };
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(string userId, string userMessage)
    {
        var history = await _sessionService.GetMessagesAsync(userId);

        var proxyRequest = new ProxyChatRequest
        {
            Message = userMessage,
            UseRag = true,
            History = history.Select(h => (h.Role, h.Content)).TakeLast(10).ToList()
        };
        var json = JsonSerializer.Serialize(proxyRequest);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/stream") { Content = httpContent };
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var fullReply = new StringBuilder();
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            if (!line.StartsWith("data: ")) continue;
            var data = line[6..];
            if (data == "[DONE]") break;
            if (data.StartsWith("[ERROR]")) throw new Exception(data);

            fullReply.Append(data);
            yield return data;
        }

        await _sessionService.AddMessageAsync(userId, "user", userMessage);
        await _sessionService.AddMessageAsync(userId, "assistant", fullReply.ToString());
    }

    public async Task<List<object>> GetHistoryAsync(string userId)
    {
        var messages = await _sessionService.GetMessagesAsync(userId);
        return messages.Select(m => (object)new { m.Role, m.Content }).ToList();
    }
}
