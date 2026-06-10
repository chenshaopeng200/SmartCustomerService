# Smart Customer Service - Implementation Design

**Date:** 2026-06-09
**Tech Stack:** .NET 8 + C#, Qdrant, Go CliproxyApi

## Architecture

Three-layer architecture per the design document, adapted to use existing Go CliproxyApi (port 8317) as LLM backend instead of direct OpenAI calls.

```
User → CustomerService(:5000) → CliproxyApi(:5002) → Go CliproxyApi(:8317) → LLM
                                    ↓
                            Qdrant(:6333) vector search
```

## Project Structure

```
SmartCustomerService/
├── SmartCustomerService.sln
├── docker-compose.yml              # Qdrant
├── RAGIndexer/                     # Console app
│   ├── RAGIndexer.csproj
│   ├── Program.cs
│   └── appsettings.json
├── CliproxyApi/                    # ASP.NET Web API
│   ├── CliproxyApi.csproj
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Controllers/ChatProxyController.cs
│   ├── Services/QdrantService.cs
│   ├── Services/LLMService.cs
│   └── Models/ProxyModels.cs
└── CustomerService/                # ASP.NET Web API
    ├── CustomerService.csproj
    ├── Program.cs
    ├── appsettings.json
    ├── Controllers/CustomerController.cs
    ├── Services/CustomerAIService.cs
    └── Models/ChatModels.cs
```

## API Contracts

### CustomerService → CliproxyApi
```
POST /api/v1/chat
{ "message": "...", "useRag": true }
→ { "reply": "...", "sources": [...] }
```

### CliproxyApi → Go CliproxyApi (OpenAI-compatible)
```
POST http://localhost:8317/v1/chat/completions
{ "model": "deepseek-v4-pro", "messages": [...] }
POST http://localhost:8317/v1/embeddings
{ "model": "text-embedding-3-small", "input": "..." }
```

## Qdrant Schema
- Collection: `knowledge_chunks`
- Vector dimension: 1536
- Distance: Cosine
- Payload: text, source, chunk_index, category

## NuGet Packages
- RAGIndexer: UglyToad.PdfPig, Qdrant.Client
- CliproxyApi: Qdrant.Client
- CustomerService: none (pure HttpClient)

## Error Handling
- Go CliproxyApi unreachable → 503 "大模型服务暂不可用"
- Qdrant retrieval failure → degrade to pure LLM response
- PDF parse failure → skip file + log warning

## Implementation Order
1. docker-compose.yml → start Qdrant
2. RAGIndexer → PDF parsing, chunking, embedding, Qdrant storage
3. CliproxyApi → RAG retrieval + LLM generation
4. CustomerService → business layer + end-to-end test

## Configuration
- CliproxyApi BaseUrl: http://localhost:8317
- Qdrant: localhost:6334 (gRPC), HTTP: 6333
- Model: deepseek-v4-pro
- Embedding model: text-embedding-3-small
