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
    private readonly ILogger<ChatProxyController> _logger;

    public ChatProxyController(RagPipelineService ragPipeline, LLMService llmService,
        ILogger<ChatProxyController> logger)
    {
        _ragPipeline = ragPipeline;
        _llmService = llmService;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request)
    {
        try
        {
            if (request.UseRag)
            {
                var (reply, sources, citations) = await _ragPipeline.ExecuteAsync(request.Message, request.History, request.FeatureOverrides);
                PrometheusMetrics.ChatRequestsTotal.WithLabels("ok").Inc();
                return new ChatResponse
                {
                    Reply = reply,
                    Sources = sources,
                    Citations = citations
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
            if (request.UseRag)
            {
                var messages = await _ragPipeline.BuildMessagesAsync(request.Message, request.History, request.FeatureOverrides);
                await foreach (var token in _llmService.StreamChatAsync(messages))
                {
                    await Response.WriteAsync($"data: {token}\n\n");
                    await Response.Body.FlushAsync();
                }
            }
            else
            {
                var messages = new List<LLMChatMessage>
                {
                    new() { Role = "user", Content = request.Message }
                };
                await foreach (var token in _llmService.StreamChatAsync(messages))
                {
                    await Response.WriteAsync($"data: {token}\n\n");
                    await Response.Body.FlushAsync();
                }
            }

            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream error");
            await Response.WriteAsync($"data: [ERROR] 服务内部错误 (CID: {HttpContext.TraceIdentifier})\n\n");
            await Response.Body.FlushAsync();
        }
    }
}
