using System.Text.Json;
using AI.Assistant.Models.Knowledge;
using AI.Assistant.Services.Domain.Entities;
using AI.Assistant.Services.Repositorys;
using SqlSugar;

namespace AI.Assistant.Persistence;

/// <summary>
/// 使用 SQLite 持久化向量，并在单机内按余弦相似度检索文本片段。
/// 向量由配置的嵌入服务生成；本类仅负责保存、回读和检索。
/// </summary>
public sealed class LocalVectorIndex(IConfiguration configuration) : IAssistantVectorIndexStore
{
    /// <summary>
    /// 保存本地向量索引在处理目录时所需的数据或状态。
    /// </summary>
    private readonly string _dataDirectory = LocalDatabase.DataDirectory(configuration);
    /// <summary>
    /// 串行化本地向量索引中的临界写入，避免共享状态并发损坏。
    /// </summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// 校验向量和集合维度后写入片段，同一集合中的同标识片段按更新处理。
    /// </summary>
    public async Task UpsertAsync(string collectionName, int dimension,
        IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ct.ThrowIfCancellationRequested();
        if (chunks.Count == 0) return;
        if (dimension < 1 || chunks.Any(chunk => !AssistantEmbeddingValidation.IsValid(chunk.Vector, dimension))
            || chunks.Select(chunk => chunk.ChunkId).Distinct(StringComparer.Ordinal).Count() != chunks.Count)
            throw new InvalidOperationException("assistant_embedding_dimension_mismatch");

        await _writeGate.WaitAsync(ct);
        try
        {
            using var database = LocalDatabase.Open(_dataDirectory);
            await database.Ado.BeginTranAsync();
            try
            {
                var collection = await database.Queryable<VectorCollectionEntity>().Where(row => row.Name == collectionName).FirstAsync();
                if (collection is null)
                    await database.Insertable(new VectorCollectionEntity { Name = collectionName, Dimension = dimension }).ExecuteCommandAsync();
                else if (collection.Dimension != dimension)
                    throw new InvalidOperationException("assistant_embedding_dimension_mismatch");

                foreach (var chunk in chunks)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = new VectorPointEntity
                    {
                        CollectionName = collectionName,
                        ChunkId = chunk.ChunkId,
                        ContentHash = chunk.ContentHash,
                        DocumentId = chunk.DocumentId,
                        VersionNo = chunk.VersionNo,
                        ChunkIndex = chunk.ChunkIndex,
                        VectorJson = JsonSerializer.Serialize(chunk.Vector)
                    };
                    var affected = await database.Updateable(row)
                        .Where(point => point.CollectionName == collectionName && point.ChunkId == chunk.ChunkId)
                        .ExecuteCommandAsync();
                    if (affected == 0)
                        await database.Insertable(row).ExecuteCommandAsync();
                }
                ct.ThrowIfCancellationRequested();
                await database.Ado.CommitTranAsync();
            }
            catch
            {
                await database.Ado.RollbackTranAsync();
                throw;
            }
        }
        finally { _writeGate.Release(); }
    }

    /// <summary>
    /// 回读指定片段的索引标识和内容哈希，供索引任务核对写入结果。
    /// </summary>
    public async Task<IReadOnlyList<AssistantVectorPointReadback>> ReadBackAsync(string collectionName, IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ct.ThrowIfCancellationRequested();
        if (chunks.Count == 0) return [];
        using var database = LocalDatabase.Open(_dataDirectory);
        var expected = chunks.ToDictionary(chunk => chunk.ChunkId, StringComparer.Ordinal);
        var rows = await database.Queryable<VectorPointEntity>().Where(row => row.CollectionName == collectionName).ToListAsync();
        var result = new List<AssistantVectorPointReadback>();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(row.ChunkId, out var chunk)) continue;
            var stored = JsonSerializer.Deserialize<float[]>(row.VectorJson) ?? [];
            if (AssistantEmbeddingValidation.IsValid(stored, chunk.Vector.Length))
                result.Add(new(row.ChunkId, row.ContentHash, AssistantKnowledgeIndexNaming.PointId(row.ChunkId)));
        }
        return result;
    }

    /// <summary>
    /// 在指定集合内检索相似向量；提供片段白名单时只返回允许范围内的候选。
    /// </summary>
    public async Task<IReadOnlyList<AssistantVectorSearchHit>> SearchAsync(string collectionName, ReadOnlyMemory<float> vector, int limit, CancellationToken ct, IReadOnlyCollection<string>? allowedChunkIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ct.ThrowIfCancellationRequested();
        if (vector.Length == 0 || allowedChunkIds is { Count: 0 }) return [];
        using var database = LocalDatabase.Open(_dataDirectory);
        var collection = await database.Queryable<VectorCollectionEntity>().Where(row => row.Name == collectionName).FirstAsync();
        if (collection is null) return [];
        var query = vector.ToArray();
        if (!AssistantEmbeddingValidation.IsValid(query, collection.Dimension))
            throw new InvalidOperationException("assistant_embedding_dimension_mismatch");
        var allowed = allowedChunkIds?.ToHashSet(StringComparer.Ordinal);
        var rows = await database.Queryable<VectorPointEntity>().Where(row => row.CollectionName == collectionName).ToListAsync();
        var result = new List<AssistantVectorSearchHit>();
        var queryNorm = Math.Sqrt(query.Sum(value => (double)value * value));
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (allowed is not null && !allowed.Contains(row.ChunkId)) continue;
            var stored = JsonSerializer.Deserialize<float[]>(row.VectorJson) ?? [];
            if (!AssistantEmbeddingValidation.IsValid(stored, collection.Dimension))
                throw new InvalidDataException("assistant_vector_index_corrupt");
            double dot = 0, normSquared = 0;
            for (var i = 0; i < stored.Length; i++)
            {
                dot += (double)query[i] * stored[i];
                normSquared += (double)stored[i] * stored[i];
            }
            var score = Math.Clamp(dot / (queryNorm * Math.Sqrt(normSquared)), -1, 1);
            result.Add(new(row.ChunkId, score, row.DocumentId, row.VersionNo, row.ChunkIndex));
        }
        return result.OrderByDescending(hit => hit.Score).ThenBy(hit => hit.ChunkId, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 100)).ToArray();
    }
}
