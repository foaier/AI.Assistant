#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SqlSugar;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 草稿预览的持久化记录，关联执行快照、会话和到期时间。
/// </summary>
[SugarTable("assistant_preview_snapshot")]
public sealed class AssistantPreviewRecord
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = "";
    [SugarColumn(ColumnName = "tenant_id")] public string TenantId { get; set; } = "";
    [SugarColumn(ColumnName = "org_id")] public string OrgId { get; set; } = "";
    [SugarColumn(ColumnName = "assistant_id")] public string AssistantId { get; set; } = "";
    [SugarColumn(ColumnName = "actor_id")] public string ActorId { get; set; } = "";
    [SugarColumn(ColumnName = "draft_revision")] public long DraftRevision { get; set; }
    [SugarColumn(ColumnName = "configuration_json")] public string ConfigurationJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "session_id")] public string SessionId { get; set; } = "";
    [SugarColumn(ColumnName = "conversation_id")] public string ConversationId { get; set; } = "";
    [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
    [SugarColumn(ColumnName = "expires_at")] public DateTime ExpiresAt { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "deleted_at", IsNullable = true)] public DateTime? DeletedAt { get; set; }
    [SugarColumn(ColumnName = "deleted_by", IsNullable = true)] public string? DeletedBy { get; set; }
}

/// <summary>
/// 保存与读取草稿预览记录。
/// </summary>
public sealed class AssistantPreviewRepository(IDbContext db)
{
    public Task<AssistantPreviewRecord?> GetAsync(string tenant, string org, string assistant, string actor, string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return db.GetSugarScope(ConnectionType.Master).Queryable<AssistantPreviewRecord>()
            .Where(x => x.Id == id && x.TenantId == tenant && x.OrgId == org && x.AssistantId == assistant
                && x.ActorId == actor && !x.IsDeleted).FirstAsync()!;
    }
    public void Add(AssistantPreviewRecord record) => db.AddCommand(client => client.Insertable(record).ExecuteCommandAsync());
}
