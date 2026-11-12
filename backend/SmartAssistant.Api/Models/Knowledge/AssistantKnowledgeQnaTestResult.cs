#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 固定问答测试结果，说明是否命中以及是否调用了模型供应商。
/// </summary>
public sealed record AssistantKnowledgeQnaTestResult(
    string Question,
    bool Matched,
    string? Answer,
    string AnswerMode,
    bool ProviderInvoked);
