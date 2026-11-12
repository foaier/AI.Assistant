#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Platform;
using SqlSugar;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 定义模型与业务策略当前配置、历史修订的持久化操作。
/// </summary>
public interface IAssistantPolicyRepository
{
    Task<IReadOnlyList<AssistantPolicyRecord>> ListAsync(string tenantId, string orgId, string kind, CancellationToken ct);
    Task<AssistantPolicyRecord?> GetAsync(string tenantId, string orgId, string kind, string id, CancellationToken ct);
    Task<AssistantPolicyRecord?> GetProviderAsync(string id, CancellationToken ct);
    Task<AssistantPolicyRevisionRecord?> GetRevisionAsync(string tenantId, string orgId, string kind, string id, long revision, CancellationToken ct);
    Task SaveAsync(AssistantPolicyRecord record, long expectedRevision, CancellationToken ct);
}

/// <summary>
/// 保存模型与业务策略配置及历史修订，并检测并发更新。
/// </summary>
public sealed class AssistantPolicyRepository : IAssistantPolicyRepository
{
    private readonly IDbContext _db;
    public AssistantPolicyRepository(IDbContext db) => _db = db;
    public async Task<IReadOnlyList<AssistantPolicyRecord>> ListAsync(string tenantId, string orgId, string kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.GetSugarScope(ConnectionType.Master).Queryable<AssistantPolicyRecord>()
            .Where(x => x.TenantId == tenantId && x.OrgId == orgId && x.Kind == kind && !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync();
    }
    public async Task<AssistantPolicyRecord?> GetAsync(string tenantId, string orgId, string kind, string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.GetSugarScope(ConnectionType.Master).Queryable<AssistantPolicyRecord>()
            .Where(x => x.Id == id && x.TenantId == tenantId && x.OrgId == orgId && x.Kind == kind && !x.IsDeleted).FirstAsync();
    }
    public async Task<AssistantPolicyRecord?> GetProviderAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.GetSugarScope(ConnectionType.Master).Queryable<AssistantPolicyRecord>()
            .Where(x => x.Id == id && x.Kind == "provider" && x.Enabled && !x.IsDeleted).FirstAsync();
    }
    public async Task<AssistantPolicyRevisionRecord?> GetRevisionAsync(string tenantId, string orgId, string kind, string id, long revision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        /*
         * 只有所属范围内的当前配置仍有效时，历史修订才允许被访问。
         */
        return await _db.GetSugarScope(ConnectionType.Master).Queryable<AssistantPolicyRevisionRecord>()
            .InnerJoin<AssistantPolicyRecord>((v, p) => v.RecordId == p.Id && v.TenantId == p.TenantId && v.OrgId == p.OrgId && v.Kind == p.Kind)
            .Where((v, p) => v.RecordId == id && v.Revision == revision && v.TenantId == tenantId && v.OrgId == orgId && v.Kind == kind
                && !v.IsDeleted && !p.IsDeleted && p.Enabled).Select((v, p) => v).FirstAsync();
    }
    public Task SaveAsync(AssistantPolicyRecord record, long expectedRevision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (expectedRevision == 0)
            _db.AddCommand(client => client.Insertable(record).ExecuteCommandAsync());
        else
            _db.AddCommand(async client =>
            {
                var rows = await client.Updateable(record).Where(x => x.Id == record.Id && x.TenantId == record.TenantId
                    && x.OrgId == record.OrgId && x.Kind == record.Kind && x.Revision == expectedRevision && !x.IsDeleted).ExecuteCommandAsync();
                if (rows != 1) throw new BizResultException(BusinessCodes.Conflict, "assistant_policy_revision_conflict");
                return rows;
            });
        var version = new AssistantPolicyRevisionRecord
        {
            Id = Guid.NewGuid().ToString("N"), RecordId = record.Id, TenantId = record.TenantId, OrgId = record.OrgId,
            Kind = record.Kind, Revision = record.Revision, SettingsJson = record.SettingsJson, Enabled = record.Enabled,
            CreatedAt = record.UpdatedAt, CreatedBy = record.UpdatedBy
        };
        _db.AddCommand(client => client.Insertable(version).ExecuteCommandAsync());
        return Task.CompletedTask;
    }
}

/// <summary>
/// 模型或业务策略的当前配置数据库记录。
/// </summary>
[SugarTable("assistant_policy_record")]
public sealed class AssistantPolicyRecord
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public string Id { get; set; } = "";
    [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = "";
    [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = "";
    [SugarColumn(ColumnName = "kind")] public string Kind { get; set; } = "";
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = "";
    [SugarColumn(ColumnName = "business_code")] public string? BusinessCode { get; set; }
    [SugarColumn(ColumnName = "revision")] public long Revision { get; set; }
    [SugarColumn(ColumnName = "enabled")] public bool Enabled { get; set; } = true;
    [SugarColumn(ColumnName = "settings_json")] public string SettingsJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
    [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = "";
    [SugarColumn(ColumnName = "updated_at")] public DateTime UpdatedAt { get; set; }
    [SugarColumn(ColumnName = "updated_by")] public string UpdatedBy { get; set; } = "";
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "deleted_at")] public DateTime? DeletedAt { get; set; }
    [SugarColumn(ColumnName = "deleted_by")] public string? DeletedBy { get; set; }
}

/// <summary>
/// 模型或业务策略的历史配置修订记录，供冻结版本解析使用。
/// </summary>
[SugarTable("assistant_policy_revision")]
public sealed class AssistantPolicyRevisionRecord
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public string Id { get; set; } = "";
    [SugarColumn(ColumnName = "record_id")] public string RecordId { get; set; } = "";
    [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = "";
    [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = "";
    [SugarColumn(ColumnName = "kind")] public string Kind { get; set; } = "";
    [SugarColumn(ColumnName = "revision")] public long Revision { get; set; }
    [SugarColumn(ColumnName = "enabled")] public bool Enabled { get; set; }
    [SugarColumn(ColumnName = "settings_json")] public string SettingsJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
    [SugarColumn(ColumnName = "created_by")] public string CreatedBy { get; set; } = "";
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "deleted_at")] public DateTime? DeletedAt { get; set; }
    [SugarColumn(ColumnName = "deleted_by")] public string? DeletedBy { get; set; }
}
