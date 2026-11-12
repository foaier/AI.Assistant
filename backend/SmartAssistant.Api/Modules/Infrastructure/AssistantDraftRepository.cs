#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Domain;
using SqlSugar;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 定义助手草稿的范围查询、幂等查询和带修订校验的写入操作。
/// </summary>
public interface IAssistantDraftRepository
{
    Task<IReadOnlyList<AssistantDraft>> ListAsync(string tenantId, string orgId, CancellationToken cancellationToken);
    Task<AssistantDraft?> GetAsync(string tenantId, string orgId, string id, CancellationToken cancellationToken);
    /// <summary>
    /// 在租户和组织范围内按创建幂等键读取已有草稿。
    /// </summary>
    Task<AssistantDraft?> GetByIdempotencyKeyAsync(string tenantId, string orgId, string key, CancellationToken cancellationToken);
    Task AddAsync(AssistantDraft draft, CancellationToken cancellationToken);
    /// <summary>
    /// 将带预期修订条件的草稿更新加入事务队列，未命中时报告并发冲突。
    /// </summary>
    Task SaveAsync(AssistantDraft draft, long expectedRevision, CancellationToken cancellationToken);
    Task DeleteAsync(string tenantId, string orgId, string id, string actorUserId, DateTime deletedAt, CancellationToken cancellationToken);
}

/// <summary>
/// 将助手草稿映射到数据库，并以预期修订号保护并发更新。
/// </summary>
public sealed class AssistantDraftRepository : IAssistantDraftRepository
{
    private readonly IDbContext _dbContext;

    public AssistantDraftRepository(IDbContext dbContext) => _dbContext = dbContext;

    public Task<AssistantDraft?> GetAsync(string tenantId, string orgId, string id, CancellationToken cancellationToken)
        => Query(scope => scope.Id == id && scope.TenantId == tenantId && scope.OrgId == orgId && !scope.IsDeleted, cancellationToken);

    public async Task<IReadOnlyList<AssistantDraft>> ListAsync(
        string tenantId,
        string orgId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<AssistantDraftRow>()
            .Where(scope => scope.TenantId == tenantId && scope.OrgId == orgId && !scope.IsDeleted)
            .OrderByDescending(scope => scope.UpdatedAt)
            .ToListAsync();
        return rows.Select(ToDomain).ToArray();
    }

    /// <summary>
    /// 在租户和组织范围内按创建幂等键读取已有草稿。
    /// </summary>
    public Task<AssistantDraft?> GetByIdempotencyKeyAsync(string tenantId, string orgId, string key, CancellationToken cancellationToken)
        => Query(scope => scope.CreationIdempotencyKey == key && scope.TenantId == tenantId && scope.OrgId == orgId && !scope.IsDeleted, cancellationToken);

    public Task AddAsync(AssistantDraft draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(draft)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 将带预期修订条件的草稿更新加入事务队列，未命中时报告并发冲突。
    /// </summary>
    public Task SaveAsync(AssistantDraft draft, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(async client =>
        {
            var affected = await client.Updateable(ToRow(draft))
                .Where(row => row.Id == draft.Id
                    && row.TenantId == draft.TenantId
                    && row.OrgId == draft.OrgId
                    && !row.IsDeleted
                    && row.DraftRevision == expectedRevision)
                .ExecuteCommandAsync();
            if (affected != 1)
                throw new DraftRevisionConflictException();
            return affected;
        });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string tenantId, string orgId, string id, string actorUserId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable<AssistantDraftRow>()
            .SetColumns(row => new AssistantDraftRow
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

    private async Task<AssistantDraft?> Query(Expression<Func<AssistantDraftRow, bool>> predicate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<AssistantDraftRow>()
            .Where(predicate)
            .FirstAsync();
        return row is null ? null : ToDomain(row);
    }

    private static AssistantDraftRow ToRow(AssistantDraft draft) => new()
    {
        Id = draft.Id,
        TenantId = draft.TenantId,
        OrgId = draft.OrgId,
        Name = draft.Name,
        Description = draft.Description,
        DraftJson = draft.DraftJson,
        DraftRevision = draft.DraftRevision,
        IsDeleted = draft.IsDeleted,
        DeletedAt = draft.DeletedAt,
        DeletedBy = draft.DeletedBy,
        CreatedAt = draft.CreatedAt,
        CreatedBy = draft.CreatedBy,
        UpdatedAt = draft.UpdatedAt,
        UpdatedBy = draft.UpdatedBy,
        CreationIdempotencyKey = draft.CreationIdempotencyKey
    };

    private static AssistantDraft ToDomain(AssistantDraftRow row)
    {
        return AssistantDraft.Rehydrate(row.Id, row.TenantId, row.OrgId, row.Name, row.Description,
            row.DraftJson, row.DraftRevision, row.IsDeleted, row.DeletedAt, row.DeletedBy,
            row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy, row.CreationIdempotencyKey);
    }

    /// <summary>
    /// 助手草稿的数据库行映射。
    /// </summary>
    [SugarTable("assistant_draft")]
    private sealed class AssistantDraftRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
        [SugarColumn(ColumnName = "draft_json")] public string DraftJson { get; set; } = "{}";
        [SugarColumn(ColumnName = "draft_revision")] public long DraftRevision { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at")] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by")] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "updated_at")] public DateTime UpdatedAt { get; set; }
        [SugarColumn(ColumnName = "updated_by")] public string UpdatedBy { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "creation_idempotency_key")] public string? CreationIdempotencyKey { get; set; }
    }

    /// <summary>
    /// 表示草稿持久化时预期修订号已失效。
    /// </summary>
    public sealed class DraftRevisionConflictException : Exception
    {
        public DraftRevisionConflictException() : base("assistant_draft_revision_conflict") { }
    }
}
