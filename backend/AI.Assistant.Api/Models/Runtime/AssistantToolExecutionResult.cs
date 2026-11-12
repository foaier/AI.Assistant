#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 工具执行后供编排器继续构建模型上下文的结果。
/// </summary>
internal sealed record AssistantToolExecutionResult(
    string OutputJson,
    bool Succeeded);
