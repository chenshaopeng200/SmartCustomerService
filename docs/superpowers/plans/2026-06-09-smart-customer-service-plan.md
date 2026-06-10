# Smart Customer Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a RAG-based intelligent customer service system that answers user questions based on private knowledge documents.

**Architecture:** Three-layer .NET 8 architecture — RAGIndexer (console, document indexing), CliproxyApi (WebAPI, RAG retrieval + LLM proxy via Go CliproxyApi), CustomerService (WebAPI, business logic + session management). Qdrant for vector search.

**Tech Stack:** .NET 8, C#, ASP.NET Core Web API, Qdrant, UglyToad.PdfPig, Go CliproxyApi (port 8317) as LLM backend

---

### Task 1: Install .NET 8 SDK

**Files:** None

- [ ] **Step 1: Install .NET 8 SDK**

```bash
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh && chmod +x /tmp/dotnet-install.sh && /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet && sudo ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
```

- [ ] **Step 2: Verify installation**

```bash
dotnet --version
```
Expected: `8.0.x`

---

### Task 2: Initialize solution and docker-compose

**Files:**
- Create: `SmartCustomerService.sln`
- Create: `docker-compose.yml`

- [ ] **Step 1: Create solution file**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService && dotnet new sln -n SmartCustomerService
```

- [ ] **Step 2: Write docker-compose.yml**

File: `docker-compose.yml`
```yaml
version: "3.8"
services:
  qdrant:
    image: qdrant/qdrant:latest
    ports:
      - "6333:6333"
      - "6334:6334"
    volumes:
      - qdrant_data:/qdrant/storage

volumes:
  qdrant_data:
```

- [ ] **Step 3: Start Qdrant**

```bash
docker-compose up -d
```

- [ ] **Step 4: Verify Qdrant is running**

```bash
curl -s http://localhost:6333/healthz
```
Expected: HTTP 200 or "ok"

---

### Task 3: Create RAGIndexer project

**Files:**
- Create: `RAGIndexer/RAGIndexer.csproj`
- Create: `RAGIndexer/appsettings.json`

- [ ] **Step 1: Create console project and add to solution**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService && dotnet new console -n RAGIndexer && dotnet sln add RAGIndexer/RAGIndexer.csproj
```

- [ ] **Step 2: Add NuGet packages**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService/RAGIndexer && dotnet add package UglyToad.PdfPig && dotnet add package Qdrant.Client && dotnet add package Microsoft.Extensions.Configuration.Json && dotnet add package Microsoft.Extensions.Configuration
```

- [ ] **Step 3: Write appsettings.json**

File: `RAGIndexer/appsettings.json`
```json
{
  "CliproxyApi": {
    "BaseUrl": "http://localhost:8317",
    "ApiKey": "sk-bcLdWhCrXTbjVComFDFUZWamG1krwKmH3Jcg70uejN4CC",
    "EmbeddingModel": "text-embedding-3-small"
  },
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334,
    "CollectionName": "knowledge_chunks",
    "VectorSize": 1536
  },
  "Chunking": {
    "MaxChunkSize": 800,
    "Overlap": 100
  }
}
```

- [ ] **Step 4: Update csproj to copy appsettings to output**

Edit `RAGIndexer/RAGIndexer.csproj` — add after `</PackageReference>` items:
```xml
  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

---

### Task 4: Write RAGIndexer Program.cs

**Files:**
- Modify: `RAGIndexer/Program.cs`

- [ ] **Step 1: Write Program.cs with PDF extraction, chunking, embedding, and Qdrant storage**

