#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Grpc.Core;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SmartAssistant.Models.AI;
using SmartAssistant.Models.Knowledge;

namespace SmartAssistant.Services.Infrastructure;

/// <summary>
/// 定义将上传文件转换为知识库正文的解析契约。
/// </summary>
public interface IAssistantDocumentParser
{
    /// <summary>
    /// 按媒体类型读取文本或 DOCX 正文，返回标准化文本与解析器版本。
    /// </summary>
    Task<AssistantParsedDocument> ParseAsync(string contentType, string fileName, Stream content, CancellationToken ct);
}

/// <summary>
/// 解析文本、Markdown、JSON 和 DOCX 文档，并拒绝不支持或空白的内容。
/// </summary>
public sealed class AssistantDocumentParser : IAssistantDocumentParser
{
    /// <summary>
    /// 按媒体类型读取文本或 DOCX 正文，返回标准化文本与解析器版本。
    /// </summary>
    public async Task<AssistantParsedDocument> ParseAsync(string contentType, string fileName, Stream content, CancellationToken ct)
    {
        var normalized = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
            return await ParseDocxAsync(content, ct);

        if (normalized is not ("text/plain" or "text/markdown" or "application/json"))
            throw new InvalidDataException("knowledge_document_parser_unsupported_type");

        using var reader = new StreamReader(content, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("knowledge_document_empty");
        return new AssistantParsedDocument(text.Replace("\r\n", "\n", StringComparison.Ordinal), "text-v1");
    }

    /// <summary>
    /// 解析助手文档解析器，并响应调用方的取消请求。
    /// </summary>
    private static async Task<AssistantParsedDocument> ParseDocxAsync(Stream content, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("knowledge_document_docx_document_part_missing");

        XDocument document;
        await using (var entryStream = entry.Open())
        {
            document = await XDocument.LoadAsync(entryStream, LoadOptions.PreserveWhitespace, ct);
        }

        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var paragraphs = document.Descendants(w + "p")
            .Select(paragraph => ReadParagraph(paragraph, w))
            .Where(text => text.Length > 0)
            .ToArray();
        var text = string.Join("\n", paragraphs);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("knowledge_document_empty");

        return new AssistantParsedDocument(text, "docx-v1");
    }

    /// <summary>
    /// 读取助手文档解析器数据。
    /// </summary>
    private static string ReadParagraph(XElement paragraph, XNamespace w)
    {
        var builder = new StringBuilder();
        foreach (var node in paragraph.Descendants())
        {
            if (node.Name == w + "t")
                builder.Append(node.Value);
            else if (node.Name == w + "tab")
                builder.Append('\t');
            else if (node.Name == w + "br" || node.Name == w + "cr")
                builder.Append('\n');
        }

        return builder.ToString().Trim();
    }
}

/// <summary>
/// 按冻结的嵌入配置为一批文本生成向量。
/// </summary>
public interface IAssistantEmbeddingProvider
{
    /// <summary>
    /// 按执行配置为输入文本生成对应向量，并校验返回数量和维度。
    /// </summary>
    Task<AssistantEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, AssistantKnowledgeEmbeddingExecutionProfile profile, CancellationToken ct);
}

/// <summary>
/// 用于开发和测试的确定性哈希向量生成器。
/// 该实现不提供语义嵌入能力，正式检索应使用配置的嵌入模型服务。
/// </summary>
public sealed class DeterministicHashEmbeddingProvider : IAssistantEmbeddingProvider
{
    /// <summary>
    /// 定义相关内容摘要嵌入模型供应商使用的默认值维度配置值。
    /// </summary>
    public const int DefaultDimension = 128;
    /// <summary>
    /// 定义相关内容摘要嵌入模型供应商使用的默认值配置标识配置值。
    /// </summary>
    public const string DefaultProfileId = "hash-v1";

    /// <summary>
    /// 使用文本摘要生成确定性测试向量；该实现不提供语义检索嵌入。
    /// </summary>
    public Task<AssistantEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, AssistantKnowledgeEmbeddingExecutionProfile profile, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (profile.Dimension != DefaultDimension)
            throw new InvalidOperationException("assistant_embedding_dimension_mismatch");
        var vectors = inputs.Select(CreateVector).ToArray();
        return Task.FromResult<AssistantEmbeddingResult>(new(vectors, profile.ProviderProfileRef, profile.Dimension));
    }

    /// <summary>
    /// 根据文本摘要构造固定维度的归一化测试向量。
    /// </summary>
    private static float[] CreateVector(string input)
    {
        var vector = new float[DefaultDimension];
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        for (var i = 0; i < vector.Length; i++)
        {
            var b = bytes[i % bytes.Length];
            vector[i] = (b - 127.5f) / 127.5f;
        }
        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        if (norm > 0)
            for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
        return vector;
    }
}

