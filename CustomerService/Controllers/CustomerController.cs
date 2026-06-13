using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using CustomerService.Models;
using CustomerService.Services;
using Prometheus;

namespace CustomerService.Controllers;

[ApiController]
[Route("api/customer")]
public class CustomerController : ControllerBase
{
    private readonly CustomerAIService _aiService;
    private readonly EvalService _evalService;
    private readonly ILogger<CustomerController> _logger;

    public CustomerController(CustomerAIService aiService, EvalService evalService,
        ILogger<CustomerController> logger)
    {
        _aiService = aiService;
        _evalService = evalService;
        _logger = logger;
    }

    [HttpPost("chat")]
    [EnableRateLimiting("chat")]
    public async Task<ActionResult<CustomerChatResponse>> Chat([FromBody] CustomerChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            PrometheusMetrics.ChatRequestsTotal.WithLabels("bad_request").Inc();
            return BadRequest(new CustomerChatResponse { Reply = "消息不能为空。" });
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            PrometheusMetrics.ChatRequestsTotal.WithLabels("bad_request").Inc();
            return BadRequest(new CustomerChatResponse { Reply = "用户ID不能为空。" });
        }

        using var timer = PrometheusMetrics.ChatRequestDuration.NewTimer();

        try
        {
            var response = await _aiService.ChatAsync(request.UserId, request.Message);
            PrometheusMetrics.ChatRequestsTotal.WithLabels("ok").Inc();
            return response;
        }
        catch (HttpRequestException ex)
        {
            PrometheusMetrics.ChatRequestsTotal.WithLabels("error").Inc();
            _logger.LogError(ex, "Failed to reach CliproxyApi");
            return StatusCode(503, new CustomerChatResponse { Reply = "智能客服服务暂不可用，请稍后重试。" });
        }
        catch (Exception ex)
        {
            PrometheusMetrics.ChatRequestsTotal.WithLabels("error").Inc();
            _logger.LogError(ex, "Unexpected error in chat endpoint");
            return StatusCode(500, new CustomerChatResponse { Reply = "服务内部错误，请稍后重试。" });
        }
    }

    [HttpGet("history/{userId}")]
    public async Task<ActionResult<List<object>>> GetHistory(string userId)
    {
        try
        {
            var history = await _aiService.GetHistoryAsync(userId);
            return history;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get history for user {UserId}", userId);
            return StatusCode(500, new List<object>());
        }
    }

    [HttpPost("chat/stream")]
    [EnableRateLimiting("chat")]
    public async Task ChatStream([FromBody] CustomerChatRequest request)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await Response.WriteAsync("data: [ERROR] 消息不能为空。\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            await Response.WriteAsync("data: [ERROR] 用户ID不能为空。\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        try
        {
            using var timer = PrometheusMetrics.ChatRequestDuration.NewTimer();
            await foreach (var token in _aiService.ChatStreamAsync(request.UserId, request.Message))
            {
                await Response.WriteAsync($"data: {token}\n\n");
                await Response.Body.FlushAsync();
            }
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
            PrometheusMetrics.ChatRequestsTotal.WithLabels("ok").Inc();
        }
        catch (HttpRequestException ex)
        {
            PrometheusMetrics.ChatRequestsTotal.WithLabels("error").Inc();
            _logger.LogError(ex, "Stream failed - CliproxyApi unavailable");
            await Response.WriteAsync("data: [ERROR] 智能客服服务暂不可用，请稍后重试。\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            PrometheusMetrics.ChatRequestsTotal.WithLabels("error").Inc();
            _logger.LogError(ex, "Stream error");
            await Response.WriteAsync("data: [ERROR] 服务内部错误，请稍后重试。\n\n");
            await Response.Body.FlushAsync();
        }
    }

    [HttpPost("eval")]
    public async Task<ActionResult<EvalResult>> Evaluate([FromBody] EvalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new EvalResult { Details = "Query不能为空。" });

        try
        {
            var result = await _evalService.AutoEvaluateAsync(request.Query, request.UserId);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Eval failed - CliproxyApi unavailable");
            return StatusCode(503, new EvalResult { Details = "评估服务暂不可用。" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eval error");
            return StatusCode(500, new EvalResult { Details = "评估过程出错。" });
        }
    }

    [HttpPost("eval/compare")]
    public async Task<ActionResult<CompareResult>> Compare([FromBody] CompareRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new CompareResult { Analysis = "Query不能为空。" });

        try
        {
            var result = await _evalService.CompareAsync(request.Query, request.ConfigA, request.ConfigB);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Compare failed - CliproxyApi unavailable");
            return StatusCode(503, new CompareResult { Analysis = "对比服务暂不可用。" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compare error");
            return StatusCode(500, new CompareResult { Analysis = "对比过程出错。" });
        }
    }
}