File: `RAGIndexer/Program.cs`
```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using UglyToad.PdfPig;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var cliproxyBaseUrl = config["CliproxyApi:BaseUrl"]!;
var cliproxyApiKey = config["CliproxyApi:ApiKey"]!;
var embeddingModel = config["CliproxyApi:EmbeddingModel"]!;
var qdrantHost = config["Qdrant:Host"]!;
var qdrantPort = int.Parse(config["Qdrant:Port"]!);
var collectionName = config["Qdrant:CollectionName"]!;
var vectorSize = int.Parse(config["Qdrant:VectorSize"]!);
var maxChunkSize = int.Parse(config["Chunking:MaxChunkSize"]!);
var overlap = int.Parse(config["Chunking:Overlap"]!);

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run <file-path> [<file-path>...]");
    return;
}

using var httpClient = new HttpClient { BaseAddress = new Uri(cliproxyBaseUrl) };
httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {cliproxyApiKey}");

using var qdrantClient = new QdrantClient(qdrantHost, qdrantPort);

var collections = await qdrantClient.ListCollectionsAsync();
if (!collections.Contains(collectionName))
{
    await qdrantClient.CreateCollectionAsync(collectionName,
        new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine });
    Console.WriteLine($"Created collection: {collectionName}");
}

ulong pointIndex = 0;

foreach (var filePath in args)
{
    if (!File.Exists(filePath))
    {
        Console.WriteLine($"File not found: {filePath}, skipping.");
        continue;
    }

    Console.WriteLine($"Processing: {filePath}");
    string text;

    if (filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        text = ExtractTextFromPdf(filePath);
    else if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        text = File.ReadAllText(filePath, Encoding.UTF8);
    else
    {
        Console.WriteLine($"Unsupported format: {filePath}, skipping.");
        continue;
    }

    var chunks = SplitTextIntoChunks(text, maxChunkSize, overlap);
    Console.WriteLine($"  Chunks: {chunks.Length}");

    foreach (var chunk in chunks)
    {
        var embedding = await GetEmbedding(httpClient, embeddingModel, chunk);
        var point = new PointStruct
        {
            Id = pointIndex++,
            Vector = { embedding },
            Payload =
            {
                ["text"] = chunk,
                ["source"] = filePath,
                ["chunk_index"] = (long)Array.IndexOf(chunks, chunk)
            }
        };

        await qdrantClient.UpsertAsync(collectionName, new[] { point });
    }

    Console.WriteLine($"  Indexed {chunks.Length} chunks from {filePath}");
}

Console.WriteLine("Done.");

static string ExtractTextFromPdf(string path)
{
    var sb = new StringBuilder();
    using var pdf = PdfDocument.Open(path);
    foreach (var page in pdf.GetPages())
    {
        sb.AppendLine(page.Text);
    }
    return sb.ToString();
}

static string[] SplitTextIntoChunks(string text, int maxChunkSize, int overlap)
{
    var chunks = new List<string>();
    int start = 0;
    while (start < text.Length)
    {
        int end = Math.Min(start + maxChunkSize, text.Length);
        if (end < text.Length)
        {
            int lastPeriod = text.LastIndexOf('.', end, end - start);
            if (lastPeriod > start + maxChunkSize / 2)
                end = lastPeriod + 1;
        }
        chunks.Add(text[start..end].Trim());
        start = end - overlap;
        if (start < 0) start = 0;
    }
    return chunks.ToArray();
}

static async Task<float[]> GetEmbedding(HttpClient client, string model, string text)
{
    var requestBody = new
    {
        model,
        input = text
    };
    var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
    var response = await client.PostAsync("/v1/embeddings", content);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    var embedding = json.GetProperty("data")[0].GetProperty("embedding").EnumerateArray()
        .Select(e => e.GetSingle()).ToArray();
    return embedding;
}
```

- [ ] **Step 2: Verify it compiles**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService/RAGIndexer && dotnet build
```
Expected: Build succeeded.

---

### Task 5: Create CliproxyApi project

**Files:**
- Create: `CliproxyApi/CliproxyApi.csproj`
- Create: `CliproxyApi/Program.cs`
- Create: `CliproxyApi/appsettings.json`

- [ ] **Step 1: Create Web API project and add to solution**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService && dotnet new webapi -n CliproxyApi --no-https && dotnet sln add CliproxyApi/CliproxyApi.csproj
```

- [ ] **Step 2: Add Qdrant.Client package**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService/CliproxyApi && dotnet add package Qdrant.Client
```

- [ ] **Step 3: Write appsettings.json**

File: `CliproxyApi/appsettings.json`
```json
{
  "Logging": {
    "LogLevel": { "Default": "Information" }
  },
  "CliproxyApi": {
    "BaseUrl": "http://localhost:8317",
    "ApiKey": "sk-bcLdWhCrXTbjVComFDFUZWamG1krwKmH3Jcg70uejN4CC",
    "ChatModel": "deepseek-v4-pro",
    "EmbeddingModel": "text-embedding-3-small"
  },
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334,
    "CollectionName": "knowledge_chunks"
  },
  "RAG": {
    "TopK": 5
  }
}
```

- [ ] **Step 4: Write Program.cs (clean, no controllers weather forecast)**

File: `CliproxyApi/Program.cs`
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
```

