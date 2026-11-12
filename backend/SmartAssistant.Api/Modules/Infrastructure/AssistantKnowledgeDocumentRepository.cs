#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Extensions.DAL;
using SqlSugar;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 定义文档、版本、片段和索引任务的持久化操作。
/// </summary>
public interface IAssistantKnowledgeDocumentRepository
{
    Task<IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow>> ListDocumentsAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken ct);
    Task<AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow?> GetDocumentAsync(string tenantId, string orgId, string knowledgeBaseId, string documentId, CancellationToken ct);
    Task AddDocumentAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow row, CancellationToken ct);
    Task UpdateDocumentAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentRow row, CancellationToken ct);
    Task AddVersionAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow row, CancellationToken ct);
    Task UpdateVersionAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow row, CancellationToken ct);
    Task<AssistantKnowledgeDocumentRepository.AssistantKnowledgeDocumentVersionRow?> GetVersionAsync(string tenantId, string orgId, string documentId, int versionNo, CancellationToken ct);
    Task AddChunksAsync(IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow> rows, CancellationToken ct);
    Task UpdateChunkPointIdsAsync(IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow> rows, CancellationToken ct);
    Task<IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeChunkRow>> ListChunksAsync(string tenantId, string orgId, string versionId, CancellationToken ct);
    Task AddIndexJobAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow row, CancellationToken ct);
    Task UpdateIndexJobAsync(AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow row, CancellationToken ct);
    Task<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow?> GetIndexJobAsync(string tenantId, string orgId, string knowledgeBaseId, string jobId, CancellationToken ct);
    Task<IReadOnlyList<AssistantKnowledgeDocumentRepository.AssistantKnowledgeIndexJobRow>> ListIndexJobsAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken ct);
}

/// <summary>
/// 保存知识文档及其版本、片段和索引任务，并按租户与组织隔离查询。
/// </summary>
public sealed class AssistantKnowledgeDocumentRepository : IAssistantKnowledgeDocumentRepository
{
    private readonly IDbContext _dbContext;

    public AssistantKnowledgeDocumentRepository(IDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<AssistantKnowledgeDocumentRow>> ListDocumentsAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeDocumentRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.KnowledgeBaseId == knowledgeBaseId && !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync();
    }

