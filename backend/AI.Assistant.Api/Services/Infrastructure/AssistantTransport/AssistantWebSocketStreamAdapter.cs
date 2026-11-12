#nullable enable
using System.Net.WebSockets;
using System.Text;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Services.Contracts;

namespace AI.Assistant.Services.Infrastructure.AssistantTransport;

/// <summary>
/// 将事件代理的订阅结果输出为 WebSocket 消息。
/// 与 SSE 共用事件信封，不承担模型或工具执行职责。
/// </summary>
public sealed class AssistantWebSocketStreamAdapter
{
    /// <summary>
    /// 保存用于助手网络套接字流式输出适配器事件编解码的序列化器。
    /// </summary>
    private readonly AssistantRunEventTransportSerializer _serializer;

    /// <summary>
    /// 接收并保存助手网络套接字流式输出适配器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantWebSocketStreamAdapter(AssistantRunEventTransportSerializer serializer)
        => _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

    /// <summary>
    /// 根据已校验的恢复请求订阅运行事件，并通过当前连接输出协议对应的数据帧。
    /// </summary>
    public async Task StreamAsync(
        WebSocket socket,
        IRunEventBroker broker,
        AssistantRunResumeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(broker);
        if (request.Transport != AssistantTransportKind.WebSocket)
            throw new InvalidOperationException("WebSocket adapter requires the WebSocket transport kind.");

        await foreach (var envelope in broker.SubscribeAsync(request, cancellationToken))
        {
            if (socket.State != WebSocketState.Open && socket.State != WebSocketState.CloseReceived)
                break;
            var bytes = Encoding.UTF8.GetBytes(_serializer.SerializeWebSocket(envelope));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
    }
}
