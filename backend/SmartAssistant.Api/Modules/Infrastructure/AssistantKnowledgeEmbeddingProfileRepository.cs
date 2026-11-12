#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SqlSugar;
using SmartAssistant.Extensions.Common;
using AssistantKnowledgeEmbeddingProfileRow = SmartAssistant.Modules.Infrastructure.AssistantKnowledgeEmbeddingProfileRepository.AssistantKnowledgeEmbeddingProfileRow;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 定义知识库嵌入配置的修订保存与生效代次提升操作。
/// </summary>
public interface IAssistantKnowledgeEmbeddingProfileRepository
{
    Task<AssistantKnowledgeEmbeddingProfileRow?> GetAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken ct);
    Task AddAsync(AssistantKnowledgeEmbeddingProfileRow row, CancellationToken ct);
    Task SaveAsync(AssistantKnowledgeEmbeddingProfileRow row, long expectedRevision, CancellationToken ct);
    /// <summary>
    /// 仅在修订号与目标代次仍匹配时更新生效嵌入配置，防止旧任务覆盖新配置。
    /// </summary>
    Task TryPromoteAsync(AssistantKnowledgeEmbeddingProfileRow row, long expectedRevision, long expectedTargetGeneration, CancellationToken ct);
}

/// <summary>
/// 持久化目标与生效嵌入配置，并通过条件更新保护代次切换。
/// </summary>
public sealed class AssistantKnowledgeEmbeddingProfileRepository : IAssistantKnowledgeEmbeddingProfileRepository
{
    private readonly IDbContext _db;

    public AssistantKnowledgeEmbeddingProfileRepository(IDbContext db) => _db = db;

    public async Task<AssistantKnowledgeEmbeddingProfileRow?> GetAsync(string tenantId, string orgId, string knowledgeBaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.GetSugarScope(ConnectionType.Master).Queryable<AssistantKnowledgeEmbeddingProfileRow>()
            .InnerJoin<AssistantKnowledgeRepository.AssistantKnowledgeBaseRow>((profile, basis) =>
                profile.KnowledgeBaseId == basis.Id && profile.TenantId == basis.TenantId && profile.OrgId == basis.OrgId && !basis.IsDeleted)
            .Where((profile, basis) => profile.TenantId == tenantId && profile.OrgId == orgId
                && profile.KnowledgeBaseId == knowledgeBaseId && !profile.IsDeleted && !basis.IsDeleted)
            .Select((profile, basis) => profile)
            .FirstAsync();
    }

    public Task AddAsync(AssistantKnowledgeEmbeddingProfileRow row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task SaveAsync(AssistantKnowledgeEmbeddingProfileRow row, long expectedRevision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.AddCommand(async client =>
        {
            var affected = await client.Updateable(row)
                .Where(value => value.Id == row.Id && value.TenantId == row.TenantId && value.OrgId == row.OrgId
                    && value.KnowledgeBaseId == row.KnowledgeBaseId && !value.IsDeleted && value.Revision == expectedRevision)
                .ExecuteCommandAsync();
            if (affected != 1)
                throw new BizResultException(BusinessCodes.Conflict, "knowledge_embedding_profile_revision_conflict");
            return affected;
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 仅在修订号与目标代次仍匹配时更新生效嵌入配置，防止旧任务覆盖新配置。
    /// </summary>
    public Task TryPromoteAsync(AssistantKnowledgeEmbeddingProfileRow row, long expectedRevision,
        long expectedTargetGeneration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.AddCommand(async client =>
        {
            return await client.Updateable(row)
                .Where(value => value.Id == row.Id && value.TenantId == row.TenantId && value.OrgId == row.OrgId
                    && value.KnowledgeBaseId == row.KnowledgeBaseId && !value.IsDeleted
                    && value.Revision == expectedRevision && value.TargetGeneration == expectedTargetGeneration)
                .ExecuteCommandAsync() == 1 ? 1
                : throw new BizResultException(BusinessCodes.Conflict, "knowledge_embedding_profile_revision_conflict");
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 知识库嵌入配置的数据库记录，区分目标代次与生效代次。
    /// </summary>
    [SugarTable("assistant_knowledge_embedding_policy")]
    public sealed class AssistantKnowledgeEmbeddingProfileRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "knowledge_base_id")] public string KnowledgeBaseId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "provider_id")] public string ProviderId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "provider_revision")] public long ProviderRevision { get; set; }
        [SugarColumn(ColumnName = "model")] public string Model { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "dimension")] public int Dimension { get; set; }
        /// <summary>
        /// 正在构建或计划构建的索引代次，与已生效代次分别维护。
        /// </summary>
        [SugarColumn(ColumnName = "target_generation")] public long TargetGeneration { get; set; }
        [SugarColumn(ColumnName = "active_provider_id", IsNullable = true)] public string? ActiveProviderId { get; set; }
        [SugarColumn(ColumnName = "active_provider_revision", IsNullable = true)] public long? ActiveProviderRevision { get; set; }
        [SugarColumn(ColumnName = "active_model", IsNullable = true)] public string? ActiveModel { get; set; }
        [SugarColumn(ColumnName = "active_dimension", IsNullable = true)] public int? ActiveDimension { get; set; }
        /// <summary>
        /// 已完成索引并可供发布快照引用的生效代次。
        /// </summary>
        [SugarColumn(ColumnName = "active_generation")] public long ActiveGeneration { get; set; }
        [SugarColumn(ColumnName = "active_profile_revision", IsNullable = true)] public long? ActiveProfileRevision { get; set; }
        [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "rebuilding";
        [SugarColumn(ColumnName = "last_error_code", IsNullable = true)] public string? LastErrorCode { get; set; }
        [SugarColumn(ColumnName = "revision")] public long Revision { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "updated_at")] public DateTime UpdatedAt { get; set; }
        [SugarColumn(ColumnName = "updated_by")] public string UpdatedBy { get; set; } = string.Empty;
    }
}
