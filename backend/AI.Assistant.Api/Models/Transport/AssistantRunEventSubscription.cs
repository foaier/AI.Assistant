#nullable enable
using System.Threading.Channels;
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Models.Transport;

/// <summary>
/// 封装订阅相关的内部状态或数据，供所属组件完成相应处理。
/// </summary>
internal sealed record AssistantRunEventSubscription(
    IReadOnlyList<AssistantRunEventEnvelope> Events,
    AssistantReplayGap? Gap,
    bool Terminal,
    Channel<AssistantRunEventEnvelope>? Channel);
