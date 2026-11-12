#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Modules.Domain.Entities;

/// <summary>
/// 草稿预览的持久化记录，关联执行快照、会话和到期时间。
/// </summary>
[SugarTable("assistant_preview_snapshot")]
public sealed class AssistantPreviewEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    [SugarColumn(ColumnName = "assistant_id")]
    public string AssistantId { get; set; } = "";

    /// <summary>
    /// 记录执行业务操作的用户标识。
    /// </summary>
    [SugarColumn(ColumnName = "actor_id")]
    public string ActorId { get; set; } = "";

    /// <summary>
    /// 记录草稿修订号，用于检测并发保存。
    /// </summary>
    [SugarColumn(ColumnName = "draft_revision")]
    public long DraftRevision { get; set; }

    /// <summary>
    /// 保存序列化的助手配置或执行快照。
    /// </summary>
    [SugarColumn(ColumnName = "configuration_json")]
    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>
    /// 关联创建该对话或请求的匿名会话。
    /// </summary>
    [SugarColumn(ColumnName = "session_id")]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// 关联此消息、轮次或运行所属的对话。
    /// </summary>
    [SugarColumn(ColumnName = "conversation_id")]
    public string ConversationId { get; set; } = "";

    /// <summary>
    /// 记录会话、票据或预览失效时间。
    /// </summary>
    [SugarColumn(ColumnName = "expires_at")]
    public DateTime ExpiresAt { get; set; }
}
