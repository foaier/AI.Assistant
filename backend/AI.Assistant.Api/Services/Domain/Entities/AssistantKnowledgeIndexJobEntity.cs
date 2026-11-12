#nullable enable
using System;
using SqlSugar;

namespace AI.Assistant.Services.Domain.Entities;

/// <summary>
/// 冻结嵌入配置与文档版本的索引任务记录。
/// </summary>
[SugarTable("assistant_knowledge_index_job")]
public sealed class AssistantKnowledgeIndexJobEntity
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
    /// 关联产生此数据的原始文档。
    /// </summary>
    [SugarColumn(ColumnName = "document_id")]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 关联当前片段或索引任务使用的文档版本。
    /// </summary>
    [SugarColumn(ColumnName = "document_version_id")]
    public string DocumentVersionId { get; set; } = string.Empty;

    /// <summary>
    /// 记录发布版本或文档版本的递增编号。
    /// </summary>
    [SugarColumn(ColumnName = "version_no")]
    public int VersionNo { get; set; }

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = "queued";

    /// <summary>
    /// 记录任务已经尝试执行的次数。
    /// </summary>
    [SugarColumn(ColumnName = "attempt_count")]
    public int AttemptCount { get; set; }

    /// <summary>
    /// 记录文档生成的文本片段数量。
    /// </summary>
    [SugarColumn(ColumnName = "chunk_count")]
    public int ChunkCount { get; set; }

    /// <summary>
    /// 保存可稳定识别的错误代码。
    /// </summary>
    [SugarColumn(ColumnName = "error_code", IsNullable = true)]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 记录执行或索引任务的开始时间。
    /// </summary>
    [SugarColumn(ColumnName = "started_at", IsNullable = true)]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// 记录处理完成或进入终态的时间。
    /// </summary>
    [SugarColumn(ColumnName = "completed_at", IsNullable = true)]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 记录当前索引或配置代次，用于隔离重建任务。
    /// </summary>
    [SugarColumn(ColumnName = "generation", IsNullable = true)]
    public long? Generation { get; set; }

    /// <summary>
    /// 映射数据库中的“嵌入模型供应商”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "embedding_provider_id", IsNullable = true)]
    public string? EmbeddingProviderId { get; set; }

    /// <summary>
    /// 映射数据库中的“嵌入模型供应商修订”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "embedding_provider_revision", IsNullable = true)]
    public long? EmbeddingProviderRevision { get; set; }

    /// <summary>
    /// 映射数据库中的“嵌入模型”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "embedding_model", IsNullable = true)]
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// 映射数据库中的“嵌入维度”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "embedding_dimension", IsNullable = true)]
    public int? EmbeddingDimension { get; set; }

    /// <summary>
    /// 记录生成片段时使用的分块策略修订。
    /// </summary>
    [SugarColumn(ColumnName = "chunk_policy_revision", IsNullable = true)]
    public long? ChunkPolicyRevision { get; set; }

    /// <summary>
    /// 保存文档处理时使用的分块策略 JSON。
    /// </summary>
    [SugarColumn(ColumnName = "chunk_policy_json", ColumnDataType = "longtext", IsNullable = true)]
    public string? ChunkPolicyJson { get; set; }

    /// <summary>
    /// 记录生成当前文档正文使用的解析器版本。
    /// </summary>
    [SugarColumn(ColumnName = "parser_version", IsNullable = true)]
    public string? ParserVersion { get; set; }

    /// <summary>
    /// 记录正文的内容摘要，用于检测内容变化。
    /// </summary>
    [SugarColumn(ColumnName = "content_hash", IsNullable = true)]
    public string? ContentHash { get; set; }
}
