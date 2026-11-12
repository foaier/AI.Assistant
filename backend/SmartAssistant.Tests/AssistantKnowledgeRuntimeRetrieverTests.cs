#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using Xunit;
using SmartAssistant.Modules.Domain.Entities;

namespace SmartAssistant.Tests;

// Spec: specs/136-public-assistant/spec.md, published execution snapshot runtime.
// Proof: runtime retrieval reads only the frozen document version; a mutable document
// status for a newer draft cannot hide the published ready version.
/// <summary>
/// 验证助手知识运行时检索器行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantKnowledgeRuntimeRetrieverTests
{
    /// <summary>
    /// 验证文档重新处理期间仍读取已冻结的可用版本。
    /// </summary>
    [Fact]
    public async Task Processing_document_still_reads_frozen_ready_version()
    {
        var documents = new FakeDocumentRepository
        {
            Document = new AssistantKnowledgeDocumentEntity
            {
                Id = "doc",
                KnowledgeBaseId = "kb",
                FileName = "guide.md",
                Status = "processing",
                CurrentVersionNo = 2
            },
            Version = new AssistantKnowledgeDocumentVersionEntity
            {
                Id = "version-1",
                DocumentId = "doc",
                VersionNo = 1,
                Status = "ready",
                IndexStatus = "ready"
            },
            Chunks = new[]
            {
                new AssistantKnowledgeChunkEntity
                {
                    Id = "chunk-1",
                    DocumentVersionId = "version-1",
                    ChunkIndex = 0,
                    Content = "活动开始时间为 09:00。"
                }
            }
        };

        var sut = new AssistantKnowledgeRuntimeRetriever(new FixedKnowledgeRepository(), documents, new FixedEmbeddingProvider(), new FixedVectorStore());
        var result = await sut.RetrieveAsync(
            new AssistantScopeSnapshot("assistant", "published", AssistantExecutionMode.Public),
            new AssistantExecutionSnapshot(1, Policy(), "kb", Array.Empty<AssistantFrozenQuestion>(),
                new[] { new AssistantFrozenDocument("doc", "version-1", 1, "guide.md") }, KnowledgeEmbedding: EmbeddingProfile()),
            "活动开始时间", 5, CancellationToken.None);

        var match = Assert.Single(result);
        Assert.Equal("chunk-1", match.SegmentId);
        Assert.Equal("doc", match.DocumentId);
        Assert.Equal("活动开始时间为 09:00。", match.Text);
    }

    /// <summary>
    /// 验证版本标识不匹配的片段不会被检索返回。
    /// </summary>
    [Fact]
    public async Task Version_identity_mismatch_is_not_retrieved()
    {
        var documents = new FakeDocumentRepository
        {
            Document = new AssistantKnowledgeDocumentEntity
            {
                Id = "doc",
                KnowledgeBaseId = "kb",
                Status = "ready"
            },
            Version = new AssistantKnowledgeDocumentVersionEntity
            {
                Id = "different-version",
                DocumentId = "doc",
                VersionNo = 1,
                Status = "ready",
                IndexStatus = "ready"
            }
        };

        var sut = new AssistantKnowledgeRuntimeRetriever(
            new FixedKnowledgeRepository(),
            documents,
            new FixedEmbeddingProvider(),
            new FixedVectorStore());

        var result = await sut.RetrieveAsync(
            new AssistantScopeSnapshot("assistant", "published", AssistantExecutionMode.Public),
            new AssistantExecutionSnapshot(1, Policy(), "kb", Array.Empty<AssistantFrozenQuestion>(),
                new[] { new AssistantFrozenDocument("doc", "version-1", 1, "guide.md") }, KnowledgeEmbedding: EmbeddingProfile()),
            "query", 5, CancellationToken.None);

        Assert.Empty(result);
    }

    /// <summary>
    /// 校验业务策略设置中的范围、取值和相互依赖关系。
    /// </summary>
    private static AssistantResolvedPolicy Policy()
        => new("policy", 1, "business", "provider@1", "Provider", "model", "", 0.2, 100, true, 5, true, "无答案");

    /// <summary>
    /// 验证嵌入配置相关行为、预期结果及错误边界。
    /// </summary>
    private static AssistantKnowledgeEmbeddingExecutionProfile EmbeddingProfile()
        => new("provider", 1, "embedding", 4, 1, 1);

    /// <summary>
    /// 仅供助手知识运行时检索器测试使用的测试辅助类型。
    /// </summary>
    private sealed class FixedEmbeddingProvider : IAssistantEmbeddingProvider
    {
        /// <summary>
        /// 使用文本摘要生成确定性测试向量；该实现不提供语义检索嵌入。
        /// </summary>
        public Task<AssistantEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, AssistantKnowledgeEmbeddingExecutionProfile profile, CancellationToken ct)
            => Task.FromResult(new AssistantEmbeddingResult(new[] { new[] { 1f, 0f, 0f, 0f } }, profile.ProviderProfileRef, 4));
    }

    /// <summary>
    /// 测试用仓储替身，按预设数据实现所需查询并避免连接外部数据库。
    /// </summary>
    private sealed class FixedKnowledgeRepository : IAssistantKnowledgeRepository
    {
        /// <summary>
        /// 验证列出知识库相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantKnowledgeBaseEntity>> ListBasesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistantKnowledgeBaseEntity>>(Array.Empty<AssistantKnowledgeBaseEntity>());
        /// <summary>
        /// 验证获取知识库相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantKnowledgeBaseEntity?> GetBaseAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult<AssistantKnowledgeBaseEntity?>(new AssistantKnowledgeBaseEntity { Id = id });
        /// <summary>
        /// 验证添加知识库相关行为、预期结果及错误边界。
        /// </summary>
        public Task AddBaseAsync(AssistantKnowledgeBaseEntity row, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// 验证判断助手引用相关行为、预期结果及错误边界。
        /// </summary>
        public Task<bool> HasAssistantReferenceAsync(string knowledgeBaseId, CancellationToken cancellationToken)
            => Task.FromResult(false);
        /// <summary>
        /// 验证删除知识库相关行为、预期结果及错误边界。
        /// </summary>
        public Task DeleteBaseAsync(string id, CancellationToken cancellationToken)
            => Task.CompletedTask;
        /// <summary>
        /// 验证获取分块策略相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantKnowledgeChunkingPolicyEntity?> GetChunkingPolicyAsync(string knowledgeBaseId, CancellationToken cancellationToken)
            => Task.FromResult<AssistantKnowledgeChunkingPolicyEntity?>(null);
        /// <summary>
        /// 验证添加分块策略相关行为、预期结果及错误边界。
        /// </summary>
        public Task AddChunkingPolicyAsync(AssistantKnowledgeChunkingPolicyEntity row, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// 验证保存分块策略相关行为、预期结果及错误边界。
        /// </summary>
        public Task SaveChunkingPolicyAsync(AssistantKnowledgeChunkingPolicyEntity row, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// 验证列出固定问答相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantKnowledgeQnaEntity>> ListQnaAsync(string knowledgeBaseId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistantKnowledgeQnaEntity>>(Array.Empty<AssistantKnowledgeQnaEntity>());
        /// <summary>
        /// 验证获取固定问答相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantKnowledgeQnaEntity?> GetQnaAsync(string knowledgeBaseId, string id, CancellationToken cancellationToken)
            => Task.FromResult<AssistantKnowledgeQnaEntity?>(null);
        /// <summary>
        /// 验证获取固定问答问题相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantKnowledgeQnaEntity?> GetQnaByNormalizedQuestionAsync(string knowledgeBaseId, string normalizedQuestion, string? exceptId, CancellationToken cancellationToken)
            => Task.FromResult<AssistantKnowledgeQnaEntity?>(null);
        /// <summary>
        /// 验证查找启用回答相关行为、预期结果及错误边界。
        /// </summary>
        public Task<string?> FindEnabledAnswerAsync(string knowledgeBaseId, string normalizedQuestion, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
        /// <summary>
        /// 验证添加固定问答相关行为、预期结果及错误边界。
        /// </summary>
        public Task AddQnaAsync(AssistantKnowledgeQnaEntity row, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// 验证保存固定问答相关行为、预期结果及错误边界。
        /// </summary>
        public Task SaveQnaAsync(AssistantKnowledgeQnaEntity row, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// 验证删除固定问答相关行为、预期结果及错误边界。
        /// </summary>
        public Task DeleteQnaAsync(string knowledgeBaseId, string id, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// 仅供助手知识运行时检索器测试使用的测试辅助类型。
    /// </summary>
    private sealed class FixedVectorStore : IAssistantVectorIndexStore
    {
        /// <summary>
        /// 验证读取相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantVectorPointReadback>> ReadBackAsync(string collectionName,
            IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantVectorPointReadback>>(Array.Empty<AssistantVectorPointReadback>());

        /// <summary>
        /// 验证向量相关行为、预期结果及错误边界。
        /// </summary>
        public Task UpsertAsync(string collectionName, int dimension, IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct)
            => Task.CompletedTask;

        /// <summary>
        /// 验证检索相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantVectorSearchHit>> SearchAsync(string collectionName, ReadOnlyMemory<float> vector, int limit, CancellationToken ct, IReadOnlyCollection<string>? allowedChunkIds = null)
            => Task.FromResult<IReadOnlyList<AssistantVectorSearchHit>>(new[]
            {
                new AssistantVectorSearchHit("chunk-1", 0.9d, "doc", 1, 0)
            });
    }

    /// <summary>
    /// 测试用仓储替身，按预设数据实现所需查询并避免连接外部数据库。
    /// </summary>
    private sealed class FakeDocumentRepository : IAssistantKnowledgeDocumentRepository
    {
        /// <summary>
        /// 验证更新片段点相关行为、预期结果及错误边界。
        /// </summary>
        public Task UpdateChunkPointIdsAsync(IReadOnlyList<AssistantKnowledgeChunkEntity> rows,
            CancellationToken ct) => Task.CompletedTask;
        /// <summary>
        /// 表示文档仓储的文档信息。
        /// </summary>
        public AssistantKnowledgeDocumentEntity? Document { get; init; }
        /// <summary>
        /// 表示文档仓储的版本信息。
        /// </summary>
        public AssistantKnowledgeDocumentVersionEntity? Version { get; init; }
        /// <summary>
        /// 表示文档仓储的片段信息。
        /// </summary>
        public IReadOnlyList<AssistantKnowledgeChunkEntity> Chunks { get; init; } = Array.Empty<AssistantKnowledgeChunkEntity>();

        /// <summary>
        /// 验证列出文档相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantKnowledgeDocumentEntity>> ListDocumentsAsync(string knowledgeBaseId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantKnowledgeDocumentEntity>>(Document is null ? Array.Empty<AssistantKnowledgeDocumentEntity>() : new[] { Document });
        /// <summary>
        /// 验证获取文档相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantKnowledgeDocumentEntity?> GetDocumentAsync(string knowledgeBaseId, string documentId, CancellationToken ct)
            => Task.FromResult(Document);
        /// <summary>
        /// 验证添加文档相关行为、预期结果及错误边界。
        /// </summary>
        public Task AddDocumentAsync(AssistantKnowledgeDocumentEntity row, CancellationToken ct) => Task.CompletedTask;
        /// <summary>
        /// 验证更新文档相关行为、预期结果及错误边界。
        /// </summary>
        public Task UpdateDocumentAsync(AssistantKnowledgeDocumentEntity row, CancellationToken ct) => Task.CompletedTask;
        /// <summary>
        /// 验证添加版本相关行为、预期结果及错误边界。
        /// </summary>
        public Task AddVersionAsync(AssistantKnowledgeDocumentVersionEntity row, CancellationToken ct) => Task.CompletedTask;
        /// <summary>
        /// 验证更新版本相关行为、预期结果及错误边界。
        /// </summary>
        public Task UpdateVersionAsync(AssistantKnowledgeDocumentVersionEntity row, CancellationToken ct) => Task.CompletedTask;
        /// <summary>
        /// 验证获取版本相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantKnowledgeDocumentVersionEntity?> GetVersionAsync(string documentId, int versionNo, CancellationToken ct)
            => Task.FromResult(Version);
        /// <summary>
        /// 验证添加片段相关行为、预期结果及错误边界。
        /// </summary>
        public Task AddChunksAsync(IReadOnlyList<AssistantKnowledgeChunkEntity> rows, CancellationToken ct) => Task.CompletedTask;
        /// <summary>
        /// 验证列出片段相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantKnowledgeChunkEntity>> ListChunksAsync(string versionId, CancellationToken ct)
            => Task.FromResult(Chunks);
        /// <summary>
        /// 验证添加索引任务相关行为、预期结果及错误边界。
        /// </summary>
        public Task AddIndexJobAsync(AssistantKnowledgeIndexJobEntity row, CancellationToken ct) => Task.CompletedTask;
        /// <summary>
        /// 验证更新索引任务相关行为、预期结果及错误边界。
        /// </summary>
        public Task UpdateIndexJobAsync(AssistantKnowledgeIndexJobEntity row, CancellationToken ct) => Task.CompletedTask;
        /// <summary>
        /// 验证获取索引任务相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantKnowledgeIndexJobEntity?> GetIndexJobAsync(string knowledgeBaseId, string jobId, CancellationToken ct)
            => Task.FromResult<AssistantKnowledgeIndexJobEntity?>(null);
        /// <summary>
        /// 验证列出索引任务相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantKnowledgeIndexJobEntity>> ListIndexJobsAsync(string knowledgeBaseId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantKnowledgeIndexJobEntity>>(Array.Empty<AssistantKnowledgeIndexJobEntity>());
    }
}
