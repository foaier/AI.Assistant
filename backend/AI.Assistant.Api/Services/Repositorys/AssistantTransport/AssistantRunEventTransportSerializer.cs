#nullable enable
using System.Text.Json;
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Services.Repositorys.AssistantTransport;

/// <summary>
/// 将统一运行事件编码为 WebSocket JSON 或 SSE 数据帧。
/// </summary>
public sealed class AssistantRunEventTransportSerializer
{
    /// <summary>
    /// 保存用于助手执行事件传输序列化器事件编解码的序列化器。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 将运行事件序列化为 WebSocket 使用的 JSON 信封。
    /// </summary>
    public string SerializeWebSocket(AssistantRunEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    /// <summary>
    /// 将运行事件转换为包含事件标识、类型和 JSON 负载的 SSE 帧。
    /// </summary>
    public string SerializeSse(AssistantRunEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var json = SerializeWebSocket(envelope);
        return $"id: {envelope.Sequence}\nevent: {envelope.Type}\ndata: {json}\n\n";
    }
}