    public async Task<AssistantKnowledgeDocumentRow?> GetDocumentAsync(string tenantId, string orgId, string knowledgeBaseId, string documentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeDocumentRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.KnowledgeBaseId == knowledgeBaseId && x.Id == documentId && !x.IsDeleted)
            .FirstAsync();
    }

    public Task AddDocumentAsync(AssistantKnowledgeDocumentRow row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task UpdateDocumentAsync(AssistantKnowledgeDocumentRow row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id && x.TenantId == row.TenantId && x.OrgId == row.OrgId && !x.IsDeleted)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task AddVersionAsync(AssistantKnowledgeDocumentVersionRow row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task UpdateVersionAsync(AssistantKnowledgeDocumentVersionRow row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id && x.TenantId == row.TenantId && x.OrgId == row.OrgId && !x.IsDeleted)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<AssistantKnowledgeDocumentVersionRow?> GetVersionAsync(string tenantId, string orgId, string documentId, int versionNo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeDocumentVersionRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.DocumentId == documentId && x.VersionNo == versionNo && !x.IsDeleted)
            .FirstAsync();
    }

    public Task AddChunksAsync(IReadOnlyList<AssistantKnowledgeChunkRow> rows, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (rows.Count > 0)
            _dbContext.AddCommand(client => client.Insertable(rows.ToList()).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task UpdateChunkPointIdsAsync(IReadOnlyList<AssistantKnowledgeChunkRow> rows, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (rows.Count > 0)
            _dbContext.AddCommand(async client =>
            {
                var affected = 0;
                foreach (var row in rows)
                    affected += await client.Updateable(row)
                        .Where(value => value.Id == row.Id && value.TenantId == row.TenantId && value.OrgId == row.OrgId
                            && value.DocumentVersionId == row.DocumentVersionId && !value.IsDeleted)
                        .ExecuteCommandAsync();
                if (affected != rows.Count)
                    throw new BizResultException(BusinessCodes.Conflict, "knowledge_chunk_point_link_conflict");
                return affected;
            });
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AssistantKnowledgeChunkRow>> ListChunksAsync(string tenantId, string orgId, string versionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeChunkRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.DocumentVersionId == versionId && !x.IsDeleted)
            .OrderBy(x => x.ChunkIndex).ToListAsync();
    }

    public Task AddIndexJobAsync(AssistantKnowledgeIndexJobRow row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task UpdateIndexJobAsync(AssistantKnowledgeIndexJobRow row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id && x.TenantId == row.TenantId && x.OrgId == row.OrgId)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<AssistantKnowledgeIndexJobRow?> GetIndexJobAsync(string tenantId, string orgId, string knowledgeBaseId, string jobId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeIndexJobRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.KnowledgeBaseId == knowledgeBaseId && x.Id == jobId)
            .FirstAsync();
    }

    public async Task<IReadOnlyList<AssistantKnowledgeIndexJobRow>> ListIndexJobsAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeIndexJobRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.KnowledgeBaseId == knowledgeBaseId)
            .OrderByDescending(x => x.CreatedAt).ToListAsync();
    }

    /// <summary>
    /// 知识文档的数据库主记录。
    /// </summary>
    [SugarTable("assistant_knowledge_document")]
    public sealed class AssistantKnowledgeDocumentRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "knowledge_base_id")] public string KnowledgeBaseId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "asset_id")] public string AssetId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "file_name")] public string FileName { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "content_type")] public string ContentType { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "size_bytes")] public long SizeBytes { get; set; }
        [SugarColumn(ColumnName = "current_version_no")] public int CurrentVersionNo { get; set; }
        [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "uploaded";
        [SugarColumn(ColumnName = "last_error_code", IsNullable = true)] public string? LastErrorCode { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "updated_at")] public DateTime UpdatedAt { get; set; }
        [SugarColumn(ColumnName = "updated_by")] public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 知识文档某次解析版本的数据库记录。
    /// </summary>
    [SugarTable("assistant_knowledge_document_version")]
    public sealed class AssistantKnowledgeDocumentVersionRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "document_id")] public string DocumentId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "version_no")] public int VersionNo { get; set; }
        [SugarColumn(ColumnName = "content_hash")] public string ContentHash { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "parser_version")] public string ParserVersion { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "chunk_strategy")] public string ChunkStrategy { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "chunk_policy_json", ColumnDataType = "longtext")] public string ChunkPolicyJson { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "uploaded";
        [SugarColumn(ColumnName = "chunk_count")] public int ChunkCount { get; set; }
        [SugarColumn(ColumnName = "index_status")] public string IndexStatus { get; set; } = "queued";
        [SugarColumn(ColumnName = "indexed_at", IsNullable = true)] public DateTime? IndexedAt { get; set; }
        [SugarColumn(ColumnName = "error_code", IsNullable = true)] public string? ErrorCode { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 文档版本中的文本片段及其索引关联记录。
    /// </summary>
    [SugarTable("assistant_knowledge_chunk")]
    public sealed class AssistantKnowledgeChunkRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "document_version_id")] public string DocumentVersionId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "chunk_index")] public int ChunkIndex { get; set; }
        [SugarColumn(ColumnName = "content_hash")] public string ContentHash { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "content", ColumnDataType = "longtext")] public string Content { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "token_count")] public int TokenCount { get; set; }
        [SugarColumn(ColumnName = "point_id", IsNullable = true)] public string? PointId { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 冻结嵌入配置与文档版本的索引任务记录。
    /// </summary>
    [SugarTable("assistant_knowledge_index_job")]
    public sealed class AssistantKnowledgeIndexJobRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "knowledge_base_id")] public string KnowledgeBaseId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "document_id")] public string DocumentId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "document_version_id")] public string DocumentVersionId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "version_no")] public int VersionNo { get; set; }
        [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "queued";
        [SugarColumn(ColumnName = "attempt_count")] public int AttemptCount { get; set; }
        [SugarColumn(ColumnName = "chunk_count")] public int ChunkCount { get; set; }
        [SugarColumn(ColumnName = "error_code", IsNullable = true)] public string? ErrorCode { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "started_at", IsNullable = true)] public DateTime? StartedAt { get; set; }
        [SugarColumn(ColumnName = "completed_at", IsNullable = true)] public DateTime? CompletedAt { get; set; }
        [SugarColumn(ColumnName = "generation", IsNullable = true)] public long? Generation { get; set; }
        [SugarColumn(ColumnName = "embedding_provider_id", IsNullable = true)] public string? EmbeddingProviderId { get; set; }
        [SugarColumn(ColumnName = "embedding_provider_revision", IsNullable = true)] public long? EmbeddingProviderRevision { get; set; }
        [SugarColumn(ColumnName = "embedding_model", IsNullable = true)] public string? EmbeddingModel { get; set; }
        [SugarColumn(ColumnName = "embedding_dimension", IsNullable = true)] public int? EmbeddingDimension { get; set; }
        [SugarColumn(ColumnName = "chunk_policy_revision", IsNullable = true)] public long? ChunkPolicyRevision { get; set; }
        [SugarColumn(ColumnName = "chunk_policy_json", ColumnDataType = "longtext", IsNullable = true)] public string? ChunkPolicyJson { get; set; }
        [SugarColumn(ColumnName = "parser_version", IsNullable = true)] public string? ParserVersion { get; set; }
        [SugarColumn(ColumnName = "content_hash", IsNullable = true)] public string? ContentHash { get; set; }
    }
}