- [ ] **Step 5: Verify it compiles**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService/CliproxyApi && dotnet build
```
Expected: Build succeeded.

---

### Task 6: Write CliproxyApi Models

**Files:**
- Create: `CliproxyApi/Models/ProxyModels.cs`

- [ ] **Step 1: Write ProxyModels.cs**

File: `CliproxyApi/Models/ProxyModels.cs`
```csharp
namespace CliproxyApi.Models;

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public bool UseRag { get; set; } = true;
}

public class ChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public List<string> Sources { get; set; } = new();
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
```

---

### Task 7: Write CliproxyApi LLMService

**Files:**
- Create: `CliproxyApi/Services/LLMService.cs`

- [ ] **Step 1: Write LLMService.cs**

File: `CliproxyApi/Services/LLMService.cs`
```csharp
using System.Text;
using System.Text.Json;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class LLMService
{
    private readonly HttpClient _httpClient;
    private readonly string _chatModel;
    private readonly string _embeddingModel;
    private readonly ILogger<LLMService> _logger;

    public LLMService(IConfiguration config, ILogger<LLMService> logger)
    {
        _logger = logger;
        var baseUrl = config["CliproxyApi:BaseUrl"]!;
        var apiKey = config["CliproxyApi:ApiKey"]!;
        _chatModel = config["CliproxyApi:ChatModel"]!;
        _embeddingModel = config["CliproxyApi:EmbeddingModel"]!;

        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<float[]> GetEmbedding(string text)
    {
        var request = new EmbeddingRequest { Model = _embeddingModel, Input = text };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/v1/embeddings", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();
        return result!.Data[0].Embedding.ToArray();
    }

    public async Task<string> ChatWithContext(string context, string userMessage)
    {
        var messages = new List<LLMChatMessage>
        {
            new() { Role = "system", Content = $"你是一个智能客服助手。请基于以下参考知识回答用户问题。如果参考知识中没有相关信息，请如实告知。\n\n参考知识：\n{context}" },
            new() { Role = "user", Content = userMessage }
        };

        return await Chat(messages);
    }

    public async Task<string> ChatDirect(string userMessage)
    {
        var messages = new List<LLMChatMessage>
        {
            new() { Role = "user", Content = userMessage }
        };

        return await Chat(messages);
    }

    private async Task<string> Chat(List<LLMChatMessage> messages)
    {
        var request = new LLMChatRequest { Model = _chatModel, Messages = messages };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LLMChatResponse>();
        return result!.Choices[0].Message.Content;
    }
}
```

---

### Task 8: Write CliproxyApi QdrantService

**Files:**
- Create: `CliproxyApi/Services/QdrantService.cs`

- [ ] **Step 1: Write QdrantService.cs**

File: `CliproxyApi/Services/QdrantService.cs`
```csharp
using Qdrant.Client;
using Qdrant.Client.Grpc;
using CliproxyApi.Models;

namespace CliproxyApi.Services;

public class QdrantService
{
    private readonly QdrantClient _client;
    private readonly string _collectionName;
    private readonly LLMService _llmService;
    private readonly ILogger<QdrantService> _logger;

    public QdrantService(IConfiguration config, LLMService llmService, ILogger<QdrantService> logger)
    {
        _llmService = llmService;
        _logger = logger;
        var host = config["Qdrant:Host"]!;
        var port = int.Parse(config["Qdrant:Port"]!);
        _collectionName = config["Qdrant:CollectionName"]!;
        _client = new QdrantClient(host, port);
    }

    public async Task<List<QdrantSearchResult>> RetrieveRelevantChunks(string query, int topK = 5)
    {
        try
        {
            var queryVector = await _llmService.GetEmbedding(query);
            var searchResult = await _client.SearchAsync(_collectionName, queryVector.ToArray(), limit: (ulong)topK);

            return searchResult.Select(r => new QdrantSearchResult
            {
                Text = r.Payload["text"].StringValue,
                Source = r.Payload.TryGetValue("source", out var src) ? src.StringValue : "unknown",
                Score = r.Score
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant retrieval failed, returning empty results");
            return new List<QdrantSearchResult>();
        }
    }
}
```

---

### Task 9: Write CliproxyApi ChatProxyController

**Files:**
- Create: `CliproxyApi/Controllers/ChatProxyController.cs`
- Delete: `CliproxyApi/Controllers/WeatherForecastController.cs` (if exists)

- [ ] **Step 1: Remove default template controller**

```bash
rm -f /home/chenshaopeng/myprojectfiled/SmartCustomerService/CliproxyApi/Controllers/WeatherForecastController.cs
```

- [ ] **Step 2: Write ChatProxyController.cs**

File: `CliproxyApi/Controllers/ChatProxyController.cs`
```csharp
using Microsoft.AspNetCore.Mvc;
using CliproxyApi.Models;
using CliproxyApi.Services;

namespace CliproxyApi.Controllers;

[ApiController]
[Route("api/v1")]
public class ChatProxyController : ControllerBase
{
    private readonly QdrantService _qdrantService;
    private readonly LLMService _llmService;
    private readonly IConfiguration _config;
    private readonly ILogger<ChatProxyController> _logger;

    public ChatProxyController(QdrantService qdrantService, LLMService llmService,
        IConfiguration config, ILogger<ChatProxyController> logger)
    {
        _qdrantService = qdrantService;
        _llmService = llmService;
        _config = config;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request)
    {
        try
        {
            if (request.UseRag)
            {
                var topK = int.Parse(_config["RAG:TopK"] ?? "5");
                var chunks = await _qdrantService.RetrieveRelevantChunks(request.Message, topK);

                if (chunks.Count > 0)
                {
                    var context = string.Join("\n---\n", chunks.Select(c => c.Text));
                    var reply = await _llmService.ChatWithContext(context, request.Message);
                    return new ChatResponse
                    {
                        Reply = reply,
                        Sources = chunks.Select(c => c.Source).Distinct().ToList()
                    };
                }
            }

            var directReply = await _llmService.ChatDirect(request.Message);
            return new ChatResponse { Reply = directReply };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "LLM service unavailable");
            return StatusCode(503, new ChatResponse { Reply = "大模型服务暂不可用，请稍后重试。" });
        }
    }
}
```

- [ ] **Step 3: Verify build**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService/CliproxyApi && dotnet build
```
Expected: Build succeeded.

---

### Task 10: Create CustomerService project

**Files:**
- Create: `CustomerService/CustomerService.csproj`
- Create: `CustomerService/Program.cs`
- Create: `CustomerService/appsettings.json`

- [ ] **Step 1: Create Web API project and add to solution**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService && dotnet new webapi -n CustomerService --no-https && dotnet sln add CustomerService/CustomerService.csproj
```

- [ ] **Step 2: Write appsettings.json**

File: `CustomerService/appsettings.json`
```json
{
  "Logging": {
    "LogLevel": { "Default": "Information" }
  },
  "CliproxyApi": {
    "BaseUrl": "http://localhost:5002"
  }
}
```

- [ ] **Step 3: Write Program.cs**

File: `CustomerService/Program.cs`
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
```

- [ ] **Step 4: Verify build**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService/CustomerService && dotnet build
```
Expected: Build succeeded.

---

### Task 11: Write CustomerService Models

**Files:**
- Create: `CustomerService/Models/ChatModels.cs`

- [ ] **Step 1: Write ChatModels.cs**

File: `CustomerService/Models/ChatModels.cs`
```csharp
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
}

public class ProxyChatRequest
{
    public string Message { get; set; } = string.Empty;
    public bool UseRag { get; set; } = true;
}

public class ProxyChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public List<string> Sources { get; set; } = new();
}
```

---

### Task 12: Write CustomerService CustomerAIService

**Files:**
- Create: `CustomerService/Services/CustomerAIService.cs`

- [ ] **Step 1: Write CustomerAIService.cs**

File: `CustomerService/Services/CustomerAIService.cs`
```csharp
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using CustomerService.Models;

