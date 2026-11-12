#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 区分公开聊天与草稿预览，避免两个运行范围相互访问。
/// </summary>
public enum AssistantExecutionMode
{
    /// <summary>
    /// 对外公开执行模式。
    /// </summary>
    Public = 1,

    /// <summary>
    /// 管理端草稿预览执行模式。
    /// </summary>
    Preview = 2
}
