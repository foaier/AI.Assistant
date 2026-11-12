#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Contracts;
using SqlSugar;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 定义知识库、固定问答、分块策略及助手引用检查的持久化边界。
/// </summary>
public interface IAssistantKnowledgeRepository
{
    Task<IReadOnlyList<AssistantKnowledgeRepository.AssistantKnowledgeBaseRow>> ListBasesAsync(string tenantId, string orgId, CancellationToken cancellationToken);
    Task<AssistantKnowledgeRepository.AssistantKnowledgeBaseRow?> GetBaseAsync(string tenantId, string orgId, string id, CancellationToken cancellationToken);
    Task AddBaseAsync(AssistantKnowledgeRepository.AssistantKnowledgeBaseRow row, CancellationToken cancellationToken);
    Task<bool> HasAssistantReferenceAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken);
    Task DeleteBaseAsync(string tenantId, string orgId, string id, string actorUserId, DateTime deletedAt, CancellationToken cancellationToken);
    Task<AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow?> GetChunkingPolicyAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken);
    Task AddChunkingPolicyAsync(AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow row, CancellationToken cancellationToken);
    Task SaveChunkingPolicyAsync(AssistantKnowledgeRepository.AssistantKnowledgeChunkingPolicyRow row, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow>> ListQnaAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken);
    Task<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow?> GetQnaAsync(string tenantId, string orgId, string knowledgeBaseId, string id, CancellationToken cancellationToken);
    Task<AssistantKnowledgeRepository.AssistantKnowledgeQnaRow?> GetQnaByNormalizedQuestionAsync(string tenantId, string orgId, string knowledgeBaseId, string normalizedQuestion, string? exceptId, CancellationToken cancellationToken);
    Task<string?> FindEnabledAnswerAsync(string tenantId, string orgId, string knowledgeBaseId, string normalizedQuestion, CancellationToken cancellationToken);
    Task AddQnaAsync(AssistantKnowledgeRepository.AssistantKnowledgeQnaRow row, CancellationToken cancellationToken);
    Task SaveQnaAsync(AssistantKnowledgeRepository.AssistantKnowledgeQnaRow row, CancellationToken cancellationToken);
    Task DeleteQnaAsync(string tenantId, string orgId, string knowledgeBaseId, string id, string actorUserId, DateTime deletedAt, CancellationToken cancellationToken);
}

/// <summary>
/// 实现知识库与固定问答持久化，并检查删除前的助手绑定关系。
/// </summary>
public sealed class AssistantKnowledgeRepository : IAssistantKnowledgeRepository
{
    private readonly IDbContext _dbContext;

    public AssistantKnowledgeRepository(IDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<AssistantKnowledgeBaseRow>> ListBasesAsync(string tenantId, string orgId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeBaseRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync();
    }

    public async Task<AssistantKnowledgeBaseRow?> GetBaseAsync(string tenantId, string orgId, string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeBaseRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.Id == id && !x.IsDeleted).FirstAsync();
    }

