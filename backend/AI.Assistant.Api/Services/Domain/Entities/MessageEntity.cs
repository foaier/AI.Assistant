#nullable enable
using System;
using SqlSugar;

namespace AI.Assistant.Services.Domain.Entities;

/// <summary>
/// 对话消息内容与生成状态的数据库行映射。
/// </summary>
[SugarTable("assistant_message")]
internal sealed class MessageEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 关联此消息、轮次或运行所属的对话。
    /// </summary>
    [SugarColumn(ColumnName = "conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// 关联此记录所属的用户请求轮次。
    /// </summary>
    [SugarColumn(ColumnName = "turn_id")]
    public string TurnId { get; set; } = string.Empty;

    /// <summary>
    /// 关联产生此事件、消息或工具调用的执行运行。
    /// </summary>
    [SugarColumn(ColumnName = "run_id")]
    public string? RunId { get; set; }

    /// <summary>
    /// 区分用户、助手和工具消息。
    /// </summary>
    [SugarColumn(ColumnName = "role")]
    public int Role { get; set; }

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    /// <summary>
    /// 保存消息正文及其附加字段的 JSON 内容。
    /// </summary>
    [SugarColumn(ColumnName = "content_json")]
    public string ContentJson { get; set; } = "{}";

    /// <summary>
    /// 记录处理完成或进入终态的时间。
    /// </summary>
    [SugarColumn(ColumnName = "completed_at")]
    public DateTime? CompletedAt { get; set; }
}
