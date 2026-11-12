#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using Xunit;

namespace SmartAssistant.Tests;

// Spec: specs/136-public-assistant/spec.md, published execution snapshot runtime.
// Proof: runtime retrieval reads only the frozen document version; a mutable document
// status for a newer draft cannot hide the published ready version.
public sealed class AssistantKnowledgeRuntimeRetrieverTests
{
    [Fact]
    public async Task Processing_document_still_reads_frozen_ready_version()
    {
        var documents = new FakeDocumentRepository
        {
            Document = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow
            {
                Id = "doc", TenantId = "tenant", OrgId = "org", KnowledgeBaseId = "kb",
                FileName = "guide.md", Status = "processing", CurrentVersionNo = 2
            },
            Version = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow
            {
                Id = "version-1", TenantId = "tenant", OrgId = "org", DocumentId = "doc", VersionNo = 1,
                Status = "ready", IndexStatus = "ready"
            },
            Chunks = new[]
            {
                new AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow
                {
                    Id = "chunk-1", TenantId = "tenant", OrgId = "org", DocumentVersionId = "version-1",
                    ChunkIndex = 0, Content = "活动开始时间为 09:00。"
                }
            }
        };

        var sut = new AssistantKnowledgeRuntimeRetriever(new FixedKnowledgeRepository(), documents, new FixedEmbeddingProvider(), new FixedVectorStore());
        var result = await sut.RetrieveAsync(
            new AssistantScopeSnapshot("assistant", "tenant", "org", "published", AssistantExecutionMode.Public),
            new AssistantExecutionSnapshot(1, Policy(), "kb", Array.Empty<AssistantFrozenQuestion>(),
                new[] { new AssistantFrozenDocument("doc", "version-1", 1, "guide.md") }, KnowledgeEmbedding: EmbeddingProfile()),
            "活动开始时间", 5, CancellationToken.None);

        var match = Assert.Single(result);
        Assert.Equal("chunk-1", match.SegmentId);
        Assert.Equal("doc", match.DocumentId);
        Assert.Equal("活动开始时间为 09:00。", match.Text);
    }

    [Fact]
    public async Task Version_identity_mismatch_is_not_retrieved()
    {
        var documents = new FakeDocumentRepository
        {
            Document = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow
            {
                Id = "doc", TenantId = "tenant", OrgId = "org", KnowledgeBaseId = "kb", Status = "ready"
            },
            Version = new AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow
            {
                Id = "different-version", TenantId = "tenant", OrgId = "org", DocumentId = "doc", VersionNo = 1,
                Status = "ready", IndexStatus = "ready"
            }
        };

        var sut = new AssistantKnowledgeRuntimeRetriever(
            new FixedKnowledgeRepository(),
            documents,
            new FixedEmbeddingProvider(),
            new FixedVectorStore());

        var result = await sut.RetrieveAsync(
            new AssistantScopeSnapshot("assistant", "tenant", "org", "published", AssistantExecutionMode.Public),
            new AssistantExecutionSnapshot(1, Policy(), "kb", Array.Empty<AssistantFrozenQuestion>(),
                new[] { new AssistantFrozenDocument("doc", "version-1", 1, "guide.md") }, KnowledgeEmbedding: EmbeddingProfile()),
            "query", 5, CancellationToken.None);

        Assert.Empty(result);
    }

    private static AssistantResolvedPolicy Policy()
        => new("policy", 1, "business", "provider@1", "Provider", "model", "", 0.2, 100, true, 5, true, "无答案");

    private static AssistantKnowledgeEmbeddingExecutionProfile EmbeddingProfile()
        => new("provider", 1, "embedding", 4, 1, 1);

