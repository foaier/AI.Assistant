#nullable enable
using System;
using SqlSugar;

namespace AI.Assistant.Services.Domain.Entities;

/// <summary>
/// 固定问答的数据库记录，包含标准化问题和展示设置。
/// </summary>
[SugarTable("assistant_knowledge_qna")]
public sealed class AssistantKnowledgeQnaEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 关联此数据所属的知识库。
    /// </summary>
    [SugarColumn(ColumnName = "knowledge_base_id")]
    public string KnowledgeBaseId { get; set; } = string.Empty;

    /// <summary>
    /// 映射数据库中的“问题”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "question")]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// 保存规范化后的问题文本，用于固定问答匹配和重复检测。
    /// </summary>
    [SugarColumn(ColumnName = "normalized_question")]
    public string NormalizedQuestion { get; set; } = string.Empty;

    /// <summary>
    /// 映射数据库中的“回答”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "answer")]
    public string? Answer { get; set; }

    /// <summary>
    /// 标记对应能力或配置是否启用。
    /// </summary>
    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// 记录问答项在列表中的显示顺序。
    /// </summary>
    [SugarColumn(ColumnName = "sort_order")]
    public int SortOrder { get; set; }

    /// <summary>
    /// 标记该问答是否可以作为建议问题展示。
    /// </summary>
    [SugarColumn(ColumnName = "show_as_suggestion")]
    public bool ShowAsSuggestion { get; set; }
}
