#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Contracts;
using SqlSugar;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 定义发布快照、公开标识及带修订保护的发布指针操作。
/// </summary>
public interface IAssistantPublicationRepository
{
    Task<AssistantPublicationPointer?> GetPointerAsync(
        string tenantId,
        string orgId,
        string assistantId,
        CancellationToken cancellationToken);

    Task<AssistantPublishedVersion?> GetVersionAsync(
        string tenantId,
        string orgId,
        string versionId,
        CancellationToken cancellationToken);

    Task<(AssistantPublicationPointer Pointer, AssistantPublishedVersion Version)?>
        GetCurrentByAssistantKeyAsync(string assistantKey, CancellationToken cancellationToken);

    Task AddVersionAsync(AssistantPublishedVersion version, CancellationToken cancellationToken);
    Task AddPointerAsync(AssistantPublicationPointer pointer, CancellationToken cancellationToken);
    Task SavePointerAsync(
        AssistantPublicationPointer pointer,
        long expectedRevision,
        CancellationToken cancellationToken);
    Task SaveVersionAsync(AssistantPublishedVersion version, CancellationToken cancellationToken);
}

/// <summary>
/// 保存冻结的发布版本，并通过条件写入保护版本发布和撤回。
/// </summary>
public sealed class AssistantPublicationRepository : IAssistantPublicationRepository
{
    private readonly IDbContext _dbContext;

    public AssistantPublicationRepository(IDbContext dbContext) => _dbContext = dbContext;

    public async Task<AssistantPublicationPointer?> GetPointerAsync(
        string tenantId,
        string orgId,
        string assistantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<AssistantPublicationPointerRow>()
            .Where(value => value.TenantId == tenantId
                && value.OrgId == orgId
                && value.AssistantId == assistantId
                && !value.IsDeleted)
            .FirstAsync();
        return row is null ? null : ToDomain(row);
    }

    public async Task<AssistantPublishedVersion?> GetVersionAsync(
        string tenantId,
        string orgId,
        string versionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<AssistantPublishedVersionRow>()
            .Where(value => value.TenantId == tenantId
                && value.OrgId == orgId
                && value.Id == versionId
                && !value.IsDeleted)
            .FirstAsync();
        return row is null ? null : ToDomain(row);
    }

    public async Task<(AssistantPublicationPointer Pointer, AssistantPublishedVersion Version)?>
        GetCurrentByAssistantKeyAsync(string assistantKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = _dbContext.GetSugarScope(ConnectionType.Master);
        var pointerRow = await scope.Queryable<AssistantPublicationPointerRow>()
            .Where(value => value.AssistantKey == assistantKey
                && !value.IsDeleted
                && value.Status == (int)AssistantPublicationPointerStatus.Active
                && value.CurrentPublishedVersionId != null)
            .FirstAsync();
        if (pointerRow is null || string.IsNullOrWhiteSpace(pointerRow.CurrentPublishedVersionId))
            return null;

        var versionRow = await scope.Queryable<AssistantPublishedVersionRow>()
            .Where(value => value.Id == pointerRow.CurrentPublishedVersionId
                && value.AssistantId == pointerRow.AssistantId
                && value.AssistantKey == pointerRow.AssistantKey
                && value.TenantId == pointerRow.TenantId
                && value.OrgId == pointerRow.OrgId
                && !value.IsDeleted
                && !value.IsRevoked)
            .FirstAsync();
        if (versionRow is null)
            return null;

        return (ToDomain(pointerRow), ToDomain(versionRow));
    }

