#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 冻结的助手发布版本数据库行映射。
/// </summary>
[SugarTable("assistant_published_version")]
internal sealed class AssistantPublishedVersionEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 映射数据库中的“助手密钥”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "assistant_key")]
    public string AssistantKey { get; set; } = string.Empty;

    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    [SugarColumn(ColumnName = "assistant_id")]
    public string AssistantId { get; set; } = string.Empty;

    /// <summary>
    /// 记录发布版本或文档版本的递增编号。
    /// </summary>
    [SugarColumn(ColumnName = "version_no")]
    public long VersionNo { get; set; }

    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 记录此资源的可选说明。
    /// </summary>
    [SugarColumn(ColumnName = "description", IsNullable = true)]
    public string? Description { get; set; }

    /// <summary>
    /// 保存发布时冻结的助手配置与执行依赖。
    /// </summary>
    [SugarColumn(ColumnName = "snapshot_json", ColumnDataType = "json")]
    public string SnapshotJson { get; set; } = "{}";

    /// <summary>
    /// 记录冻结发布快照的内容摘要。
    /// </summary>
    [SugarColumn(ColumnName = "snapshot_hash")]
    public string SnapshotHash { get; set; } = string.Empty;

    /// <summary>
    /// 标记发布版本或访问授权是否已撤回。
    /// </summary>
    [SugarColumn(ColumnName = "is_revoked")]
    public bool IsRevoked { get; set; }

    /// <summary>
    /// 记录授权或版本撤回的时间。
    /// </summary>
    [SugarColumn(ColumnName = "revoked_at", IsNullable = true)]
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// 记录版本发布的时间。
    /// </summary>
    [SugarColumn(ColumnName = "published_at")]
    public DateTime PublishedAt { get; set; }
}