/// <summary>
/// 调用兼容 OpenAI 的嵌入接口，分批提交文本并校验返回向量。
/// </summary>
public sealed class OpenAiCompatibleEmbeddingProvider : IAssistantEmbeddingProvider
{
    /*
     * DashScope 兼容嵌入接口每次请求最多接受 20 条文本。
     * 此处限制传输批次，独立于文档解析和分块策略。
     */
    /// <summary>
    /// 定义打开兼容嵌入模型供应商使用的最大批次大小配置值。
    /// </summary>
    private const int MaxBatchSize = 20;
    /// <summary>
    /// 保存复用的 HTTP 传输依赖，用于访问配置的模型或搜索服务。
    /// </summary>
    private readonly IHttpClientFactory _httpClientFactory;
    /// <summary>
    /// 保存相关内容.助手模型供应商凭据解析器依赖，供打开兼容嵌入模型供应商执行相关操作。
    /// </summary>
    private readonly AI.IAssistantProviderCredentialResolver _credentialResolver;
    /// <summary>
    /// 接收并保存打开兼容嵌入模型供应商执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public OpenAiCompatibleEmbeddingProvider(
        IHttpClientFactory httpClientFactory,
        AI.IAssistantProviderCredentialResolver credentialResolver)
    {
        _httpClientFactory = httpClientFactory;
        _credentialResolver = credentialResolver;
    }

    /// <summary>
    /// 按执行配置为输入文本生成对应向量，并校验返回数量和维度。
    /// </summary>
    public async Task<AssistantEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, AssistantKnowledgeEmbeddingExecutionProfile profile, CancellationToken ct)
    {
        if (inputs.Count == 0 || profile.Dimension is < 1 or > 32768)
            throw new InvalidOperationException("assistant_embedding_request_invalid");
        var credential = await _credentialResolver.ResolveAsync(
            new AssistantProviderAccess(profile.ProviderProfileRef, profile.Model, "embedding", profile.Dimension), ct);
        using var client = _httpClientFactory.CreateClient("assistant-provider");
        var vectors = new List<float[]>(inputs.Count);
        foreach (var batch in inputs.Chunk(MaxBatchSize))
        {
            ct.ThrowIfCancellationRequested();
            /*
             * 每个响应批次的索引都从零开始，需逐批校验和排序后再拼接。
             * 任何一批失败都不能返回仅包含部分文档的向量结果。
             */
            vectors.AddRange(await EmbedBatchAsync(client, batch, credential, profile, ct));
        }
        return new AssistantEmbeddingResult(vectors, profile.ProviderProfileRef, profile.Dimension);
    }

    /// <summary>
    /// 分批请求兼容 OpenAI 的嵌入接口，并校验返回数量、顺序和向量维度。
    /// </summary>
    private static async Task<IReadOnlyList<float[]>> EmbedBatchAsync(HttpClient client, IReadOnlyList<string> inputs,
        OpenAiCompatibleProviderCredential credential, AssistantKnowledgeEmbeddingExecutionProfile profile, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Application.AssistantPolicyService.ApiEndpoint(credential.BaseUrl.ToString(), "embeddings"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.ApiKey);
        if (!string.IsNullOrWhiteSpace(credential.Organization))
            request.Headers.TryAddWithoutValidation("OpenAI-Organization", credential.Organization);
        request.Content = JsonContent.Create(new { model = profile.Model, input = inputs, dimensions = profile.Dimension });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(credential.Timeout ?? TimeSpan.FromSeconds(90));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("assistant_embedding_provider_failed");
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("assistant_embedding_response_invalid");
            var indexed = data.EnumerateArray().Select(item => (
                    Index: item.GetProperty("index").GetInt32(),
                    Vector: item.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray()))
                .OrderBy(item => item.Index).ToArray();
            var vectors = indexed.Select(item => item.Vector).ToArray();
            if (vectors.Length != inputs.Count || indexed.Where((item, index) => item.Index != index).Any()
                || vectors.Any(vector => !AssistantEmbeddingValidation.IsValid(vector, profile.Dimension)))
                throw new InvalidOperationException("assistant_embedding_dimension_mismatch");
            return vectors;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            /*
             * 供应商或客户端超时作为可重试故障处理，调用方主动取消则继续向上传播。
             * 对外错误不得包含供应商响应正文或凭据细节。
             */
            throw new InvalidOperationException("assistant_embedding_provider_failed");
        }
    }
}

