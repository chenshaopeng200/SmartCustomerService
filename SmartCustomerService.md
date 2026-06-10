智能客服系统设计方案（基于 .NET Core + RAG）
1. 项目概述
本方案旨在构建一个企业级智能客服系统，能够基于私有知识文档（产品手册、FAQ、工单记录等），利用大语言模型（LLM）生成准确、自然的回答。系统采用 检索增强生成（RAG） 架构，并在 .NET Core 技术栈上实现，同时增加 CliproxyApi 代理层，实现对底层大模型的统一封装和治理。

2. 总体架构

用户（前端/微信/App）
         ↓
ASP.NET Core 智能客服后端（CustomerService）
         ↓ 调用
CliproxyApi（大模型代理层）
         ↓ 实际调用
大模型（OpenAI / Azure / Ollama / 本地模型）
同时，知识索引流程为：


原始文档（PDF、Word、TXT） → 解析 → 分块 → 向量化 → 存入向量数据库（Qdrant）
在线检索时：


用户问题 → 向量化 → 相似度检索 → 召回相关文本块 → 注入 Prompt → 大模型生成答案

2.1 文字版架构框图
┌─────────────────────────────────────────────────────────────┐
│                        用户层                                │
│               Web / App / 微信小程序 / 公众号                 │
└─────────────────────────────┬───────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                     业务后端层                               │
│           ASP.NET Core CustomerService                      │
│  ┌──────────────┐    ┌──────────────┐                      │
│  │ 会话管理      │    │ 对话历史缓存  │                      │
│  │ (User Context)│    │   (Redis)    │                      │
│  └──────────────┘    └──────────────┘                      │
└─────────────────────────────┬───────────────────────────────┘
                              │ HTTP + API Key
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    代理层 (CliproxyApi)                      │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐  │
│  │ 统一入口      │───▶│ 鉴权 & 限流   │───▶│ RAG 检索服务 │  │
│  │ /api/v1/chat │    │              │    │ (Qdrant)     │  │
│  └──────────────┘    └──────────────┘    └──────┬───────┘  │
│                                                  │          │
│  ┌──────────────┐    ┌──────────────┐           │          │
│  │ Prompt 构建   │◀───│ 大模型调用    │           │          │
│  └──────────────┘    └──────────────┘           │          │
└─────────────────────┬─────────────┬──────────────┼─────────┘
                      │             │              │
                      ▼             ▼              ▼
               ┌──────────┐  ┌──────────┐   ┌─────────────┐
               │ OpenAI   │  │ Azure    │   │ Qdrant      │
               │ GPT-3.5  │  │ OpenAI   │   │ 向量数据库   │
               │ / GPT-4  │  │          │   └─────────────┘
               └──────────┘  └──────────┘          ▲
                                                    │
┌───────────────────────────────────────────────────┼─────────┐
│                   数据与索引层                     │         │
│  ┌──────────────┐    ┌──────────────┐   ┌────────┴───────┐ │
│  │ 原始知识文档  │───▶│ 离线索引服务  │──▶│ PostgreSQL    │ │
│  │(PDF/Word/Txt)│    │(RAGIndexer)  │   │ (元数据备份)   │ │
│  └──────────────┘    └──────────────┘   └────────────────┘ │
└─────────────────────────────────────────────────────────────┘

2.2 组件职责
组件	职责
CustomerService	业务逻辑、会话管理、用户上下文、调用代理层
CliproxyApi	统一大模型入口，封装鉴权、限流、日志、RAG 检索、多模型路由
大模型	提供生成能力（OpenAI GPT / 本地模型）
RAGIndexer	离线/定时处理知识文档，生成向量索引
向量数据库	存储文本块向量，支持高效相似性检索（选用 Qdrant）
3. 核心数据设计（RAG 部分）
3.1 文档分块策略
文档类型	分块方式	块大小	重叠
FAQ（问答对）	每个(Q,A)作为一个块	200~500字符	0
长文档（手册）	按段落或语义边界	500~1000字符	10%~20%
对话/工单	按轮次固定长度	300~800字符	10%
3.2 数据表设计（关系型备份）
sql
CREATE TABLE knowledge_chunks (
    id            UUID PRIMARY KEY,
    content       TEXT NOT NULL,
    source        VARCHAR(255),
    doc_title     VARCHAR(255),
    chunk_index   INT,
    created_at    TIMESTAMP,
    metadata      JSONB
);
3.3 向量数据库结构（以 Qdrant 为例）
集合名称：knowledge_chunks

