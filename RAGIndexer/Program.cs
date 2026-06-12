using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;
using HtmlAgilityPack;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var cliproxyBaseUrl = config["CliproxyApi:BaseUrl"]!;
var cliproxyApiKey = config["CliproxyApi:ApiKey"]!;
var embeddingModel = config["EmbeddingApi:Model"] ?? config["CliproxyApi:EmbeddingModel"]!;
var embeddingBaseUrl = config["EmbeddingApi:BaseUrl"]!;
var embeddingApiKey = config["EmbeddingApi:ApiKey"]!;
var qdrantHost = config["Qdrant:Host"]!;
var qdrantPort = int.Parse(config["Qdrant:Port"]!);
var collectionName = config["Qdrant:CollectionName"]!;
var vectorSize = int.Parse(config["Qdrant:VectorSize"]!);
var maxChunkSize = int.Parse(config["Chunking:MaxChunkSize"]!);
var overlap = int.Parse(config["Chunking:Overlap"]!);

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run <file-path> [<file-path>...]");
    Console.WriteLine("   or: dotnet run --watch <directory>");
    return;
}

using var httpClient = new HttpClient { BaseAddress = new Uri(cliproxyBaseUrl) };
httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {cliproxyApiKey}");

using var embeddingHttpClient = new HttpClient { BaseAddress = new Uri(embeddingBaseUrl) };
embeddingHttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {embeddingApiKey}");

using var qdrantClient = new QdrantClient(qdrantHost, qdrantPort);

var collections = await qdrantClient.ListCollectionsAsync();
if (!collections.Contains(collectionName))
{
    await qdrantClient.CreateCollectionAsync(collectionName,
        new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine });
    Console.WriteLine($"Created collection: {collectionName}");
}

// Watch mode
if (args[0] == "--watch" && args.Length > 1)
{
    var watchDir = args[1];
    Console.WriteLine($"Watching directory: {watchDir}");
    await IndexDirectoryAsync(qdrantClient, httpClient, embeddingHttpClient, watchDir, collectionName, embeddingModel, maxChunkSize, overlap);

    using var watcher = new FileSystemWatcher(watchDir)
    {
        EnableRaisingEvents = true,
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
    };

    watcher.Created += async (s, e) =>
    {
        await Task.Delay(1000);
        Console.WriteLine($"New file: {e.FullPath}");
        await IndexFileAsync(qdrantClient, httpClient, embeddingHttpClient, e.FullPath, collectionName, embeddingModel, maxChunkSize, overlap);
    };

    watcher.Changed += async (s, e) =>
    {
        await Task.Delay(2000);
        Console.WriteLine($"Changed: {e.FullPath}");
        await DeleteBySourceAsync(qdrantClient, collectionName, e.FullPath);
        await IndexFileAsync(qdrantClient, httpClient, embeddingHttpClient, e.FullPath, collectionName, embeddingModel, maxChunkSize, overlap);
    };

    watcher.Deleted += (s, e) =>
    {
        Console.WriteLine($"Deleted: {e.FullPath}");
        DeleteBySourceAsync(qdrantClient, collectionName, e.FullPath).Wait();
    };

    Console.WriteLine("Watching... Press Ctrl+C to stop.");
    await Task.Delay(Timeout.Infinite);
}
else
{
    foreach (var filePath in args)
    {
        if (Directory.Exists(filePath))
        {
            await IndexDirectoryAsync(qdrantClient, httpClient, embeddingHttpClient, filePath, collectionName, embeddingModel, maxChunkSize, overlap);
            continue;
        }

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}, skipping.");
            continue;
        }

        await IndexFileAsync(qdrantClient, httpClient, embeddingHttpClient, filePath, collectionName, embeddingModel, maxChunkSize, overlap);
    }
    Console.WriteLine("Done.");
}

static async Task IndexDirectoryAsync(QdrantClient qdrantClient, HttpClient httpClient, HttpClient embeddingHttpClient,
    string dir, string collectionName, string embeddingModel, int maxChunkSize, int overlap)
{
    var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
        .Where(f => IsSupported(f))
        .ToList();

    foreach (var file in files)
        await IndexFileAsync(qdrantClient, httpClient, embeddingHttpClient, file, collectionName, embeddingModel, maxChunkSize, overlap);
}

