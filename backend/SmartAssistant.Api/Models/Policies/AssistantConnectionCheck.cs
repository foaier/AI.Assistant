#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 模型供应商连接检查结果及诊断信息。
/// </summary>
public sealed record AssistantConnectionCheck(
    bool Success,
    string Message,
    DateTime CheckedAt);