向量维度：1536（使用 OpenAI text-embedding-3-small）

距离度量：Cosine

负载（Payload）：

text: 文本块内容

source: 来源文件路径

chunk_index: 块序号

page: 页码（可选）

category: 分类（售后、技术等）

3.4 元数据设计
建议在向量数据库中存储以下元数据字段，用于检索前预过滤：

category：问题分类

product_line：产品线

source_type：FAQ / 手册 / 工单

version：文档版本

4. 向量数据库选型分析
数据库	定位	适合阶段	.NET SDK	部署复杂度
Qdrant	高性能专用向量库	MVP 到大规模生产	官方	低（Docker）
pgvector	PostgreSQL 扩展	小规模、已有PG	Npgsql	低
Milvus	十亿级超大规模	企业级生产	官方	高（K8s）
Chroma	本地原型验证	PoC	社区	零配置
Pinecone	全托管云服务	不想运维	REST API	无
推荐：MVP 阶段使用 Qdrant（单机 Docker），生产环境数据量增长后可平滑扩展。

5. MVP 定义与迭代路线
5.1 MVP 核心目标
用户输入问题 → 系统基于已有知识文档返回合理的、非模板化的答案。

5.2 MVP 必须包含的功能
用户可通过简单界面（命令行/Swagger/简陋HTML）发送问题

后台执行 RAG 流程：检索知识库 → 召回相关块 → 大模型生成答案

大模型能正确使用检索到的知识回答

单次请求延迟 ≤5 秒

5.3 MVP 可暂时不做
多轮对话记忆

用户鉴权与隔离

高并发、高可用

精细的 prompt 调优

监控告警

前端美化

5.4 迭代路线图
版本	增加功能	目标
V0.2	多轮对话记忆（Redis）	支持追问
V0.3	前端流式输出（SSE）	提升体验
V0.4	用户鉴权 + 会话隔离	多租户
V0.5	增量知识更新	降低成本
V0.6	Qdrant 集群	高可用
V1.0	监控、限流、日志	生产就绪
6. 完整代码实现

解决方案结构
SmartCustomerService/
├── SmartCustomerService.sln
├── docker-compose.yml
├── README.md
├── RAGIndexer/
│   ├── RAGIndexer.csproj
│   ├── Program.cs
│   ├── appsettings.json
│   └── sample.pdf (可自行放置测试文件)
├── CliproxyApi/
│   ├── CliproxyApi.csproj
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Controllers/
│   │   └── ChatProxyController.cs
│   ├── Services/
│   │   └── QdrantService.cs
│   └── Models/
│       └── ProxyModels.cs
└── CustomerService/
    ├── CustomerService.csproj
    ├── Program.cs
    ├── appsettings.json
    ├── Controllers/
    │   └── CustomerController.cs
    ├── Services/
    │   └── CustomerAIService.cs
    └── Models/
        └── ChatModels.cs


6.1 RAGIndexer（控制台应用）
功能：解析 PDF，分块，生成向量，存入 Qdrant。

关键 NuGet 包：

UglyToad.PdfPig（PDF 解析）

Qdrant.Client

OpenAI（Embedding 生成）

Microsoft.Extensions.Configuration

核心代码（Program.cs 简化版）：

csharp
// 提取PDF文本
string ExtractTextFromPdf(string path) { ... }

