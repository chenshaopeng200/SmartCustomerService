using System.Text.Json;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(IEnumerable<ITool> tools, ILogger<ToolRegistry> logger)
    {
        _logger = logger;
        foreach (var tool in tools)
        {
            _tools[tool.Name] = tool;
            _logger.LogInformation("Registered tool: {ToolName}", tool.Name);
        }
    }

    public List<ToolDefinition> GetDefinitions()
    {
        return _tools.Values.Select(t => new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Parameters
            }
        }).ToList();
    }

    public async Task<string> ExecuteAsync(string name, string argumentsJson)
    {
        if (!_tools.TryGetValue(name, out var tool))
            return JsonSerializer.Serialize(new { error = $"Unknown tool: {name}" });

        try
        {
            return await tool.ExecuteAsync(argumentsJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool '{ToolName}' execution failed", name);
            return JsonSerializer.Serialize(new { error = $"Tool '{name}' failed: {ex.Message}" });
        }
    }

    public bool Has(string name) => _tools.ContainsKey(name);
}
