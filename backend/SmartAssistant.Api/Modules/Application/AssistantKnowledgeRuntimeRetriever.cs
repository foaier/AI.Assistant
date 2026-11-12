#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using Grpc.Core;
using SmartAssistant.Modules.Domain.Entities;

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 运行时检索到的知识片段，包含文档来源、正文和相关度。
/// </summary>
public sealed record AssistantKnowledgeRuntimeMatch(
    string SegmentId,
    string DocumentId,
    string DocumentName,
    int SegmentIndex,
    string Text,
    double Relevance);

/// <summary>
/// 只检索执行快照中冻结的文档版本。
/// 向量索引用于召回候选，片段正文与最终范围校验以数据库回读结果为准。
/// </summary>
public interface IAssistantKnowledgeRuntimeRetriever
{
    /// <summary>
    /// 在冻结的嵌入配置和文档版本范围内检索知识片段，并回读正文验证范围与版本。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeRuntimeMatch>> RetrieveAsync(
        AssistantScopeSnapshot scope,
        AssistantExecutionSnapshot snapshot,
        string query,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 按冻结的嵌入配置生成查询向量，并回读已授权文档版本中的候选片段。
/// </summary>
public sealed class AssistantKnowledgeRuntimeRetriever : IAssistantKnowledgeRuntimeRetriever
{
    /// <summary>
    /// 保存助手知识仓储依赖，供助手知识运行时检索器执行相关操作。
    /// </summary>
    private readonly IAssistantKnowledgeRepository _knowledge;
    /// <summary>
    /// 保存助手知识文档仓储依赖，供助手知识运行时检索器执行相关操作。
    /// </summary>
    private readonly IAssistantKnowledgeDocumentRepository _documents;
    /// <summary>
    /// 保存助手嵌入模型供应商依赖，供助手知识运行时检索器执行相关操作。
    /// </summary>
    private readonly IAssistantEmbeddingProvider _embeddingProvider;
    /// <summary>
    /// 保存助手向量索引依赖，供助手知识运行时检索器执行相关操作。
    /// </summary>
    private readonly IAssistantVectorIndexStore _vectorIndexStore;

    /// <summary>
    /// 接收并保存助手知识运行时检索器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantKnowledgeRuntimeRetriever(
        IAssistantKnowledgeRepository knowledge,
        IAssistantKnowledgeDocumentRepository documents,
        IAssistantEmbeddingProvider embeddingProvider,
        IAssistantVectorIndexStore vectorIndexStore)
        => (_knowledge, _documents, _embeddingProvider, _vectorIndexStore) =
            (knowledge, documents, embeddingProvider, vectorIndexStore);

    /// <summary>
    /// 在冻结的嵌入配置和文档版本范围内检索知识片段，并回读正文验证范围与版本。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeRuntimeMatch>> RetrieveAsync(
        AssistantScopeSnapshot scope,
        AssistantExecutionSnapshot snapshot,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.KnowledgeBaseId is null || snapshot.Documents.Count == 0 || string.IsNullOrWhiteSpace(query)
            || snapshot.KnowledgeEmbedding is null)
            return Array.Empty<AssistantKnowledgeRuntimeMatch>();
        if (await _knowledge.GetBaseAsync(snapshot.KnowledgeBaseId, cancellationToken) is null)
            return Array.Empty<AssistantKnowledgeRuntimeMatch>();

        var candidates = new Dictionary<string, (AssistantKnowledgeChunkEntity Chunk,
            string DocumentId, string DocumentName)>();
        foreach (var frozen in snapshot.Documents)
        {
            var document = await _documents.GetDocumentAsync(
                snapshot.KnowledgeBaseId, frozen.Id, cancellationToken);
            /*
             * 文档主记录可能正在处理新的草稿版本，而发布快照仍指向已就绪的旧版本。
             * 运行时按冻结版本校验可用性，不能用可变的文档处理状态误判旧版本不可用。
             */
            if (document is null)
                continue;

            var version = await _documents.GetVersionAsync(
                frozen.Id, frozen.VersionNo, cancellationToken);
            if (version is null || version.Id != frozen.VersionId || version.Status != "ready" || version.IndexStatus != "ready")
                continue;

            foreach (var chunk in await _documents.ListChunksAsync(
                version.Id, cancellationToken))
                candidates[chunk.Id] = (chunk, document.Id, frozen.Name);
        }

        if (candidates.Count == 0)
            return Array.Empty<AssistantKnowledgeRuntimeMatch>();

        var embedding = await _embeddingProvider.EmbedAsync(
            new[] { query.Trim() }, snapshot.KnowledgeEmbedding, cancellationToken);
        if (embedding.ProfileId != snapshot.KnowledgeEmbedding.ProviderProfileRef
            || embedding.Dimension != snapshot.KnowledgeEmbedding.Dimension || embedding.Vectors.Count != 1
            || embedding.Vectors[0].Length != embedding.Dimension || embedding.Vectors[0].Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("assistant_embedding_response_invalid");
        var collection = AssistantKnowledgeIndexNaming.Collection(
            snapshot.KnowledgeBaseId,
            AssistantKnowledgeIndexNaming.ProfileId(snapshot.KnowledgeEmbedding), snapshot.KnowledgeEmbedding.Generation);
        IReadOnlyList<AssistantVectorSearchHit> hits;
        try
        {
            hits = await _vectorIndexStore.SearchAsync(
                collection, embedding.Vectors[0], Math.Clamp(limit * 4, 1, 100), cancellationToken,
                candidates.Keys);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Array.Empty<AssistantKnowledgeRuntimeMatch>();
        }

        var terms = AssistantKnowledgeService.NormalizeQuestion(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<AssistantKnowledgeRuntimeMatch>();
        foreach (var hit in hits)
        {
            if (!candidates.TryGetValue(hit.ChunkId, out var candidate))
                continue;
            var normalized = AssistantKnowledgeService.NormalizeQuestion(candidate.Chunk.Content);
            var lexical = terms.Length == 0
                ? 0d
                : (double)terms.Count(term => normalized.Contains(term, StringComparison.Ordinal)) / terms.Length;
            var relevance = Math.Clamp(hit.Score * 0.75d + lexical * 0.25d, 0d, 1d);
            result.Add(new AssistantKnowledgeRuntimeMatch(
                candidate.Chunk.Id, candidate.DocumentId, candidate.DocumentName,
                candidate.Chunk.ChunkIndex, candidate.Chunk.Content, relevance));
        }

        return result.OrderByDescending(x => x.Relevance)
            .ThenBy(x => x.DocumentName, StringComparer.Ordinal)
            .ThenBy(x => x.SegmentIndex)
            .Take(Math.Clamp(limit, 1, 20))
            .ToArray();
    }
}
