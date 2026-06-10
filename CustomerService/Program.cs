using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<CustomerService.Services.SessionService>();
builder.Services.AddSingleton<CustomerService.Services.CacheService>();
builder.Services.AddSingleton<CustomerService.Services.CustomerAIService>();
builder.Services.AddSingleton<CustomerService.Services.EvalService>();

var app = builder.Build();

app.UseHttpMetrics();
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