/// <summary>
/// 定义向量索引写入、回读与受片段白名单约束的相似度检索操作。
/// </summary>
public interface IAssistantVectorIndexStore
{
    /// <summary>
    /// 校验向量和集合维度后写入片段，同一集合中的同标识片段按更新处理。
    /// </summary>
    Task UpsertAsync(string collectionName, int dimension, IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct);
    /// <summary>
    /// 回读指定片段的索引标识和内容哈希，供索引任务核对写入结果。
    /// </summary>
    Task<IReadOnlyList<AssistantVectorPointReadback>> ReadBackAsync(string collectionName, IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct);
    /// <summary>
    /// 在指定集合内检索相似向量；提供片段白名单时只返回允许范围内的候选。
    /// </summary>
    Task<IReadOnlyList<AssistantVectorSearchHit>> SearchAsync(
        string collectionName, ReadOnlyMemory<float> vector, int limit, CancellationToken ct,
        IReadOnlyCollection<string>? allowedChunkIds = null);
}

/// <summary>
/// 校验向量维度、数值有效性与非零范数。
/// </summary>
public static class AssistantEmbeddingValidation
{
    /// <summary>
    /// 检查向量长度、有限数值和非零范数，避免无效向量进入索引或相似度计算。
    /// </summary>
    public static bool IsValid(IReadOnlyList<float> vector, int dimension)
        => vector.Count == dimension && dimension > 0
            && vector.All(float.IsFinite) && vector.Any(value => value != 0);
}

/// <summary>
/// 使用 Qdrant 保存和查询知识片段向量，并回读索引点验证写入结果。
/// </summary>
public sealed class QdrantAssistantVectorIndexStore : IAssistantVectorIndexStore
{
    /// <summary>
    /// 保存Qdrant客户端依赖，供Qdrant助手向量索引执行相关操作。
    /// </summary>
    private readonly IQdrantClient _client;

    /// <summary>
    /// 接收并保存Qdrant助手向量索引执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public QdrantAssistantVectorIndexStore(IQdrantClient client) => _client = client;

