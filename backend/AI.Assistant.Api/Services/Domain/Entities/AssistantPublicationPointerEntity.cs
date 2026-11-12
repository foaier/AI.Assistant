#nullable enable
using System;
using SqlSugar;

namespace AI.Assistant.Services.Domain.Entities;

/// <summary>
/// 当前公开发布指针的数据库行映射。
/// </summary>
[SugarTable("assistant_publication_pointer")]
internal sealed class AssistantPublicationPointerEntity
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
    /// 指向助手当前生效的发布版本。
    /// </summary>
    [SugarColumn(ColumnName = "current_published_version_id", IsNullable = true)]
    public string? CurrentPublishedVersionId { get; set; }

    /// <summary>
    /// 记录当前发布版本号。
    /// </summary>
    [SugarColumn(ColumnName = "current_version_no")]
    public long CurrentVersionNo { get; set; }

    /// <summary>
    /// 记录发布指针修订号，用于检测并发发布或撤回。
    /// </summary>
    [SugarColumn(ColumnName = "pointer_revision")]
    public long PointerRevision { get; set; }

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }
}
