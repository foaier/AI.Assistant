#nullable enable
namespace AI.Assistant.Models.Publication;

/// <summary>
/// 公开版本的有效或撤回状态。
/// </summary>
public enum AssistantPublicationStatus
{
    /// <summary>
    /// 权限或发布状态已撤回。
    /// </summary>
    Revoked = 0,

    /// <summary>
    /// 当前处于可访问或生效状态。
    /// </summary>
    Active = 1
}
