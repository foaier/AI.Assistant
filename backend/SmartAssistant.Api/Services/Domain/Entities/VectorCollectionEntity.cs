#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 向量集合的持久化元数据，约束同一集合使用统一的向量维度。
/// </summary>
[SugarTable("assistant_vector_collection")]
public sealed class VectorCollectionEntity
{
    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    [SugarColumn(ColumnName = "name", IsPrimaryKey = true)]
    public string Name { get; set; } = "";

    /// <summary>
    /// 记录嵌入向量的固定维度。
    /// </summary>
    [SugarColumn(ColumnName = "dimension")]
    public int Dimension { get; set; }
}
