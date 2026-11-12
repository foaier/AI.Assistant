#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Services.Domain.Entities;

/// <summary>
/// 助手运行状态、模型信息和事件进度的数据库行映射。
/// </summary>
[SugarTable("assistant_run")]
internal sealed class RunEntity : LifecycleEntity
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
    /// 关联此记录所属的用户请求轮次。
    /// </summary>
    [SugarColumn(ColumnName = "turn_id")]
    public string TurnId { get; set; } = string.Empty;

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
    /// 保存本次执行使用的供应商配置及其修订引用。
    /// </summary>
    [SugarColumn(ColumnName = "provider_profile_ref")]
    public string ProviderProfileRef { get; set; } = string.Empty;

    /// <summary>
    /// 记录当前选择的模型标识。
    /// </summary>
    [SugarColumn(ColumnName = "model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    /// <summary>
    /// 记录当前已预留事件序号上限。
    /// </summary>
    [SugarColumn(ColumnName = "sequence_head")]
    public long SequenceHead { get; set; }

    /// <summary>
    /// 记录执行或索引任务的开始时间。
    /// </summary>
    [SugarColumn(ColumnName = "started_at")]
    public DateTime? StartedAt { get; set; }

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

    /// <summary>
    /// 记录模型输入占用的令牌数量。
    /// </summary>
    [SugarColumn(ColumnName = "prompt_tokens")]
    public int? PromptTokens { get; set; }

    /// <summary>
    /// 记录模型生成内容占用的令牌数量。
    /// </summary>
    [SugarColumn(ColumnName = "completion_tokens")]
    public int? CompletionTokens { get; set; }

    /// <summary>
    /// 记录本次模型调用消耗的令牌总数。
    /// </summary>
    [SugarColumn(ColumnName = "total_tokens")]
    public int? TotalTokens { get; set; }
}
