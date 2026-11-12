#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Modules.Domain.Entities;

/// <summary>
/// 查询有效发布指针时使用的数据库投影。
/// </summary>
[SugarTable("assistant_publication_pointer")]
internal sealed class AssistantPublicationPointerReferenceEntity
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
    /// 指向助手当前生效的发布版本。
    /// </summary>
    [SugarColumn(ColumnName = "current_published_version_id", IsNullable = true)]
    public string? CurrentPublishedVersionId { get; set; }

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }
}
