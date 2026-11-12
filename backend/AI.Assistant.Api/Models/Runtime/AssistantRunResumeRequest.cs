#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 恢复事件订阅所需的运行、票据和已接收事件序号。
/// </summary>
public sealed record AssistantRunResumeRequest(
    string RunId,
    long AfterSequence,
    AssistantTransportKind Transport);
