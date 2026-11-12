#nullable enable
using System;
using SqlSugar;

namespace AI.Assistant.Services.Domain.Entities;

/// <summary>
/// 知识库的数据库记录，包含名称、说明及所属范围。
/// </summary>
[SugarTable("assistant_knowledge_base")]
public sealed class AssistantKnowledgeBaseEntity
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
}
