#nullable enable
using System.Threading.Channels;
using SmartAssistant.Models.Runtime;

namespace SmartAssistant.Models.Transport;

/// <summary>
/// 封装订阅相关的内部状态或数据，供所属组件完成相应处理。
/// </summary>
internal sealed record AssistantRunEventSubscription(
    IReadOnlyList<AssistantRunEventEnvelope> Events,
    AssistantReplayGap? Gap,
    bool Terminal,
    Channel<AssistantRunEventEnvelope>? Channel);
