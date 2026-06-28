using System.ComponentModel.DataAnnotations;

namespace CliproxyApi.Models;

public class ChatRequest
{
    [Required(ErrorMessage = "消息不能为空")]
    [MinLength(1, ErrorMessage = "消息不能为空")]
    public string Message { get; set; } = string.Empty;
    public bool UseRag { get; set; } = true;
    public List<(string Role, string Content)> History { get; set; } = new();
    public RagFeatureOverrides? FeatureOverrides { get; set; }
}

public class RagFeatureOverrides
{
    public bool? EnableQueryRewriting { get; set; }
    public bool? EnableHyDE { get; set; }
    public bool? EnableHybridSearch { get; set; }
    public bool? EnableReranking { get; set; }
    public bool? EnableContextCompression { get; set; }
    public bool? EnableSelfConsistency { get; set; }
    public bool? EnableTools { get; set; }
}

public class ChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public List<string> Sources { get; set; } = new();
    public List<string> Citations { get; set; } = new();
    public List<string> ContextTexts { get; set; } = new();
}

public class LLMChatMessage
{
    public string Role { get; set; } = "user";
    public string? Content { get; set; }
    public string? ToolCallId { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
}

public class LLMChatRequest
{
    public string Model { get; set; } = string.Empty;
    public List<LLMChatMessage> Messages { get; set; } = new();
    public List<ToolDefinition>? Tools { get; set; }
    public string? ToolChoice { get; set; }
}

public class LLMChatResponse
{
    public List<LLMChoice> Choices { get; set; } = new();
}

public class LLMChoice
{
    public LLMChatMessage Message { get; set; } = new();
}

public class EmbeddingRequest
{
    public string Model { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string InputType { get; set; } = "passage";
}

public class EmbeddingResponse
{
    public List<EmbeddingData> Data { get; set; } = new();
}

public class EmbeddingData
{
    public List<float> Embedding { get; set; } = new();
}

public class QdrantSearchResult
{
    public string Text { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class JsonSchemaProperty
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "string";
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("enum")]
    public string[]? Enum { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("minimum")]
    public int? Minimum { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("maximum")]
    public int? Maximum { get; set; }
}

public class JsonSchema
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "object";
    [System.Text.Json.Serialization.JsonPropertyName("properties")]
    public Dictionary<string, JsonSchemaProperty> Properties { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("required")]
    public string[]? Required { get; set; }
}

// Function Calling models
public class ToolDefinition
{
    public string Type { get; set; } = "function";
    public FunctionDefinition Function { get; set; } = new();
}

public class FunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonSchema Parameters { get; set; } = new();
}

public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "function";
    public FunctionCall Function { get; set; } = new();
}

public class FunctionCall
{
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
}
