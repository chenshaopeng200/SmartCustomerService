using System.Text.Json;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class SearchKnowledgeBaseTool : ITool
{
    private readonly QdrantService _qdrantService;

    public string Name => "search_knowledge_base";
    public string Description => "搜索知识库，查找与查询相关的文档资料";
    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "搜索查询关键词" },
            top_k = new { type = "integer", description = "返回结果数量，默认3", @default = 3 }
        },
        required = new[] { "query" }
    };

    public SearchKnowledgeBaseTool(QdrantService qdrantService)
    {
        _qdrantService = qdrantService;
    }

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        var query = args?.GetValueOrDefault("query").GetString() ?? "";
        var topK = args?.GetValueOrDefault("top_k").GetInt32() ?? 3;

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
    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "工单标题" },
            description = new { type = "string", description = "工单描述" },
            priority = new { type = "string", @enum = new[] { "low", "medium", "high" }, description = "优先级" }
        },
        required = new[] { "title", "description" }
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        var title = args?.GetValueOrDefault("title").GetString() ?? "";
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
    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            reason = new { type = "string", description = "转接原因" },
            summary = new { type = "string", description = "问题摘要" }
        },
        required = new[] { "reason", "summary" }
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
    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            order_id = new { type = "string", description = "订单ID" }
        },
        required = new[] { "order_id" }
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        var orderId = args?.GetValueOrDefault("order_id").GetString() ?? "";
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
    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            product_name = new { type = "string", description = "产品名称" }
        },
        required = new[] { "product_name" }
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        var productName = args?.GetValueOrDefault("product_name").GetString() ?? "";
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
    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            rating = new { type = "integer", description = "评分 1-5", minimum = 1, maximum = 5 },
            comment = new { type = "string", description = "评价内容" }
        },
        required = new[] { "rating" }
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        var rating = args?.GetValueOrDefault("rating").GetInt32() ?? 0;
        var comment = args?.GetValueOrDefault("comment").GetString() ?? "";
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            received = true,
            rating,
            message = "感谢您的反馈！"
        }));
    }
}
