using Microsoft.AspNetCore.Mvc;
using CliproxyApi.Models;
using CliproxyApi.Services;

namespace CliproxyApi.Controllers;

[ApiController]
[Route("api/v1")]
public class ChatProxyController : ControllerBase
{
    private readonly RagPipelineService _ragPipeline;
    private readonly LLMService _llmService;
    private readonly FunctionCallingService _functionCalling;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<ChatProxyController> _logger;

    public ChatProxyController(RagPipelineService ragPipeline, LLMService llmService,
        FunctionCallingService functionCalling, ToolRegistry toolRegistry,
        ILogger<ChatProxyController> logger)
    {
        _ragPipeline = ragPipeline;
        _llmService = llmService;
        _functionCalling = functionCalling;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request)
    {
        try
        {
            if (request.UseRag)
            {
                var (reply, sources, citations, contextTexts) = await _ragPipeline.ExecuteAsync(request.Message, request.History, request.FeatureOverrides);
                PrometheusMetrics.ChatRequestsTotal.WithLabels("ok").Inc();
                return new ChatResponse
                {
                    Reply = reply,
                    Sources = sources,
                    Citations = citations,
                    ContextTexts = contextTexts
                };
            }

            var directReply = await _llmService.ChatDirect(request.Message);
            PrometheusMetrics.ChatRequestsTotal.WithLabels("ok").Inc();
            return new ChatResponse { Reply = directReply };
        }
        catch (HttpRequestException ex)
        {
            PrometheusMetrics.ChatRequestsTotal.WithLabels("error").Inc();
            _logger.LogError(ex, "LLM service unavailable");
            return StatusCode(503, new ErrorResponse
            {
                Code = "LLM_UNAVAILABLE",
                Message = "大模型服务暂不可用，请稍后重试。",
                CorrelationId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            PrometheusMetrics.ChatRequestsTotal.WithLabels("error").Inc();
            _logger.LogError(ex, "Unexpected error in chat endpoint");
            return StatusCode(500, new ErrorResponse
            {
                Code = "INTERNAL_ERROR",
                Message = "服务内部错误，请联系管理员。",
                CorrelationId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("chat/stream")]
    public async Task ChatStream([FromBody] ChatRequest request)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Correlation-Id"] = HttpContext.TraceIdentifier;

        try
        {
            // Case 1: RAG + EnableTools → use FunctionCallingService (lightweight mode, no RAG preload)
            if (request.UseRag && request.FeatureOverrides?.EnableTools == true)
            {
                _logger.LogInformation("Streaming: RAG + Function Calling mode");
                var toolMessages = BuildToolModeMessages(request.Message, request.History);
                var (reply, _, _, _) = await _functionCalling.RunAsync(toolMessages);
                await SendStreamingTokens(reply);
                return;
            }

            // Case 2: RAG without tools → existing RAG streaming path (backward compatible)
            if (request.UseRag)
            {
                _logger.LogInformation("Streaming: RAG mode");
                await Response.WriteAsync("data: [STATUS] 正在检索知识库...\n\n");
                await Response.Body.FlushAsync();
                var ragMessages = await _ragPipeline.BuildMessagesAsync(request.Message, request.History, request.FeatureOverrides);
                await Response.WriteAsync("data: [STATUS] 正在生成回答...\n\n");
                await Response.Body.FlushAsync();
                await foreach (var token in _llmService.StreamChatAsync(ragMessages))
                {
                    await SendStreamingToken(token);
                }
                return;
            }

            // Case 3: Non-RAG + EnableTools → Function Calling directly
            if (request.FeatureOverrides?.EnableTools == true)
            {
                _logger.LogInformation("Streaming: Direct Function Calling mode");
                var fcMessages = BuildToolModeMessages(request.Message, request.History);
                var (reply, _, _, _) = await _functionCalling.RunAsync(fcMessages);
                await SendStreamingTokens(reply);
                return;
            }

            // Case 4: Simple direct chat
            _logger.LogInformation("Streaming: Direct chat mode");
            var chatMessages = new List<LLMChatMessage>
            {
                new() { Role = "user", Content = request.Message }
            };
            await foreach (var token in _llmService.StreamChatAsync(chatMessages))
            {
                await SendStreamingToken(token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream error");
            await WriteErrorSafe($"[ERROR] 服务内部错误 (CID: {HttpContext.TraceIdentifier})");
        }
    }

    private static List<LLMChatMessage> BuildToolModeMessages(
        string query, List<(string Role, string Content)>? history)
    {
        var messages = new List<LLMChatMessage>();

        // Build proper conversation history as message entries
        if (history != null && history.Count > 0)
        {
            messages.AddRange(history.Select(h => new LLMChatMessage
            {
                Role = h.Role.ToLowerInvariant() switch { "user" or "human" => "user", "assistant" or "ai" => "assistant", _ => "user" },
                Content = h.Content
            }));
        }

        var systemPrompt = "你是智能客服助手。如需查询知识库，使用 search_knowledge_base 工具。";
        messages.Insert(0, new() { Role = "system", Content = systemPrompt });
        messages.Add(new() { Role = "user", Content = query });

        return messages;
    }

    /// <summary>
    /// Send a single token as SSE data line. Checks if response has started.
    /// </summary>
    private async Task SendStreamingToken(string token)
    {
        if (Response.HasStarted) return;
        await Response.WriteAsync($"data: {token}\n\n");
        await Response.Body.FlushAsync();
    }

    /// <summary>
    /// Send a complete reply as SSE data lines, chunked for streaming effect.
    /// Chunks are sized to ~100 chars or at word boundaries for reasonable latency.
    /// </summary>
    private async Task SendStreamingTokens(string reply)
    {
        if (string.IsNullOrEmpty(reply)) return;

        const int chunkSize = 100;
        int pos = 0;
        while (pos < reply.Length)
        {
            if (Response.HasStarted) return;
            var end = Math.Min(pos + chunkSize, reply.Length);
            // Try to break at word boundary
            if (end < reply.Length)
            {
                var searchBack = reply.Substring(end - 20, 20);
                var lastSpace = searchBack.LastIndexOf(' ');
                if (lastSpace >= 0)
                    end = pos + lastSpace;
            }
            await Response.WriteAsync($"data: {reply.Substring(pos, end - pos)}\n\n");
            await Response.Body.FlushAsync();
            pos = end;
        }
    }

    /// <summary>
    /// Safely write an error message to the SSE stream.
    /// Checks Response.HasStarted before writing to avoid protocol errors.
    /// Wraps in try-catch to prevent exceptions after stream has started.
    /// </summary>
    private async Task WriteErrorSafe(string message)
    {
        try
        {
            if (!Response.HasStarted)
            {
                await Response.WriteAsync($"data: {message}\n\n");
                await Response.Body.FlushAsync();
            }
        }
        catch (Exception writeEx)
        {
            _logger.LogError(writeEx, "Failed to write SSE error message");
        }
    }
}