    public Task AddVersionAsync(AssistantPublishedVersion version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(version)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task AddPointerAsync(AssistantPublicationPointer pointer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(pointer)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task SavePointerAsync(
        AssistantPublicationPointer pointer,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(async client =>
        {
            var affected = await client.Updateable(ToRow(pointer))
                .Where(row => row.Id == pointer.Id
                    && row.TenantId == pointer.TenantId
                    && row.OrgId == pointer.OrgId
                    && !row.IsDeleted
                    && row.PointerRevision == expectedRevision)
                .ExecuteCommandAsync();
            if (affected != 1)
                throw new PointerRevisionConflictException();
            return affected;
        });
        return Task.CompletedTask;
    }

    public Task SaveVersionAsync(AssistantPublishedVersion version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(async client =>
        {
            var affected = await client.Updateable(ToRow(version))
                .Where(row => row.Id == version.Id
                    && row.TenantId == version.TenantId
                    && row.OrgId == version.OrgId
                    && !row.IsDeleted
                    && !row.IsRevoked)
                .ExecuteCommandAsync();
            if (affected != 1)
                throw new VersionRevokeConflictException();
            return affected;
        });
        return Task.CompletedTask;
    }

    private static AssistantPublicationPointerRow ToRow(AssistantPublicationPointer value) => new()
    {
        Id = value.Id,
        AssistantKey = value.AssistantKey,
        AssistantId = value.AssistantId,
        TenantId = value.TenantId,
        OrgId = value.OrgId,
        CurrentPublishedVersionId = value.CurrentPublishedVersionId,
        CurrentVersionNo = value.CurrentVersionNo,
        PointerRevision = value.PointerRevision,
        Status = (int)value.Status,
        IsDeleted = value.IsDeleted,
        DeletedAt = value.DeletedAt,
        DeletedBy = value.DeletedBy,
        CreatedAt = value.CreatedAt,
        CreatedBy = value.CreatedBy,
        UpdatedAt = value.UpdatedAt,
        UpdatedBy = value.UpdatedBy
    };

    private static AssistantPublishedVersionRow ToRow(AssistantPublishedVersion value) => new()
    {
        Id = value.Id,
        AssistantKey = value.AssistantKey,
        AssistantId = value.AssistantId,
        TenantId = value.TenantId,
        OrgId = value.OrgId,
        VersionNo = value.VersionNo,
        Name = value.Name,
        Description = value.Description,
        SnapshotJson = value.SnapshotJson,
        SnapshotHash = value.SnapshotHash,
        IsRevoked = value.IsRevoked,
        RevokedAt = value.RevokedAt,
        RevokedBy = value.RevokedBy,
        IsDeleted = value.IsDeleted,
        DeletedAt = value.DeletedAt,
        DeletedBy = value.DeletedBy,
        PublishedAt = value.PublishedAt,
        PublishedBy = value.PublishedBy
    };

    private static AssistantPublicationPointer ToDomain(AssistantPublicationPointerRow row)
        => AssistantPublicationPointer.Rehydrate(
            row.Id,
            row.AssistantKey,
            row.AssistantId,
            row.TenantId,
            row.OrgId,
            row.CurrentPublishedVersionId,
            row.CurrentVersionNo,
            row.PointerRevision,
            (AssistantPublicationPointerStatus)row.Status,
            row.IsDeleted,
            row.DeletedAt,
            row.DeletedBy,
            row.CreatedAt,
            row.CreatedBy,
            row.UpdatedAt,
            row.UpdatedBy);

    private static AssistantPublishedVersion ToDomain(AssistantPublishedVersionRow row)
        => AssistantPublishedVersion.Rehydrate(
            row.Id,
            row.AssistantKey,
            row.AssistantId,
            row.TenantId,
            row.OrgId,
            row.VersionNo,
            row.Name,
            row.Description,
            row.SnapshotJson,
            row.SnapshotHash,
            row.IsRevoked,
            row.RevokedAt,
            row.RevokedBy,
            row.IsDeleted,
            row.DeletedAt,
            row.DeletedBy,
            row.PublishedAt,
            row.PublishedBy);

    /// <summary>
    /// 当前公开发布指针的数据库行映射。
    /// </summary>
    [SugarTable("assistant_publication_pointer")]
    private sealed class AssistantPublicationPointerRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "assistant_key")] public string AssistantKey { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "assistant_id")] public string AssistantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "current_published_version_id", IsNullable = true)] public string? CurrentPublishedVersionId { get; set; }
        [SugarColumn(ColumnName = "current_version_no")] public long CurrentVersionNo { get; set; }
        [SugarColumn(ColumnName = "pointer_revision")] public long PointerRevision { get; set; }
        [SugarColumn(ColumnName = "status")] public int Status { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "updated_at")] public DateTime UpdatedAt { get; set; }
        [SugarColumn(ColumnName = "updated_by")] public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 冻结的助手发布版本数据库行映射。
    /// </summary>
    [SugarTable("assistant_published_version")]
    private sealed class AssistantPublishedVersionRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "assistant_key")] public string AssistantKey { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "assistant_id")] public string AssistantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "version_no")] public long VersionNo { get; set; }
        [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "description", IsNullable = true)] public string? Description { get; set; }
        [SugarColumn(ColumnName = "snapshot_json", ColumnDataType = "json")] public string SnapshotJson { get; set; } = "{}";
        [SugarColumn(ColumnName = "snapshot_hash")] public string SnapshotHash { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "is_revoked")] public bool IsRevoked { get; set; }
        [SugarColumn(ColumnName = "revoked_at", IsNullable = true)] public DateTime? RevokedAt { get; set; }
        [SugarColumn(ColumnName = "revoked_by", IsNullable = true)] public string? RevokedBy { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
        [SugarColumn(ColumnName = "published_at")] public DateTime PublishedAt { get; set; }
        [SugarColumn(ColumnName = "published_by")] public string PublishedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 表示发布指针在提交前已被其他操作修改。
    /// </summary>
    public sealed class PointerRevisionConflictException : Exception
    {
        public PointerRevisionConflictException() : base("assistant_publication_pointer_revision_conflict") { }
    }

    /// <summary>
    /// 表示发布版本在撤回写入时发生状态冲突。
    /// </summary>
    public sealed class VersionRevokeConflictException : Exception
    {
        public VersionRevokeConflictException() : base("assistant_publication_version_revoke_conflict") { }
    }
}
