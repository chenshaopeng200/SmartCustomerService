using System.Text.Json;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class FunctionCallingService
{
    private readonly LLMService _llmService;
    private readonly ToolRegistry _toolRegistry;
    private readonly int _maxIterations;
    private readonly int _toolTimeoutSeconds;
    private readonly bool _enabled;
    private readonly ILogger<FunctionCallingService> _logger;

    public FunctionCallingService(
        LLMService llmService,
        ToolRegistry toolRegistry,
        IConfiguration config,
        ILogger<FunctionCallingService> logger)
    {
        _llmService = llmService;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _enabled = bool.Parse(config["FunctionCalling:Enabled"] ?? "true");
        _maxIterations = int.Parse(config["FunctionCalling:MaxIterations"] ?? "5");
        _toolTimeoutSeconds = int.Parse(config["FunctionCalling:ToolTimeoutSeconds"] ?? "10");
    }

    public async Task<(string Reply, List<string> Sources, List<string> Citations, List<string> ContextTexts)> RunAsync(
        List<LLMChatMessage> messages,
        List<QdrantSearchResult>? initialDocs = null)
    {
        if (!_enabled)
        {
            _logger.LogInformation("FunctionCalling disabled, falling back to direct chat");
            var fallback = await _llmService.ChatWithMessages(messages);
            return (fallback, new(), new(), new());
        }

        var tools = _toolRegistry.GetDefinitions();
        if (tools.Count == 0)
        {
            _logger.LogWarning("No tools registered, falling back to direct chat");
            var fallback = await _llmService.ChatWithMessages(messages);
            return (fallback, new(), new(), new());
        }

        for (int i = 0; i < _maxIterations; i++)
        {
            _logger.LogInformation("FunctionCalling iteration {Iteration}/{Max}", i + 1, _maxIterations);

            var response = await _llmService.ChatWithTools(messages, tools);

            // Guard: if Choices is null or empty, return a fallback answer
            if (response.Choices == null || response.Choices.Count == 0)
            {
                _logger.LogWarning("Empty response choices on iteration {Iteration}", i + 1);
                return ("Unable to process the request — please try again.", new(), new(), new());
            }

            var choice = response.Choices[0];
            var msg = choice.Message;

            // LLM returned content without tool calls — final answer
            if (msg.ToolCalls == null || msg.ToolCalls.Count == 0)
            {
                var reply = msg.Content ?? string.Empty;
                return (reply, new(), new(), new());
            }

            // LLM requested tool calls
            _logger.LogInformation("LLM requested {Count} tool call(s)", msg.ToolCalls.Count);

            messages.Add(new LLMChatMessage
            {
                Role = "assistant",
                Content = msg.Content,
                ToolCalls = msg.ToolCalls
            });

            foreach (var toolCall in msg.ToolCalls)
            {
                var toolName = toolCall.Function.Name;
                var toolArgs = toolCall.Function.Arguments ?? "{}";
                _logger.LogInformation("Executing tool: {Tool} with args: {Args}", toolName, toolArgs);

                string toolResult;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_toolTimeoutSeconds));

                try
                {
                    var task = _toolRegistry.ExecuteAsync(toolName, toolArgs);
                    var completed = await Task.WhenAny(task, Task.Delay(int.MaxValue, cts.Token));
                    if (completed == task)
                        toolResult = await task;
                    else
                        toolResult = JsonSerializer.Serialize(new { error = $"Tool execution timed out after {_toolTimeoutSeconds}s" });
                }
                catch (OperationCanceledException)
                {
                    toolResult = JsonSerializer.Serialize(new { error = $"Tool execution timed out after {_toolTimeoutSeconds}s" });
                }
                catch (Exception ex)
                {
                    toolResult = JsonSerializer.Serialize(new { error = $"Tool execution failed: {ex.Message}" });
                }

                messages.Add(new LLMChatMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = toolResult
                });
            }
        }

        _logger.LogWarning("FunctionCalling max iterations ({Max}) reached", _maxIterations);
        return ("The request could not be completed within the allowed number of tool calls. Please try again with a simpler request.", new(), new(), new());
    }
}
