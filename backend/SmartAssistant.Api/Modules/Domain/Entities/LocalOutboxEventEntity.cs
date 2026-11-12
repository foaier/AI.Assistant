#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Modules.Domain.Entities;

/// <summary>
/// 本地 Outbox 中保存的事件标识、类型及序列化内容。
/// </summary>
[SugarTable("assistant_outbox")]
public sealed class LocalOutboxEventEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = "";

    /// <summary>
    /// 表示本地事件发件箱事件的事件类型信息。
    /// </summary>
    [SugarColumn(ColumnName = "event_type")]
    public string EventType { get; set; } = "";

    /// <summary>
    /// 保存事件负载的序列化 JSON 内容。
    /// </summary>
    [SugarColumn(ColumnName = "payload_json")]
    public string PayloadJson { get; set; } = "";
}
