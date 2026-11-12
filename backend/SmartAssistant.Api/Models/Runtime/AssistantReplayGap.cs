#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 说明客户端请求的事件已超出保留窗口，并提供当前可恢复的进度。
/// </summary>
public sealed record AssistantReplayGap(
    string RunId,
    long RequestedAfterSequence,
    long AvailableFromSequence,
    long CurrentSequence,
    AssistantRunStatus PersistedStatus);