// 分块
List<TextChunk> SplitTextIntoChunks(string text, int maxChunkSize, int overlap) { ... }

// 向量化并存储
var embeddingClient = new EmbeddingClient(embeddingModel, openaiApiKey);
var qdrantClient = new QdrantClient(host, port);
foreach (var chunk in chunks)
{
    var embedding = await embeddingClient.GenerateEmbeddingAsync(chunk.Text);
    var point = new PointStruct
    {
        Id = chunk.Id,
        Vector = embedding.ToFloats(),
        Payload = new Dictionary<string, object> { ["text"] = chunk.Text }
    };
    await qdrantClient.UpsertAsync(collectionName, new[] { point });
}
6.2 CliproxyApi（大模型代理层）
功能：统一入口，提供 /api/v1/chat 端点，集成 RAG 检索。

主要文件：

Models/ProxyModels.cs：定义请求/响应模型

Services/QdrantService.cs：封装检索逻辑

Controllers/ChatProxyController.cs：处理聊天请求，调用大模型

关键代码片段（检索与生成）：

csharp
// 检索相关块
var retrievedChunks = await _qdrantService.RetrieveRelevantChunks(request.Message);
var context = string.Join("\n---\n", retrievedChunks);

// 构建消息
var chatMessages = new List<ChatMessage>
{
    new ChatMessage(ChatRole.System, $"参考知识：{context}\n请基于上述知识回答。"),
    new ChatMessage(ChatRole.User, request.Message)
};

// 调用大模型
var response = await _chatClient.CompleteAsync(chatMessages);
6.3 CustomerService（业务后端）
功能：处理用户请求，管理会话历史，调用 CliproxyApi。

主要文件：

Services/CustomerAIService.cs：HTTP 调用代理层，维护会话历史（内存/Redis）

Controllers/CustomerController.cs：暴露 /api/customer/chat 端点

简化版服务方法：

csharp
public async Task<string> ChatAsync(string userId, string userMessage)
{
    var proxyRequest = new { Message = userMessage, UseRag = true };
    var response = await _httpClient.PostAsJsonAsync("/api/v1/chat", proxyRequest);
    var proxyResponse = await response.Content.ReadFromJsonAsync<ProxyChatResponse>();
    return proxyResponse.Reply;
}
7. 部署与测试步骤
7.1 环境准备
.NET 8 SDK

Docker Desktop

7.2 启动基础服务
bash
cd docker
docker-compose up -d   # 启动 Qdrant（端口6333）和 Redis（端口6379）
7.3 索引知识文档
bash
cd RAGIndexer
# 将 sample.pdf 放入目录
dotnet run sample.pdf
7.4 启动代理层
bash
cd CliproxyApi
# 修改 appsettings.json 中的 OpenAI:ApiKey
dotnet run   # 默认端口 5002
7.5 启动客服后端
bash
cd CustomerService
# 确认 appsettings.json 中 CliproxyApi:BaseUrl 指向 http://localhost:5002
dotnet run   # 默认端口 5000
7.6 测试请求
bash
curl -X POST http://localhost:5000/api/customer/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"如何重置密码？","userId":"test"}'
8. 后续优化方向
模型切换：支持 Azure OpenAI、Ollama 本地模型（通过配置）

缓存：对常见问题的回答进行缓存，降低延迟和成本

混合检索：结合关键词（BM25）与向量检索，提升召回率

重排序：在检索后加入 rerank 模型，提高精准度

流式输出：使用 Server-Sent Events（SSE）提升用户体验

监控：集成 OpenTelemetry 和 Application Insights

9. 总结
本设计方案提供了从 MVP 到生产级 的完整路径，基于 .NET Core 实现了业务后端、代理层和 RAG 索引器三部分解耦，并给出了具体的代码实现和部署指南。您可以根据实际需求，从 MVP 开始快速验证，再逐步增加功能，最终构建一个稳定、可扩展的企业智能客服系统。