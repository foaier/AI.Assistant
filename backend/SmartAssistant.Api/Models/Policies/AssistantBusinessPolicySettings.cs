#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 业务模型策略，约束供应商、模型、提示词、知识能力和可用工具。
/// </summary>
public sealed record AssistantBusinessPolicySettings(
    string Name,
    string BusinessCode,
    string ProviderId,
    long ProviderRevision,
    string Model,
    string Instructions,
    double Temperature = 0.2,
    int MaxOutputTokens = 2048,
    bool KnowledgeEnabled = true,
    int RetrievalLimit = 5,
    bool RequireEvidence = true,
    string NoAnswerMessage = "暂时没有找到可靠的信息，请联系工作人员。",
    bool WebSearchEnabled = false,
    bool HumanHandoffEnabled = false,
    IReadOnlyList<AssistantToolBinding>? Tools = null,
    bool ModelSupportsTools = false);
