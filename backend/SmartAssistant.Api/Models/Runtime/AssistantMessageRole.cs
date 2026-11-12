#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 消息的发送角色：用户、助手或工具。
/// </summary>
public enum AssistantMessageRole
{
    /// <summary>
    /// 用户发送的消息。
    /// </summary>
    User = 1,

    /// <summary>
    /// 助手生成的消息。
    /// </summary>
    Assistant = 2,

    /// <summary>
    /// 工具执行产生的消息。
    /// </summary>
    Tool = 3
}
