#nullable enable
using System;
using SqlSugar;

namespace SmartAssistant.Modules.Domain.Entities;

/// <summary>
/// 匿名会话的数据库行映射。
/// </summary>
[SugarTable("assistant_anonymous_session")]
internal sealed class SessionEntity : LifecycleEntity
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
    /// 保存会话主体标识的摘要。
    /// </summary>
    [SugarColumn(ColumnName = "subject_hash")]
    public string SubjectHash { get; set; } = string.Empty;

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
    /// 记录匿名会话最近一次访问的时间。
    /// </summary>
    [SugarColumn(ColumnName = "last_seen_at")]
    public DateTime LastSeenAt { get; set; }

    /// <summary>
    /// 记录会话、票据或预览失效时间。
    /// </summary>
    [SugarColumn(ColumnName = "expires_at")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 记录授权或版本撤回的时间。
    /// </summary>
    [SugarColumn(ColumnName = "revoked_at")]
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// 记录执行撤回的操作人。
    /// </summary>
    [SugarColumn(ColumnName = "revoked_by")]
    public string? RevokedBy { get; set; }
}
