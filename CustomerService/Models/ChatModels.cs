namespace CustomerService.Models;

public class CustomerChatRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class CustomerChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public List<string> Sources { get; set; } = new();
    public List<string> Citations { get; set; } = new();
}

public class ProxyChatRequest
{
    public string Message { get; set; } = string.Empty;
    public bool UseRag { get; set; } = true;
    public List<(string Role, string Content)> History { get; set; } = new();
    public RagFeatureFlags? FeatureOverrides { get; set; }
}

public class ProxyChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public List<string> Sources { get; set; } = new();
    public List<string> Citations { get; set; } = new();
    public List<string> ContextTexts { get; set; } = new();
}

public class RagFeatureFlags
{
    public bool EnableQueryRewriting { get; set; } = true;
    public bool EnableHyDE { get; set; } = true;
    public bool EnableHybridSearch { get; set; } = true;
    public bool EnableReranking { get; set; } = true;
    public bool EnableContextCompression { get; set; } = false;
    public bool EnableSelfConsistency { get; set; } = false;
    public bool EnableTools { get; set; } = false;
}

public class EvalRequest
{
    public string Query { get; set; } = string.Empty;
    public string? UserId { get; set; }
}

public class EvalResult
{
    public double Faithfulness { get; set; }
    public double Relevance { get; set; }
    public double RetrievalPrecision { get; set; }
    public double OverallScore { get; set; }
    public string Details { get; set; } = string.Empty;
}

public class CompareRequest
{
    public string Query { get; set; } = string.Empty;
    public RagFeatureFlags? ConfigA { get; set; }
    public RagFeatureFlags? ConfigB { get; set; }
}

public class CompareResult
{
    public string Query { get; set; } = string.Empty;
    public CompareSideResult SideA { get; set; } = new();
    public CompareSideResult SideB { get; set; } = new();
    public string Winner { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
}

public class CompareSideResult
{
    public string Label { get; set; } = string.Empty;
    public RagFeatureFlags Config { get; set; } = new();
    public string Answer { get; set; } = string.Empty;
    public List<string> Sources { get; set; } = new();
    public EvalResult Scores { get; set; } = new();
}
