#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 工具调用的只读快照，记录输入、结果和执行状态。
/// </summary>
public sealed record AssistantToolCallSnapshot(
    string Id,
    string RunId,
    string ToolId,
    string ContractVersion,
    string InputHash,
    AssistantToolCallStatus Status,
    string? OutputJson,
    string? FailureCode,
    DateTime? CompletedAt);
