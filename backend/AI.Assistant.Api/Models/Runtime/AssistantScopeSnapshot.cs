#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 运行授权范围，绑定助手、发布版本和执行模式。
/// </summary>
public sealed record AssistantScopeSnapshot(
    string AssistantId,
    string PublishedVersionId,
    AssistantExecutionMode ExecutionMode,
    string? WebSearchConfigRef = null);
