#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Modules.Domain.Entities;

/// <summary>
/// 工具调用输入、输出及执行状态的数据库行映射。
/// </summary>
[SugarTable("assistant_tool_call")]
internal sealed class ToolCallEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 关联产生此事件、消息或工具调用的执行运行。
    /// </summary>
    [SugarColumn(ColumnName = "run_id")]
    public string RunId { get; set; } = string.Empty;

    /// <summary>
    /// 关联当前使用的工具定义。
    /// </summary>
    [SugarColumn(ColumnName = "tool_id")]
    public string ToolId { get; set; } = string.Empty;

    /// <summary>
    /// 映射数据库中的“契约版本”列，并由仓储负责读取和持久化。
    /// </summary>
    [SugarColumn(ColumnName = "contract_version")]
    public string ContractVersion { get; set; } = string.Empty;

    /// <summary>
    /// 保存工具调用输入的摘要。
    /// </summary>
    [SugarColumn(ColumnName = "input_hash")]
    public string InputHash { get; set; } = string.Empty;

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    /// <summary>
    /// 保存工具执行结果的 JSON 内容。
    /// </summary>
    [SugarColumn(ColumnName = "output_json")]
    public string? OutputJson { get; set; }

    /// <summary>
    /// 保存运行或索引失败的稳定错误码。
    /// </summary>
    [SugarColumn(ColumnName = "failure_code")]
    public string? FailureCode { get; set; }

    /// <summary>
    /// 保存工具执行实际耗时（毫秒），用于统计成功、失败和拒绝调用的耗时。
    /// </summary>
    [SugarColumn(ColumnName = "duration_ms")]
    public double? DurationMs { get; set; }

    /// <summary>
    /// 记录处理完成或进入终态的时间。
    /// </summary>
    [SugarColumn(ColumnName = "completed_at")]
    public DateTime? CompletedAt { get; set; }
}
