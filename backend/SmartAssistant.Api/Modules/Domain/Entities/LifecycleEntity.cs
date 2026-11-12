#nullable enable
using System;

namespace SmartAssistant.Modules.Domain.Entities;

/// <summary>
/// 运行数据共用的标识、助手范围、固定发布版本及执行模式。
/// </summary>
internal abstract class LifecycleEntity
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    public string AssistantId { get; set; } = string.Empty;

    /// <summary>
    /// 绑定创建此会话或运行时使用的固定发布版本。
    /// </summary>
    public string PublishedVersionId { get; set; } = string.Empty;

    /// <summary>
    /// 区分公开执行和管理端预览执行。
    /// </summary>
    public int ExecutionMode { get; set; }
}
