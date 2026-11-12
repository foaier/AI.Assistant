#nullable enable
using System;
using SqlSugar;

namespace AI.Assistant.Services.Domain.Entities;

/// <summary>
/// 助手草稿的数据库行映射。
/// </summary>
[SugarTable("assistant_draft")]
internal sealed class AssistantDraftEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 记录此资源的可选说明。
    /// </summary>
    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }

    /// <summary>
    /// 保存尚未发布的可编辑助手配置。
    /// </summary>
    [SugarColumn(ColumnName = "draft_json")]
    public string DraftJson { get; set; } = "{}";

    /// <summary>
    /// 记录草稿修订号，用于检测并发保存。
    /// </summary>
    [SugarColumn(ColumnName = "draft_revision")]
    public long DraftRevision { get; set; }

    /// <summary>
    /// 保存创建请求的幂等键，用于识别重复提交。
    /// </summary>
    [SugarColumn(ColumnName = "creation_idempotency_key")]
    public string? CreationIdempotencyKey { get; set; }
}
