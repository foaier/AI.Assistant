#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 公开运行事件支持的传输方式。
/// </summary>
public enum AssistantTransportKind
{
    /// <summary>
    /// 使用服务器发送事件传输。
    /// </summary>
    Sse = 1,

    /// <summary>
    /// 使用 WebSocket 双向连接传输。
    /// </summary>
    WebSocket = 2
}
