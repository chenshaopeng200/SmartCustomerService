namespace CliproxyApi.Models;

public class ChatRequest
{
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
    public string Content { get; set; } = string.Empty;
}

public class LLMChatRequest
{
    public string Model { get; set; } = string.Empty;
    public List<LLMChatMessage> Messages { get; set; } = new();
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
