#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 知识文档的数据库主记录。
/// </summary>
[SugarTable("assistant_knowledge_document")]
public sealed class AssistantKnowledgeDocumentEntity
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
    /// 映射数据库中的“资源”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "asset_id")]
    public string AssetId { get; set; } = string.Empty;

    /// <summary>
    /// 记录上传文件的原始文件名。
    /// </summary>
    [SugarColumn(ColumnName = "file_name")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 记录上传文件的媒体类型。
    /// </summary>
    [SugarColumn(ColumnName = "content_type")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// 记录文件占用的字节数。
    /// </summary>
    [SugarColumn(ColumnName = "size_bytes")]
    public long SizeBytes { get; set; }

    /// <summary>
    /// 记录当前发布版本号。
    /// </summary>
    [SugarColumn(ColumnName = "current_version_no")]
    public int CurrentVersionNo { get; set; }

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = "uploaded";

    /// <summary>
    /// 保存任务最近一次失败的稳定错误码。
    /// </summary>
    [SugarColumn(ColumnName = "last_error_code", IsNullable = true)]
    public string? LastErrorCode { get; set; }
}