namespace CustomerService.Services;

public class CustomerAIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CustomerAIService> _logger;
    private readonly ConcurrentDictionary<string, List<(string Role, string Content)>> _sessions = new();

    public CustomerAIService(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<CustomerAIService> logger)
    {
        _logger = logger;
        var baseUrl = config["CliproxyApi:BaseUrl"]!;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<CustomerChatResponse> ChatAsync(string userId, string userMessage)
    {
        var sessionId = $"sess_{userId}_{Guid.NewGuid():N}"[..16];

        var proxyRequest = new ProxyChatRequest { Message = userMessage, UseRag = true };
        var content = new StringContent(JsonSerializer.Serialize(proxyRequest), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/api/v1/chat", content);
        response.EnsureSuccessStatusCode();

        var proxyResponse = await response.Content.ReadFromJsonAsync<ProxyChatResponse>();

        // Store in session history
        var history = _sessions.GetOrAdd(userId, _ => new List<(string, string)>());
        history.Add(("user", userMessage));
        history.Add(("assistant", proxyResponse!.Reply));

        return new CustomerChatResponse
        {
            Reply = proxyResponse.Reply,
            SessionId = sessionId,
            Sources = proxyResponse.Sources
        };
    }

    public List<(string Role, string Content)> GetHistory(string userId)
    {
        return _sessions.TryGetValue(userId, out var history) ? history : new();
    }
}
```

---

### Task 13: Write CustomerService CustomerController

**Files:**
- Create: `CustomerService/Controllers/CustomerController.cs`
- Delete: `CustomerService/Controllers/WeatherForecastController.cs` (if exists)

- [ ] **Step 1: Remove default template controller**

```bash
rm -f /home/chenshaopeng/myprojectfiled/SmartCustomerService/CustomerService/Controllers/WeatherForecastController.cs
```

- [ ] **Step 2: Register CustomerAIService in Program.cs**

Edit `CustomerService/Program.cs` — add before `var app = builder.Build();`:
```csharp
builder.Services.AddSingleton<CustomerService.Services.CustomerAIService>();
```

- [ ] **Step 3: Write CustomerController.cs**

File: `CustomerService/Controllers/CustomerController.cs`
```csharp
using Microsoft.AspNetCore.Mvc;
using CustomerService.Models;
using CustomerService.Services;

namespace CustomerService.Controllers;

[ApiController]
[Route("api/customer")]
public class CustomerController : ControllerBase
{
    private readonly CustomerAIService _aiService;
    private readonly ILogger<CustomerController> _logger;

    public CustomerController(CustomerAIService aiService, ILogger<CustomerController> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<CustomerChatResponse>> Chat([FromBody] CustomerChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new CustomerChatResponse { Reply = "消息不能为空。" });

        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new CustomerChatResponse { Reply = "用户ID不能为空。" });

        try
        {
            var response = await _aiService.ChatAsync(request.UserId, request.Message);
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach CliproxyApi");
            return StatusCode(503, new CustomerChatResponse { Reply = "智能客服服务暂不可用，请稍后重试。" });
        }
    }

    [HttpGet("history/{userId}")]
    public ActionResult<List<(string Role, string Content)>> GetHistory(string userId)
    {
        return _aiService.GetHistory(userId);
    }
}
```

- [ ] **Step 4: Verify build**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService/CustomerService && dotnet build
```
Expected: Build succeeded.

