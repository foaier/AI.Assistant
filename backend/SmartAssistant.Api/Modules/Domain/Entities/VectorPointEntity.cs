#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Modules.Domain.Entities;

/// <summary>
/// 向量片段的持久化记录，关联文档版本、片段标识和内容哈希。
/// </summary>
[SugarTable("assistant_vector_point")]
public sealed class VectorPointEntity
{
    /// <summary>
    /// 记录向量索引所属的集合名称。
    /// </summary>
    [SugarColumn(ColumnName = "collection_name", IsPrimaryKey = true)]
    public string CollectionName { get; set; } = "";

    /// <summary>
    /// 关联知识文档的文本片段。
    /// </summary>
    [SugarColumn(ColumnName = "chunk_id", IsPrimaryKey = true)]
    public string ChunkId { get; set; } = "";

    /// <summary>
    /// 记录正文的内容摘要，用于检测内容变化。
    /// </summary>
    [SugarColumn(ColumnName = "content_hash")]
    public string ContentHash { get; set; } = "";

    /// <summary>
    /// 关联产生此数据的原始文档。
    /// </summary>
    [SugarColumn(ColumnName = "document_id")]
    public string DocumentId { get; set; } = "";

    /// <summary>
    /// 记录发布版本或文档版本的递增编号。
    /// </summary>
    [SugarColumn(ColumnName = "version_no")]
    public int VersionNo { get; set; }

    /// <summary>
    /// 记录片段在所属文档中的顺序。
    /// </summary>
    [SugarColumn(ColumnName = "chunk_index")]
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 保存序列化后的嵌入向量。
    /// </summary>
    [SugarColumn(ColumnName = "vector_json")]
    public string VectorJson { get; set; } = "";
}
