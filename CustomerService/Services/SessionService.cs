using System.Text.Json;
using StackExchange.Redis;

namespace CustomerService.Services;

public class SessionService
{
    private readonly IDatabase _db;
    private readonly int _ttlMinutes;
    private readonly int _maxMessages;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SessionService(IConfiguration config)
    {
        var connString = config["Redis:ConnectionString"] ?? "localhost:6379";
        _ttlMinutes = int.Parse(config["Session:TtlMinutes"] ?? "30");
        _maxMessages = int.Parse(config["Session:MaxMessages"] ?? "50");
        var redis = ConnectionMultiplexer.Connect(connString);
        _db = redis.GetDatabase();
    }

    public async Task AddMessageAsync(string userId, string role, string content)
    {
        var key = $"session:{userId}";
        var msg = new ChatMessage { Role = role, Content = content, Timestamp = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(msg, _jsonOptions);
        await _db.ListRightPushAsync(key, json);
        await _db.KeyExpireAsync(key, TimeSpan.FromMinutes(_ttlMinutes));
        await _db.ListTrimAsync(key, -_maxMessages, -1);
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(string userId)
    {
        var key = $"session:{userId}";
        var values = await _db.ListRangeAsync(key);
        if (values.Length == 0)
            return new List<ChatMessage>();
        var messages = new List<ChatMessage>(values.Length);
        foreach (var v in values)
        {
            var msg = JsonSerializer.Deserialize<ChatMessage>(v!, _jsonOptions);
            if (msg != null) messages.Add(msg);
        }
        return messages;
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
