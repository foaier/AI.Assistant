#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 解析后的可执行业务策略，绑定确定的模型供应商修订和模型能力。
/// </summary>
public sealed record AssistantResolvedPolicy(
    string PolicyId,
    long PolicyRevision,
    string BusinessCode,
    string ProviderProfileRef,
    string ProviderName,
    string Model,
    string Instructions,
    double Temperature,
    int MaxOutputTokens,
    bool KnowledgeEnabled,
    int RetrievalLimit,
    bool RequireEvidence,
    string NoAnswerMessage,
    IReadOnlyList<AssistantToolBinding>? Tools = null,
    bool ModelSupportsTools = false);
