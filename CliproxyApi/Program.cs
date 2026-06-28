using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<CliproxyApi.Services.LLMService>();  // Registered after AddHttpClient()
builder.Services.AddSingleton<CliproxyApi.Services.QdrantService>();
builder.Services.AddSingleton<CliproxyApi.Services.QueryRewriterService>();
builder.Services.AddSingleton<CliproxyApi.Services.HybridSearchService>();
builder.Services.AddSingleton<CliproxyApi.Services.RerankerService>();
builder.Services.AddSingleton<CliproxyApi.Services.ContextCompressorService>();
builder.Services.AddSingleton<CliproxyApi.Services.SelfConsistencyService>();
builder.Services.AddSingleton<CliproxyApi.Services.ToolRegistry>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.SearchKnowledgeBaseTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.CreateSupportTicketTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.EscalateToHumanTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.GetOrderStatusTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.GetProductInfoTool>();
builder.Services.AddSingleton<CliproxyApi.Services.ITool, CliproxyApi.Services.CollectFeedbackTool>();
builder.Services.AddSingleton<CliproxyApi.Services.FunctionCallingService>();
builder.Services.AddSingleton<CliproxyApi.Services.RagPipelineService>();

var app = builder.Build();

app.UseHttpMetrics();
app.UseMiddleware<CliproxyApi.Middleware.CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapMetrics();

// Ensure LLMService (which owns HttpClient instances) is disposed on shutdown
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
