using System.Text.Json;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonSchema Parameters { get; }
    Task<string> ExecuteAsync(string argumentsJson);
}

/// <summary>
/// Helper to safely deserialize JSON arguments across all tools.
/// </summary>
internal static class ToolArgumentParser
{
    public static Dictionary<string, JsonElement>? Parse(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        }
        catch
        {
            return null;
        }
    }

    public static string? GetString(Dictionary<string, JsonElement>? args, string key)
    {
        var arg = args?.GetValueOrDefault(key);
        return arg.HasValue && arg.Value.ValueKind == JsonValueKind.String ? arg.Value.GetString() : null;
    }

    public static int GetInt(Dictionary<string, JsonElement>? args, string key, int defaultValue)
    {
        var arg = args?.GetValueOrDefault(key);
        return arg.HasValue && arg.Value.ValueKind == JsonValueKind.Number ? arg.Value.GetInt32() : defaultValue;
    }
}

public class SearchKnowledgeBaseTool : ITool
{
    private readonly QdrantService _qdrantService;

    public string Name => "search_knowledge_base";
    public string Description => "搜索知识库，查找与查询相关的文档资料";
    public JsonSchema Parameters => new()
    {
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["query"] = new() { Type = "string", Description = "搜索查询关键词" },
            ["top_k"] = new() { Type = "integer", Description = "返回结果数量，默认3", Minimum = 1, Maximum = 20 }
        },
        Required = new[] { "query" }
    };

    public SearchKnowledgeBaseTool(QdrantService qdrantService)
    {
        _qdrantService = qdrantService;
    }

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = ToolArgumentParser.Parse(argumentsJson);
        var query = ToolArgumentParser.GetString(args, "query") ?? "";
        var topK = ToolArgumentParser.GetInt(args, "top_k", 3);

        var results = await _qdrantService.RetrieveRelevantChunks(query, topK);
        return JsonSerializer.Serialize(new
        {
            results = results.Select(r => new
            {
                text = r.Text.Length > 200 ? r.Text[..200] + "..." : r.Text,
                r.Source,
                r.Score
            })
        });
    }
}

public class CreateSupportTicketTool : ITool
{
    public string Name => "create_support_ticket";
    public string Description => "创建客服工单";
    public JsonSchema Parameters => new()
    {
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["title"] = new() { Type = "string", Description = "工单标题" },
            ["description"] = new() { Type = "string", Description = "工单描述" },
            ["priority"] = new() { Type = "string", Description = "优先级", Enum = new[] { "low", "medium", "high" } }
        },
        Required = new[] { "title", "description" }
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = ToolArgumentParser.Parse(argumentsJson);
        var title = ToolArgumentParser.GetString(args, "title") ?? "";
        var ticketId = $"TKT-{Guid.NewGuid():N}"[..12];
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            ticket_id = ticketId,
            status = "created",
            title
        }));
    }
}

public class EscalateToHumanTool : ITool
{
    public string Name => "escalate_to_human";
    public string Description => "将问题转接给人工客服";
    public JsonSchema Parameters => new()
    {
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["reason"] = new() { Type = "string", Description = "转接原因" },
            ["summary"] = new() { Type = "string", Description = "问题摘要" }
        },
        Required = new[] { "reason", "summary" }
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            escalated = true,
            message = "已转接至人工客服，请稍候。",
            estimated_wait_minutes = 3
        }));
    }
}

public class GetOrderStatusTool : ITool
{
    public string Name => "get_order_status";
    public string Description => "根据订单ID查询订单状态";
    public JsonSchema Parameters => new()
    {
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["order_id"] = new() { Type = "string", Description = "订单ID" }
        },
        Required = new[] { "order_id" }
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = ToolArgumentParser.Parse(argumentsJson);
        var orderId = ToolArgumentParser.GetString(args, "order_id") ?? "";
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            order_id = orderId,
            status = "shipped",
            created_at = "2026-06-10",
            estimated_delivery = "2026-06-15"
        }));
    }
}

public class GetProductInfoTool : ITool
{
    public string Name => "get_product_info";
    public string Description => "根据产品名称查询产品详细信息";
    public JsonSchema Parameters => new()
    {
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["product_name"] = new() { Type = "string", Description = "产品名称" }
        },
        Required = new[] { "product_name" }
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = ToolArgumentParser.Parse(argumentsJson);
        var productName = ToolArgumentParser.GetString(args, "product_name") ?? "";
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            product_name = productName,
            category = "电子产品",
            price = "¥1,299",
            in_stock = true,
            warranty = "1年质保"
        }));
    }
}

public class CollectFeedbackTool : ITool
{
    public string Name => "collect_feedback";
    public string Description => "收集用户对服务的反馈评价";
    public JsonSchema Parameters => new()
    {
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["rating"] = new() { Type = "integer", Description = "评分 1-5", Minimum = 1, Maximum = 5 },
            ["comment"] = new() { Type = "string", Description = "评价内容" }
        },
        Required = new[] { "rating" }
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = ToolArgumentParser.Parse(argumentsJson);
        var rating = ToolArgumentParser.GetInt(args, "rating", 0);
        var comment = ToolArgumentParser.GetString(args, "comment") ?? "";
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            received = true,
            rating,
            message = "感谢您的反馈！"
        }));
    }
}