static async Task IndexFileAsync(QdrantClient qdrantClient, HttpClient httpClient, HttpClient embeddingHttpClient,
    string filePath, string collectionName, string embeddingModel, int maxChunkSize, int overlap)
{
    if (!File.Exists(filePath)) return;

    Console.WriteLine($"Processing: {filePath}");
    var text = ExtractText(filePath);
    if (string.IsNullOrWhiteSpace(text))
    {
        Console.WriteLine($"  No text extracted from {filePath}, skipping.");
        return;
    }

    var chunks = SplitTextIntoChunks(text, maxChunkSize, overlap);
    Console.WriteLine($"  Chunks: {chunks.Length}");

    var maxId = await GetMaxPointIdAsync(qdrantClient, collectionName);

    for (int i = 0; i < chunks.Length; i++)
    {
        var embedding = await GetEmbedding(embeddingHttpClient, embeddingModel, chunks[i]);
        var point = new PointStruct
        {
            Id = maxId + (ulong)i + 1,
            Vectors = embedding,
            Payload =
            {
                ["text"] = chunks[i],
                ["source"] = filePath,
                ["chunk_index"] = (long)i
            }
        };
        await qdrantClient.UpsertAsync(collectionName, new[] { point });
    }

    Console.WriteLine($"  Indexed {chunks.Length} chunks from {filePath}");
}

static async Task DeleteBySourceAsync(QdrantClient qdrantClient, string collection, string source)
{
    try
    {
        var filter = new Qdrant.Client.Grpc.Filter();
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key = "source",
                Match = new Qdrant.Client.Grpc.Match { Keyword = source }
            }
        });
        await qdrantClient.DeleteAsync(collection, filter);
        Console.WriteLine($"  Deleted points for source: {source}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Delete failed: {ex.Message}");
    }
}

static async Task<ulong> GetMaxPointIdAsync(QdrantClient qdrantClient, string collection)
{
    try
    {
        var response = await qdrantClient.ScrollAsync(collection, limit: 1, orderBy: new OrderBy { Key = "id" });
        return response.Result.Count > 0 ? response.Result[0].Id.Num : 0;
    }
    catch
    {
        return 0;
    }
}

static bool IsSupported(string path) =>
    Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => true,
        ".txt" => true,
        ".md" => true,
        ".html" => true,
        ".htm" => true,
        ".docx" => true,
        _ => false
    };

static string ExtractText(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext switch
    {
        ".pdf" => ExtractFromPdf(path),
        ".txt" or ".md" => File.ReadAllText(path, Encoding.UTF8),
        ".html" or ".htm" => ExtractFromHtml(path),
        ".docx" => ExtractFromDocx(path),
        _ => ""
    };
}

static string ExtractFromPdf(string path)
{
    var sb = new StringBuilder();
    using var pdf = PdfDocument.Open(path);
    foreach (var page in pdf.GetPages())
        sb.AppendLine(page.Text);
    return sb.ToString();
}

static string ExtractFromDocx(string path)
{
    var sb = new StringBuilder();
    using var doc = WordprocessingDocument.Open(path, false);
    var body = doc?.MainDocumentPart?.Document?.Body;
    if (body == null) return "";
    foreach (var para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        sb.AppendLine(para.InnerText);
    return sb.ToString();
}

static string ExtractFromHtml(string path)
{
    var html = File.ReadAllText(path, Encoding.UTF8);
    var doc = new HtmlDocument();
    doc.LoadHtml(html);
    return doc.DocumentNode.InnerText;
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
        var chunk = text[start..end].Trim();
        if (!string.IsNullOrWhiteSpace(chunk))
            chunks.Add(chunk);
        start = end - overlap;
        if (start < 0) start = 0;
    }
    return chunks.ToArray();
}

static async Task<float[]> GetEmbedding(HttpClient client, string model, string text)
{
    var requestBody = new { model, input = text };
    var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
    var response = await client.PostAsync("/v1/embeddings", content);
    response.EnsureSuccessStatusCode();
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    return json.GetProperty("data")[0].GetProperty("embedding").EnumerateArray()
        .Select(e => e.GetSingle()).ToArray();
}
