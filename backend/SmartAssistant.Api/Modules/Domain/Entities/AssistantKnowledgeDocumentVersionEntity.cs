#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Modules.Domain.Entities;

/// <summary>
/// 知识文档某次解析版本的数据库记录。
/// </summary>
[SugarTable("assistant_knowledge_document_version")]
public sealed class AssistantKnowledgeDocumentVersionEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 关联产生此数据的原始文档。
    /// </summary>
    [SugarColumn(ColumnName = "document_id")]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 记录发布版本或文档版本的递增编号。
    /// </summary>
    [SugarColumn(ColumnName = "version_no")]
    public int VersionNo { get; set; }

    /// <summary>
    /// 记录正文的内容摘要，用于检测内容变化。
    /// </summary>
    [SugarColumn(ColumnName = "content_hash")]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// 记录生成当前文档正文使用的解析器版本。
    /// </summary>
    [SugarColumn(ColumnName = "parser_version")]
    public string ParserVersion { get; set; } = string.Empty;

    /// <summary>
    /// 记录生成当前片段使用的策略标识。
    /// </summary>
    [SugarColumn(ColumnName = "chunk_strategy")]
    public string ChunkStrategy { get; set; } = string.Empty;

    /// <summary>
    /// 保存文档处理时使用的分块策略 JSON。
    /// </summary>
    [SugarColumn(ColumnName = "chunk_policy_json", ColumnDataType = "longtext")]
    public string ChunkPolicyJson { get; set; } = string.Empty;

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = "uploaded";

    /// <summary>
    /// 记录文档生成的文本片段数量。
    /// </summary>
    [SugarColumn(ColumnName = "chunk_count")]
    public int ChunkCount { get; set; }

    /// <summary>
    /// 映射数据库中的“索引状态”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "index_status")]
    public string IndexStatus { get; set; } = "queued";

    /// <summary>
    /// 记录文档版本完成索引的时间。
    /// </summary>
    [SugarColumn(ColumnName = "indexed_at", IsNullable = true)]
    public DateTime? IndexedAt { get; set; }

    /// <summary>
    /// 保存可稳定识别的错误代码。
    /// </summary>
    [SugarColumn(ColumnName = "error_code", IsNullable = true)]
    public string? ErrorCode { get; set; }
}