    /// <summary>
    /// 校验向量和集合维度后写入片段，同一集合中的同标识片段按更新处理。
    /// </summary>
    public async Task UpsertAsync(string collectionName, int dimension, IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct)
    {
        if (chunks.Count == 0) return;
        if (dimension < 1 || chunks.Any(chunk => !AssistantEmbeddingValidation.IsValid(chunk.Vector, dimension))
            || chunks.Select(chunk => chunk.ChunkId).Distinct(StringComparer.Ordinal).Count() != chunks.Count)
            throw new InvalidOperationException("assistant_embedding_dimension_mismatch");
        try
        {
            await _client.CreateCollectionAsync(collectionName, new VectorParams
            {
                Size = (ulong)dimension,
                Distance = Distance.Cosine
            }, cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            /*
             * 重试索引时集合已存在是正常情况，沿用已有集合以保持操作幂等。
             */
        }

        var points = chunks.Select(chunk => new PointStruct
        {
            Id = new PointId { Uuid = AssistantKnowledgeIndexNaming.PointId(chunk.ChunkId) },
            Vectors = chunk.Vector,
            Payload =
            {
                ["chunk_id"] = chunk.ChunkId,
                ["content_hash"] = chunk.ContentHash,
                ["document_id"] = chunk.DocumentId,
                ["version_no"] = chunk.VersionNo,
                ["chunk_index"] = chunk.ChunkIndex
            }
        }).ToList();
        await _client.UpsertAsync(collectionName, points, wait: true, cancellationToken: ct);
    }

    /// <summary>
    /// 回读指定片段的索引标识和内容哈希，供索引任务核对写入结果。
    /// </summary>
    public async Task<IReadOnlyList<AssistantVectorPointReadback>> ReadBackAsync(
        string collectionName, IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct)
    {
        if (chunks.Count == 0) return Array.Empty<AssistantVectorPointReadback>();
        var pointIds = chunks.Select(chunk => new PointId
        {
            Uuid = AssistantKnowledgeIndexNaming.PointId(chunk.ChunkId)
        }).ToArray();
        var points = await _client.RetrieveAsync(collectionName, pointIds,
            withPayload: true, withVectors: true, cancellationToken: ct);
        var expected = chunks.ToDictionary(chunk => chunk.ChunkId, StringComparer.Ordinal);
        return points.Select(point => new AssistantVectorPointReadback(
                GetString(point.Payload, "chunk_id") ?? string.Empty,
                GetString(point.Payload, "content_hash") ?? string.Empty,
                point.Id.Uuid))
            .Where(point => expected.ContainsKey(point.ChunkId) && points.Any(stored =>
                stored.Id.Uuid == point.PointId
                && stored.Vectors?.Vector?.GetDenseVector() is { } vector
                && AssistantEmbeddingValidation.IsValid(vector.Data, expected[point.ChunkId].Vector.Length)))
            .ToArray();
    }

    /// <summary>
    /// 在指定集合内检索相似向量；提供片段白名单时只返回允许范围内的候选。
    /// </summary>
    public async Task<IReadOnlyList<AssistantVectorSearchHit>> SearchAsync(
        string collectionName, ReadOnlyMemory<float> vector, int limit, CancellationToken ct,
        IReadOnlyCollection<string>? allowedChunkIds = null)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name is required.", nameof(collectionName));
        if (vector.Length == 0)
            return Array.Empty<AssistantVectorSearchHit>();

        Filter? filter = null;
        if (allowedChunkIds is not null)
        {
            var ids = allowedChunkIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length == 0)
                return Array.Empty<AssistantVectorSearchHit>();
            filter = Conditions.Match("chunk_id", ids);
        }
        var points = await _client.QueryAsync(
            collectionName,
            query: vector.ToArray(),
            filter: filter,
            limit: (ulong)Math.Clamp(limit, 1, 100),
            payloadSelector: (WithPayloadSelector)true,
            cancellationToken: ct);

        return points.Select(point => new AssistantVectorSearchHit(
                GetString(point.Payload, "chunk_id") ?? string.Empty,
                point.Score,
                GetString(point.Payload, "document_id"),
                GetInt32(point.Payload, "version_no"),
                GetInt32(point.Payload, "chunk_index")))
            .Where(hit => !string.IsNullOrWhiteSpace(hit.ChunkId))
            .ToArray();
    }

    /// <summary>
    /// 读取Qdrant助手向量索引记录。
    /// </summary>
    private static string? GetString(
        Google.Protobuf.Collections.MapField<string, Value> payload, string key)
        => payload.TryGetValue(key, out var value) && value.KindCase == Value.KindOneofCase.StringValue
            ? value.StringValue
            : null;

    /// <summary>
    /// 读取Qdrant助手向量索引记录。
    /// </summary>
    private static int? GetInt32(
        Google.Protobuf.Collections.MapField<string, Value> payload, string key)
        => payload.TryGetValue(key, out var value) && value.KindCase == Value.KindOneofCase.IntegerValue
            ? checked((int)value.IntegerValue)
            : null;

}

/// <summary>
/// 生成稳定的向量点、嵌入配置和集合标识，隔离不同文档及模型代次。
/// </summary>
public static class AssistantKnowledgeIndexNaming
{
    /// <summary>
    /// 根据片段标识生成稳定的向量索引点标识。
    /// </summary>
    public static string PointId(string chunkId) => GuidFromHash(chunkId);

    /// <summary>
    /// 组合供应商配置引用、模型和向量维度，生成稳定的嵌入配置标识。
    /// </summary>
    public static string ProfileId(AssistantKnowledgeEmbeddingExecutionProfile profile)
        => $"{profile.ProviderProfileRef}:{profile.Model}:{profile.Dimension.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>
    /// 对知识库和配置标识生成摘要，结合代次构造向量集合名称。
    /// </summary>
    public static string Collection(string knowledgeBaseId, string profileId, long generation = 1)
    {
        /*
         * 计算字符串的 SHA-256 摘要并截取固定长度，用于集合名称隔离。
         */
        static string Short(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];
        return $"assistant_kb_{Short(knowledgeBaseId)}_{Short(profileId)}_g{generation}";
    }

    /// <summary>
    /// 根据输入摘要生成稳定 GUID，并设置标准版本和变体位。
    /// </summary>
    private static string GuidFromHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString();
    }
}
