#nullable enable
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Contracts;

namespace SmartAssistant.Modules.Infrastructure.AssistantTransport;

/// <summary>
/// 将事件代理的订阅结果输出为 WebSocket 消息。
/// 与 SSE 共用事件信封，不承担模型或工具执行职责。
/// </summary>
public sealed class AssistantWebSocketStreamAdapter
{
    private readonly AssistantRunEventTransportSerializer _serializer;

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
