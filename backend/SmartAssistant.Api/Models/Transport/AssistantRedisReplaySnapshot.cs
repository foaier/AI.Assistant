#nullable enable
using SmartAssistant.Models.Runtime;

namespace SmartAssistant.Models.Transport;

/// <summary>
/// 封装回放快照相关的内部状态或数据，供所属组件完成相应处理。
/// </summary>
internal sealed record AssistantRedisReplaySnapshot(
    IReadOnlyList<AssistantRunEventEnvelope> Events,
    string TurnId,
    bool Terminal,
    AssistantReplayGap? Gap);
