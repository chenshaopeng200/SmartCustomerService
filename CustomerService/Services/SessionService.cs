using System.Text.Json;
using StackExchange.Redis;

namespace CustomerService.Services;

public class SessionService
{
    private readonly IDatabase _db;
    private readonly int _ttlMinutes;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SessionService(IConfiguration config)
    {
        var connString = config["Redis:ConnectionString"] ?? "localhost:6379";
        _ttlMinutes = int.Parse(config["Session:TtlMinutes"] ?? "30");
        var redis = ConnectionMultiplexer.Connect(connString);
        _db = redis.GetDatabase();
    }

    public async Task AddMessageAsync(string userId, string role, string content)
    {
        var key = $"session:{userId}";
        var messages = await GetMessagesAsync(userId);
        messages.Add(new ChatMessage { Role = role, Content = content, Timestamp = DateTime.UtcNow });
        var json = JsonSerializer.Serialize(messages, _jsonOptions);
        await _db.StringSetAsync(key, json, TimeSpan.FromMinutes(_ttlMinutes));
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(string userId)
    {
        var key = $"session:{userId}";
        var json = await _db.StringGetAsync(key);
        if (json.IsNull)
            return new List<ChatMessage>();
        return JsonSerializer.Deserialize<List<ChatMessage>>(json!, _jsonOptions) ?? new List<ChatMessage>();
    }

    public async Task ClearSessionAsync(string userId)
    {
        var key = $"session:{userId}";
        await _db.KeyDeleteAsync(key);
    }
}

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
