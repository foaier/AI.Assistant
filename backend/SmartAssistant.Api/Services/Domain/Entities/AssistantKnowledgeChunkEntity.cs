#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 文档版本中的文本片段及其索引关联记录。
/// </summary>
[SugarTable("assistant_knowledge_chunk")]
public sealed class AssistantKnowledgeChunkEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 关联当前片段或索引任务使用的文档版本。
    /// </summary>
    [SugarColumn(ColumnName = "document_version_id")]
    public string DocumentVersionId { get; set; } = string.Empty;

    /// <summary>
    /// 记录片段在所属文档中的顺序。
    /// </summary>
    [SugarColumn(ColumnName = "chunk_index")]
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 记录正文的内容摘要，用于检测内容变化。
    /// </summary>
    [SugarColumn(ColumnName = "content_hash")]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// 映射数据库中的“内容”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "content", ColumnDataType = "longtext")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 记录片段或内容估算的令牌数量。
    /// </summary>
    [SugarColumn(ColumnName = "token_count")]
    public int TokenCount { get; set; }

    /// <summary>
    /// 记录外部向量索引中对应点的标识。
    /// </summary>
    [SugarColumn(ColumnName = "point_id", IsNullable = true)]
    public string? PointId { get; set; }
}
