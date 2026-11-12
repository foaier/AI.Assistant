#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 定义前后端共享的运行事件类型字符串。
/// </summary>
public static class AssistantRunEventTypes
{
    /// <summary>
    /// 定义执行已接受事件的协议类型名称。
    /// </summary>
    public const string RunAccepted = "run.accepted";

    /// <summary>
    /// 定义执行开始事件的协议类型名称。
    /// </summary>
    public const string RunStarted = "run.started";

    /// <summary>
    /// 定义消息增量事件的协议类型名称。
    /// </summary>
    public const string MessageDelta = "message.delta";

    /// <summary>
    /// 定义消息已完成事件的协议类型名称。
    /// </summary>
    public const string MessageCompleted = "message.completed";

    /// <summary>
    /// 定义工具调用开始事件的协议类型名称。
    /// </summary>
    public const string ToolCallStarted = "tool.call.started";

    /// <summary>
    /// 定义工具调用已完成事件的协议类型名称。
    /// </summary>
    public const string ToolCallCompleted = "tool.call.completed";

    /// <summary>
    /// 定义执行已完成事件的协议类型名称。
    /// </summary>
    public const string RunCompleted = "run.completed";

    /// <summary>
    /// 定义执行失败事件的协议类型名称。
    /// </summary>
    public const string RunFailed = "run.failed";

    /// <summary>
    /// 定义执行已取消事件的协议类型名称。
    /// </summary>
    public const string RunCancelled = "run.cancelled";

    /// <summary>
    /// 定义执行已过期事件的协议类型名称。
    /// </summary>
    public const string RunExpired = "run.expired";

    /// <summary>
    /// 定义心跳事件的协议类型名称。
    /// </summary>
    public const string Heartbeat = "heartbeat";

    /// <summary>
    /// 定义回放事件的协议类型名称。
    /// </summary>
    public const string ReplayGap = "replay.gap";
}
