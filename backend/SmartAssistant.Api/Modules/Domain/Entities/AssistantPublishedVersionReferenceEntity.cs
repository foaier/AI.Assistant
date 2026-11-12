#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Modules.Domain.Entities;

/// <summary>
/// 查询发布版本知识库引用时使用的数据库投影。
/// </summary>
[SugarTable("assistant_published_version")]
internal sealed class AssistantPublishedVersionReferenceEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    [SugarColumn(ColumnName = "assistant_id")]
    public string AssistantId { get; set; } = string.Empty;

    /// <summary>
    /// 保存发布时冻结的助手配置与执行依赖。
    /// </summary>
    [SugarColumn(ColumnName = "snapshot_json", ColumnDataType = "json")]
    public string SnapshotJson { get; set; } = "{}";

    /// <summary>
    /// 标记发布版本或访问授权是否已撤回。
    /// </summary>
    [SugarColumn(ColumnName = "is_revoked")]
    public bool IsRevoked { get; set; }
}
