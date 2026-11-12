#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 知识库分块策略与修订号的数据库记录。
/// </summary>
[SugarTable("assistant_knowledge_chunking_policy")]
public sealed class AssistantKnowledgeChunkingPolicyEntity
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
    /// 映射数据库中的“模式”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "mode")]
    public string Mode { get; set; } = "hybrid";

    /// <summary>
    /// 限制单个文本片段的最大字符数。
    /// </summary>
    [SugarColumn(ColumnName = "max_chunk_size")]
    public int MaxChunkSize { get; set; }

    /// <summary>
    /// 指定短片段合并所依据的最小字符数。
    /// </summary>
    [SugarColumn(ColumnName = "min_chunk_size")]
    public int MinChunkSize { get; set; }

    /// <summary>
    /// 指定相邻文本片段保留的重叠字符数。
    /// </summary>
    [SugarColumn(ColumnName = "overlap_size")]
    public int OverlapSize { get; set; }

    /// <summary>
    /// 限制用于文档分段的 Markdown 标题层级。
    /// </summary>
    [SugarColumn(ColumnName = "max_heading_level")]
    public int MaxHeadingLevel { get; set; }

    /// <summary>
    /// 控制片段正文是否保留所属标题路径。
    /// </summary>
    [SugarColumn(ColumnName = "preserve_heading_path")]
    public bool PreserveHeadingPath { get; set; }

    /// <summary>
    /// 控制是否合并长度不足的相邻片段。
    /// </summary>
    [SugarColumn(ColumnName = "merge_short_chunks")]
    public bool MergeShortChunks { get; set; }

    /// <summary>
    /// 记录当前配置修订号，用于乐观并发控制。
    /// </summary>
    [SugarColumn(ColumnName = "revision")]
    public long Revision { get; set; }
}
