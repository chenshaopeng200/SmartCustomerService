using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<CustomerService.Services.SessionService>();
builder.Services.AddSingleton(new CustomerService.Services.CacheService(builder.Configuration));
builder.Services.AddSingleton<CustomerService.Services.CustomerAIService>();
builder.Services.AddSingleton<CustomerService.Services.EvalService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("chat", config =>
    {
        config.PermitLimit = 10;
        config.Window = TimeSpan.FromSeconds(1);
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 5;
    });
});

var app = builder.Build();

app.UseHttpMetrics();
app.UseRateLimiter();
app.UseMiddleware<CustomerService.Middleware.CorrelationIdMiddleware>();
app.UseMiddleware<CustomerService.Middleware.JwtMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapMetrics();

app.Run();