---

### Task 14: End-to-end build and test

**Files:** None

- [ ] **Step 1: Build entire solution**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService && dotnet build
```
Expected: All 3 projects build successfully.

- [ ] **Step 2: Start CliproxyApi in background**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService/CliproxyApi && dotnet run --urls http://localhost:5002 &
sleep 3
curl -s http://localhost:5002/swagger/index.html | head -1
```

- [ ] **Step 3: Start CustomerService in background**

```bash
cd /home/chenshaopeng/myprojectfiled/SmartCustomerService/CustomerService && dotnet run --urls http://localhost:5000 &
sleep 3
curl -s http://localhost:5000/swagger/index.html | head -1
```

- [ ] **Step 4: Test chat endpoint (no RAG — Qdrant is empty)**

```bash
curl -s -X POST http://localhost:5000/api/customer/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"你好，请介绍一下你自己","userId":"test"}'
```
Expected: JSON response with a `reply` field containing text.

- [ ] **Step 5: Test chat endpoint with empty message**

```bash
curl -s -X POST http://localhost:5000/api/customer/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"","userId":"test"}'
```
Expected: 400 with "消息不能为空。"

- [ ] **Step 6: Test session history**

```bash
curl -s http://localhost:5000/api/customer/history/test
```
Expected: JSON array with previous conversation turns.

- [ ] **Step 7: Kill background processes**

```bash
pkill -f "dotnet run"
```
