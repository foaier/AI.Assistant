#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 取消运行请求的处理结果和当前状态。
/// </summary>
public sealed record AssistantCancelResult(
    string RunId,
    AssistantRunStatus Status,
    bool Changed);
