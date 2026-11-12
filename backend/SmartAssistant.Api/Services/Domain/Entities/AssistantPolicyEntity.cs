#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 模型或业务策略的当前配置数据库记录。
/// </summary>
[SugarTable("assistant_policy_record")]
public sealed class AssistantPolicyEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = "";

    /// <summary>
    /// 表示助手策略记录的种类信息。
    /// </summary>
    [SugarColumn(ColumnName = "kind")]
    public string Kind { get; set; } = "";

    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 保存统一业务响应的结果代码。
    /// </summary>
    [SugarColumn(ColumnName = "business_code")]
    public string? BusinessCode { get; set; }

    /// <summary>
    /// 记录当前配置修订号，用于乐观并发控制。
    /// </summary>
    [SugarColumn(ColumnName = "revision")]
    public long Revision { get; set; }

    /// <summary>
    /// 标记对应能力或配置是否启用。
    /// </summary>
    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 保存版本化模型或业务策略设置。
    /// </summary>
    [SugarColumn(ColumnName = "settings_json")]
    public string SettingsJson { get; set; } = "{}";
}
