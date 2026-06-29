using System.Text.Json;
using CliproxyApi.Models;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// --- Service Registration ---
builder.Services.AddControllers(options =>
{
    // Enable built-in model-state validation so invalid requests return 400
    // instead of reaching downstream services.
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = false;
})
.AddJsonOptions(options =>
{
    // Consistent camelCase across all JSON responses
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// RAG pipeline configuration bound from appsettings
builder.Services.Configure<RagPipelineConfig>(
    builder.Configuration.GetSection("RAG"));

// Application-level services (singleton — created once per process)
builder.Services.AddSingleton<CliproxyApi.Services.LLMService>();
builder.Services.AddSingleton<CliproxyApi.Services.QdrantService>();
builder.Services.AddSingleton<CliproxyApi.Services.QueryRewriterService>();
builder.Services.AddSingleton<CliproxyApi.Services.HybridSearchService>();
builder.Services.AddSingleton<CliproxyApi.Services.RerankerService>();
builder.Services.AddSingleton<CliproxyApi.Services.ContextCompressorService>();
builder.Services.AddSingleton<CliproxyApi.Services.SelfConsistencyService>();
builder.Services.AddSingleton<CliproxyApi.Services.ToolRegistry>();

// Register concrete tool implementations as ITool
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.SearchKnowledgeBaseTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.CreateSupportTicketTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.EscalateToHumanTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.GetOrderStatusTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.GetProductInfoTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.CollectFeedbackTool>();

builder.Services.AddSingleton<CliproxyApi.Services.FunctionCallingService>();
builder.Services.AddSingleton<CliproxyApi.Services.RagPipelineService>();

var app = builder.Build();

// --- Middleware Pipeline (order matters) ---

// 1. HTTP metrics collection (must be first to capture all requests)
app.UseHttpMetrics();

// 2. Correlation ID — injects/tracks request ID for distributed tracing
app.UseMiddleware<CliproxyApi.Middleware.CorrelationIdMiddleware>();

// 3. Swagger UI / API docs (development only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 4. Route & execute controllers (includes model validation via [Required])
app.MapControllers();

// 5. Prometheus metrics endpoint
app.MapMetrics();

// --- Graceful shutdown: dispose LLMService (owns HttpClient instances) ---
var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
appLifetime.ApplicationStopping.Register(() =>
{
    var llm = app.Services.GetService<CliproxyApi.Services.LLMService>();
    if (llm is IAsyncDisposable asyncDisposable)
    {
        _ = asyncDisposable.DisposeAsync();
    }
});

await app.RunAsync();
