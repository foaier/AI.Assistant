#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 模型或业务策略的历史配置修订记录，供冻结版本解析使用。
/// </summary>
[SugarTable("assistant_policy_revision")]
public sealed class AssistantPolicyRevisionEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = "";

    /// <summary>
    /// 表示助手策略修订记录的记录信息。
    /// </summary>
    [SugarColumn(ColumnName = "record_id")]
    public string RecordId { get; set; } = "";

    /// <summary>
    /// 表示助手策略修订记录的种类信息。
    /// </summary>
    [SugarColumn(ColumnName = "kind")]
    public string Kind { get; set; } = "";

    /// <summary>
    /// 记录当前配置修订号，用于乐观并发控制。
    /// </summary>
    [SugarColumn(ColumnName = "revision")]
    public long Revision { get; set; }

    /// <summary>
    /// 标记对应能力或配置是否启用。
    /// </summary>
    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// 保存版本化模型或业务策略设置。
    /// </summary>
    [SugarColumn(ColumnName = "settings_json")]
    public string SettingsJson { get; set; } = "{}";
}