    private sealed class FixedEmbeddingProvider : IAssistantEmbeddingProvider
    {
        public Task<AssistantEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, string tenantId, string orgId,
            AssistantKnowledgeEmbeddingExecutionProfile profile, CancellationToken ct)
            => Task.FromResult(new AssistantEmbeddingResult(new[] { new[] { 1f, 0f, 0f, 0f } }, profile.ProviderProfileRef, 4));
    }

    private sealed class FixedKnowledgeRepository : IAssistantKnowledgeRepository
    {
        public Task<IReadOnlyList<AssistantKnowledgeRepository.AssistantKnowledgeBaseRow>> ListBasesAsync(string tenantId, string orgId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistantKnowledgeRepository.AssistantKnowledgeBaseRow>>(Array.Empty<AssistantKnowledgeRepository.AssistantKnowledgeBaseRow>());
        public Task<AssistantKnowledgeRepository.AssistantKnowledgeBaseRow?> GetBaseAsync(string tenantId, string orgId, string id, CancellationToken cancellationToken)
            => Task.FromResult<AssistantKnowledgeRepository.AssistantKnowledgeBaseRow?>(new AssistantKnowledgeRepository.AssistantKnowledgeBaseRow { Id = id, TenantId = tenantId, OrgId = orgId });
        public Task AddBaseAsync(AssistantKnowledgeRepository.AssistantKnowledgeBaseRow row, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> HasAssistantReferenceAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken)
            => Task.FromResult(false);
        public Task DeleteBaseAsync(string tenantId, string orgId, string id, string actorUserId, DateTime deletedAt, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task<AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow?> GetChunkingPolicyAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken)
            => Task.FromResult<AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow?>(null);
        public Task AddChunkingPolicyAsync(AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow row, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveChunkingPolicyAsync(AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow row, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow>> ListQnaAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow>>(Array.Empty<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow>());
        public Task<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow?> GetQnaAsync(string tenantId, string orgId, string knowledgeBaseId, string id, CancellationToken cancellationToken)
            => Task.FromResult<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow?>(null);
        public Task<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow?> GetQnaByNormalizedQuestionAsync(string tenantId, string orgId, string knowledgeBaseId, string normalizedQuestion, string? exceptId, CancellationToken cancellationToken)
            => Task.FromResult<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow?>(null);
        public Task<string?> FindEnabledAnswerAsync(string tenantId, string orgId, string knowledgeBaseId, string normalizedQuestion, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
        public Task AddQnaAsync(AssistantKnowledgeRepository.AssistantKnowledgeQnaRow row, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveQnaAsync(AssistantKnowledgeRepository.AssistantKnowledgeQnaRow row, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteQnaAsync(string tenantId, string orgId, string knowledgeBaseId, string id, string actorUserId, DateTime deletedAt, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FixedVectorStore : IAssistantVectorIndexStore
    {
        public Task<IReadOnlyList<AssistantVectorPointReadback>> ReadBackAsync(string collectionName,
            IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantVectorPointReadback>>(Array.Empty<AssistantVectorPointReadback>());

        public Task UpsertAsync(string collectionName, int dimension, IReadOnlyList<AssistantVectorChunk> chunks, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AssistantVectorSearchHit>> SearchAsync(string collectionName, ReadOnlyMemory<float> vector, int limit, CancellationToken ct, IReadOnlyCollection<string>? allowedChunkIds = null)
            => Task.FromResult<IReadOnlyList<AssistantVectorSearchHit>>(new[]
            {
                new AssistantVectorSearchHit("chunk-1", 0.9d, "doc", 1, 0)
            });
    }

    private sealed class FakeDocumentRepository : IAssistantKnowledgeDocumentRepository
    {
        public Task UpdateChunkPointIdsAsync(IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow> rows,
            CancellationToken ct) => Task.CompletedTask;
        public AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow? Document { get; init; }
        public AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow? Version { get; init; }
        public IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow> Chunks { get; init; } = Array.Empty<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow>();

        public Task<IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow>> ListDocumentsAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow>>(Document is null ? Array.Empty<AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow>() : new[] { Document });
        public Task<AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow?> GetDocumentAsync(string tenantId, string orgId, string knowledgeBaseId, string documentId, CancellationToken ct)
            => Task.FromResult(Document);
        public Task AddDocumentAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow row, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateDocumentAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow row, CancellationToken ct) => Task.CompletedTask;
        public Task AddVersionAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow row, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateVersionAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow row, CancellationToken ct) => Task.CompletedTask;
        public Task<AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow?> GetVersionAsync(string tenantId, string orgId, string documentId, int versionNo, CancellationToken ct)
            => Task.FromResult(Version);
        public Task AddChunksAsync(IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow> rows, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow>> ListChunksAsync(string tenantId, string orgId, string versionId, CancellationToken ct)
            => Task.FromResult(Chunks);
        public Task AddIndexJobAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow row, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateIndexJobAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow row, CancellationToken ct) => Task.CompletedTask;
        public Task<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow?> GetIndexJobAsync(string tenantId, string orgId, string knowledgeBaseId, string jobId, CancellationToken ct)
            => Task.FromResult<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow?>(null);
        public Task<IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow>> ListIndexJobsAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow>>(Array.Empty<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow>());
    }
}
