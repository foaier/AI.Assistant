#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 知识库嵌入配置的数据库记录，区分目标代次与生效代次。
/// </summary>
[SugarTable("assistant_knowledge_embedding_policy")]
public sealed class AssistantKnowledgeEmbeddingProfileEntity
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
    /// 记录使用的模型或嵌入供应商标识。
    /// </summary>
    [SugarColumn(ColumnName = "provider_id")]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// 绑定使用的供应商配置修订。
    /// </summary>
    [SugarColumn(ColumnName = "provider_revision")]
    public long ProviderRevision { get; set; }

    /// <summary>
    /// 记录当前选择的模型标识。
    /// </summary>
    [SugarColumn(ColumnName = "model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 记录嵌入向量的固定维度。
    /// </summary>
    [SugarColumn(ColumnName = "dimension")]
    public int Dimension { get; set; }

    /// <summary>
    /// 正在构建或计划构建的索引代次，与已生效代次分别维护。
    /// </summary>
    [SugarColumn(ColumnName = "target_generation")]
    public long TargetGeneration { get; set; }

    /// <summary>
    /// 映射数据库中的“生效模型供应商”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "active_provider_id", IsNullable = true)]
    public string? ActiveProviderId { get; set; }

    /// <summary>
    /// 映射数据库中的“生效模型供应商修订”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "active_provider_revision", IsNullable = true)]
    public long? ActiveProviderRevision { get; set; }

    /// <summary>
    /// 映射数据库中的“生效模型”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "active_model", IsNullable = true)]
    public string? ActiveModel { get; set; }

    /// <summary>
    /// 映射数据库中的“生效维度”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "active_dimension", IsNullable = true)]
    public int? ActiveDimension { get; set; }

    /// <summary>
    /// 已完成索引并可供发布快照引用的生效代次。
    /// </summary>
    [SugarColumn(ColumnName = "active_generation")]
    public long ActiveGeneration { get; set; }

    /// <summary>
    /// 映射数据库中的“生效配置修订”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "active_profile_revision", IsNullable = true)]
    public long? ActiveProfileRevision { get; set; }

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = "rebuilding";

    /// <summary>
    /// 保存任务最近一次失败的稳定错误码。
    /// </summary>
    [SugarColumn(ColumnName = "last_error_code", IsNullable = true)]
    public string? LastErrorCode { get; set; }

    /// <summary>
    /// 记录当前配置修订号，用于乐观并发控制。
    /// </summary>
    [SugarColumn(ColumnName = "revision")]
    public long Revision { get; set; }
}