    public Task AddBaseAsync(AssistantKnowledgeBaseRow row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<bool> HasAssistantReferenceAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<AssistantDraftReferenceRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && !x.IsDeleted)
            .Select(x => x.DraftJson)
            .ToListAsync();
        foreach (var json in rows)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (ReferencesKnowledgeBase(document.RootElement, knowledgeBaseId))
                    return true;
            }
            catch (JsonException)
            {
                /*
                 * 新写入的草稿已校验 JSON；引用检查忽略历史损坏记录。
                 * 正常读取草稿时仍会暴露原有配置错误。
                 */
            }
        }

        var publishedSnapshots = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<AssistantPublishedVersionReferenceRow>()
            .InnerJoin<AssistantPublicationPointerReferenceRow>((version, pointer) =>
                version.Id == pointer.CurrentPublishedVersionId
                && version.AssistantId == pointer.AssistantId
                && version.TenantId == pointer.TenantId
                && version.OrgId == pointer.OrgId
                && pointer.Status == 1
                && !pointer.IsDeleted)
            .Where((version, pointer) =>
                version.TenantId == tenantId
                && version.OrgId == orgId
                && !version.IsDeleted
                && !version.IsRevoked)
            .Select((version, pointer) => version.SnapshotJson)
            .ToListAsync();
        foreach (var json in publishedSnapshots)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (ReferencesKnowledgeBase(document.RootElement, knowledgeBaseId))
                    return true;
            }
            catch (JsonException)
            {
                /*
                 * 格式损坏的历史快照无法通过正常发布流程生效，检查当前引用时忽略该记录。
                 */
            }
        }

        return false;
    }

    private static bool ReferencesKnowledgeBase(JsonElement value, string knowledgeBaseId)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (string.Equals(property.Name, "knowledgeBaseId", StringComparison.Ordinal)
                        && property.Value.ValueKind == JsonValueKind.String
                        && string.Equals(property.Value.GetString(), knowledgeBaseId, StringComparison.Ordinal))
                        return true;
                    if (ReferencesKnowledgeBase(property.Value, knowledgeBaseId))
                        return true;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (ReferencesKnowledgeBase(item, knowledgeBaseId))
                        return true;
                }
                break;
        }

        return false;
    }

    public Task DeleteBaseAsync(string tenantId, string orgId, string id, string actorUserId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable<AssistantKnowledgeBaseRow>()
            .SetColumns(row => new AssistantKnowledgeBaseRow
            {
                IsDeleted = true,
                DeletedAt = deletedAt,
                DeletedBy = actorUserId,
                UpdatedAt = deletedAt,
                UpdatedBy = actorUserId
            })
            .Where(row => row.Id == id && row.TenantId == tenantId && row.OrgId == orgId && !row.IsDeleted)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<AssistantKnowledgeChunkingPolicyRow?> GetChunkingPolicyAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeChunkingPolicyRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.KnowledgeBaseId == knowledgeBaseId && !x.IsDeleted)
            .FirstAsync();
    }

    public Task AddChunkingPolicyAsync(AssistantKnowledgeChunkingPolicyRow row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task SaveChunkingPolicyAsync(AssistantKnowledgeChunkingPolicyRow row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id && x.TenantId == row.TenantId && x.OrgId == row.OrgId && !x.IsDeleted)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AssistantKnowledgeQnaRow>> ListQnaAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeQnaRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.KnowledgeBaseId == knowledgeBaseId && !x.IsDeleted)
            .OrderBy(x => x.SortOrder).OrderBy(x => x.Question).ToListAsync();
    }

    public async Task<AssistantKnowledgeQnaRow?> GetQnaAsync(string tenantId, string orgId, string knowledgeBaseId, string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeQnaRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.KnowledgeBaseId == knowledgeBaseId && x.Id == id && !x.IsDeleted).FirstAsync();
    }

    public async Task<AssistantKnowledgeQnaRow?> GetQnaByNormalizedQuestionAsync(string tenantId, string orgId, string knowledgeBaseId, string normalizedQuestion, string? exceptId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeQnaRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.KnowledgeBaseId == knowledgeBaseId
                && x.NormalizedQuestion == normalizedQuestion && !x.IsDeleted
            && (exceptId == null || x.Id != exceptId)).FirstAsync();
    }

    public async Task<string?> FindEnabledAnswerAsync(string tenantId, string orgId, string knowledgeBaseId, string normalizedQuestion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeQnaRow>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.KnowledgeBaseId == knowledgeBaseId
                && x.NormalizedQuestion == normalizedQuestion && x.Enabled && !x.IsDeleted)
            .FirstAsync();
        return string.IsNullOrWhiteSpace(row?.Answer) ? null : row.Answer;
    }

    public Task AddQnaAsync(AssistantKnowledgeQnaRow row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task SaveQnaAsync(AssistantKnowledgeQnaRow row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id && x.TenantId == row.TenantId && x.OrgId == row.OrgId && !x.IsDeleted)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task DeleteQnaAsync(string tenantId, string orgId, string knowledgeBaseId, string id, string actorUserId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable<AssistantKnowledgeQnaRow>()
            .SetColumns(row => new AssistantKnowledgeQnaRow
            {
                IsDeleted = true,
                DeletedAt = deletedAt,
                DeletedBy = actorUserId,
                UpdatedAt = deletedAt,
                UpdatedBy = actorUserId
            })
            .Where(row => row.Id == id && row.TenantId == tenantId && row.OrgId == orgId
                && row.KnowledgeBaseId == knowledgeBaseId && !row.IsDeleted)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 知识库的数据库记录，包含所属范围和审计信息。
    /// </summary>
    [SugarTable("assistant_knowledge_base")]
    public sealed class AssistantKnowledgeBaseRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "updated_at")] public DateTime UpdatedAt { get; set; }
        [SugarColumn(ColumnName = "updated_by")] public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 知识库分块策略与修订号的数据库记录。
    /// </summary>
    [SugarTable("assistant_knowledge_chunking_policy")]
    public sealed class AssistantKnowledgeChunkingPolicyRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "knowledge_base_id")] public string KnowledgeBaseId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "mode")] public string Mode { get; set; } = "hybrid";
        [SugarColumn(ColumnName = "max_chunk_size")] public int MaxChunkSize { get; set; }
        [SugarColumn(ColumnName = "min_chunk_size")] public int MinChunkSize { get; set; }
        [SugarColumn(ColumnName = "overlap_size")] public int OverlapSize { get; set; }
        [SugarColumn(ColumnName = "max_heading_level")] public int MaxHeadingLevel { get; set; }
        [SugarColumn(ColumnName = "preserve_heading_path")] public bool PreserveHeadingPath { get; set; }
        [SugarColumn(ColumnName = "merge_short_chunks")] public bool MergeShortChunks { get; set; }
        [SugarColumn(ColumnName = "revision")] public long Revision { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "updated_at")] public DateTime UpdatedAt { get; set; }
        [SugarColumn(ColumnName = "updated_by")] public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 固定问答的数据库记录，包含标准化问题和展示设置。
    /// </summary>
    [SugarTable("assistant_knowledge_qna")]
    public sealed class AssistantKnowledgeQnaRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "knowledge_base_id")] public string KnowledgeBaseId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "question")] public string Question { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "normalized_question")] public string NormalizedQuestion { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "answer")] public string? Answer { get; set; }
        [SugarColumn(ColumnName = "enabled")] public bool Enabled { get; set; }
        [SugarColumn(ColumnName = "sort_order")] public int SortOrder { get; set; }
        [SugarColumn(ColumnName = "show_as_suggestion")] public bool ShowAsSuggestion { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "updated_at")] public DateTime UpdatedAt { get; set; }
        [SugarColumn(ColumnName = "updated_by")] public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 查询草稿知识库引用时使用的数据库投影。
    /// </summary>
    [SugarTable("assistant_draft")]
    private sealed class AssistantDraftReferenceRow
    {
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "draft_json")] public string DraftJson { get; set; } = "{}";
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    }

    /// <summary>
    /// 查询发布版本知识库引用时使用的数据库投影。
    /// </summary>
    [SugarTable("assistant_published_version")]
    private sealed class AssistantPublishedVersionReferenceRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "assistant_id")] public string AssistantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "snapshot_json", ColumnDataType = "json")] public string SnapshotJson { get; set; } = "{}";
        [SugarColumn(ColumnName = "is_revoked")] public bool IsRevoked { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    }

    /// <summary>
    /// 查询有效发布指针时使用的数据库投影。
    /// </summary>
    [SugarTable("assistant_publication_pointer")]
    private sealed class AssistantPublicationPointerReferenceRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "assistant_id")] public string AssistantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "current_published_version_id", IsNullable = true)] public string? CurrentPublishedVersionId { get; set; }
        [SugarColumn(ColumnName = "status")] public int Status { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    }
}
