#nullable enable
using System;
using SqlSugar;

namespace AI.Assistant.Services.Domain.Entities;

/// <summary>
/// 用户请求轮次及其幂等信息的数据库行映射。
/// </summary>
[SugarTable("assistant_turn")]
internal sealed class TurnEntity : LifecycleEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "id")]
    public new string Id
    {
        get => base.Id;
        set => base.Id = value;
    }

    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    [SugarColumn(ColumnName = "assistant_id")]
    public new string AssistantId
    {
        get => base.AssistantId;
        set => base.AssistantId = value;
    }

    /// <summary>
    /// 绑定创建此会话或运行时使用的固定发布版本。
    /// </summary>
    [SugarColumn(ColumnName = "published_version_id")]
    public new string PublishedVersionId
    {
        get => base.PublishedVersionId;
        set => base.PublishedVersionId = value;
    }

    /// <summary>
    /// 区分公开执行和管理端预览执行。
    /// </summary>
    [SugarColumn(ColumnName = "execution_mode")]
    public new int ExecutionMode
    {
        get => base.ExecutionMode;
        set => base.ExecutionMode = value;
    }

    /// <summary>
    /// 关联此消息、轮次或运行所属的对话。
    /// </summary>
    [SugarColumn(ColumnName = "conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// 关联创建该对话或请求的匿名会话。
    /// </summary>
    [SugarColumn(ColumnName = "session_id")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 保留客户端幂等标识，用于识别同一消息的重试。
    /// </summary>
    [SugarColumn(ColumnName = "client_message_id")]
    public string ClientMessageId { get; set; } = string.Empty;

    /// <summary>
    /// 保存请求内容摘要，用于验证重放是否与原请求一致。
    /// </summary>
    [SugarColumn(ColumnName = "request_hash")]
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>
    /// 记录用户请求在所属对话中的轮次编号。
    /// </summary>
    [SugarColumn(ColumnName = "turn_no")]
    public long TurnNo { get; set; }

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    /// <summary>
    /// 记录处理完成或进入终态的时间。
    /// </summary>
    [SugarColumn(ColumnName = "completed_at")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 保存运行或索引失败的稳定错误码。
    /// </summary>
    [SugarColumn(ColumnName = "failure_code")]
    public string? FailureCode { get; set; }
}
