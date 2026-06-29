namespace CliproxyApi.Models;

/// <summary>
/// Standardized error response used across all controllers.
/// Includes machine-readable code, human-readable message, and correlation ID for tracing.
/// </summary>
public class ErrorResponse
{
    /// <summary>Machine-readable error code (e.g. "VALIDATION_ERROR").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable error message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Correlation / trace ID for log lookups.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Optional details (e.g. per-field validation errors).</summary>
    public object? Details { get; set; }
}

/// <summary>
/// Typed error codes for common failure scenarios.
/// Consumers can switch on these constants instead of parsing strings.
/// </summary>
public static class ErrorCodes
{
    // --- Client / Validation errors (4xx) ---
    public const string VALIDATION_ERROR = "VALIDATION_ERROR";
    public const string MISSING_BODY = "MISSING_REQUEST_BODY";
    public const string EMPTY_MESSAGE = "EMPTY_MESSAGE";

    // --- Upstream / LLM errors (5xx) ---
    public const string LLM_UNAVAILABLE = "LLM_UNAVAILABLE";
    public const string EMBEDDING_UNAVAILABLE = "EMBEDDING_UNAVAILABLE";
    public const string RERANKER_UNAVAILABLE = "RERANKER_UNAVAILABLE";
    public const string TOOL_TIMEOUT = "TOOL_TIMEOUT";
    public const string TOOL_EXECUTION_FAILED = "TOOL_EXECUTION_FAILED";

    // --- RAG pipeline errors ---
    public const string RAG_PIPELINE_EMPTY_RESULT = "RAG_PIPELINE_EMPTY_RESULT";
    public const string RAG_CONTEXT_COMPRESSION_FAILED = "RAG_CONTEXT_COMPRESSION_FAILED";
    public const string RAG_SELF_CONSISTENCY_FAILED = "RAG_SELF_CONSISTENCY_FAILED";

    // --- Vector store errors ---
    public const string QDRANT_UNAVAILABLE = "QDRANT_UNAVAILABLE";
    public const string QDRANT_SEARCH_FAILED = "QDRANT_SEARCH_FAILED";

    // --- Rate limiting ---
    public const string RATE_LIMITED = "RATE_LIMITED";

    // --- Internal errors ---
    public const string INTERNAL_ERROR = "INTERNAL_ERROR";
    public const string UNKNOWN_ERROR = "UNKNOWN_ERROR";

    // --- Helper: build a standardized error response ---
    public static ErrorResponse Create(
        string code,
        string message,
        HttpContext? httpContext = null,
        object? details = null)
    {
        return new ErrorResponse
        {
            Code = code,
            Message = message,
            CorrelationId = httpContext?.TraceIdentifier ?? string.Empty,
            Details = details
        };
    }

    // Pre-built convenience factories
    public static ValidationErrorResponse CreateValidationError(
        IEnumerable<KeyValuePair<string, string?>> fieldErrors,
        HttpContext? httpContext = null)
    {
        return new ValidationErrorResponse
        {
            Code = VALIDATION_ERROR,
            Message = "请求参数校验失败",
            CorrelationId = httpContext?.TraceIdentifier ?? string.Empty,
            Errors = fieldErrors.ToList()
        };
    }
}

/// <summary>
/// Extended error response for validation failures — carries per-field details.
/// </summary>
public class ValidationErrorResponse : ErrorResponse
{
    /// <summary>List of {field, message} pairs describing what went wrong.</summary>
    public List<KeyValuePair<string, string?>> Errors { get; set; } = new();
}
