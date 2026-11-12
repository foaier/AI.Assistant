#nullable enable
using System;
using SqlSugar;

namespace AI.Assistant.Services.Domain.Entities;

/// <summary>
/// 对话及其当前轮次进度的数据库行映射。
/// </summary>
[SugarTable("assistant_conversation")]
internal sealed class ConversationEntity : LifecycleEntity
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
    /// 关联创建该对话或请求的匿名会话。
    /// </summary>
    [SugarColumn(ColumnName = "session_id")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 记录会话或请求使用的语言区域。
    /// </summary>
    [SugarColumn(ColumnName = "locale")]
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    /// <summary>
    /// 记录当前对话已分配的最后轮次号。
    /// </summary>
    [SugarColumn(ColumnName = "last_turn_no")]
    public long LastTurnNo { get; set; }

    /// <summary>
    /// 记录对话最近一次活动的时间。
    /// </summary>
    [SugarColumn(ColumnName = "last_activity_at")]
    public DateTime LastActivityAt { get; set; }
}
