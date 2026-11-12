#nullable enable
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Models.Transport;

/// <summary>
/// 事件回放结果，返回可用事件或说明已超出保留窗口的缺口信息。
/// </summary>
public sealed record AssistantRunEventReplayResult(
    IReadOnlyList<AssistantRunEventEnvelope> Events,
    AssistantReplayGap? Gap);
