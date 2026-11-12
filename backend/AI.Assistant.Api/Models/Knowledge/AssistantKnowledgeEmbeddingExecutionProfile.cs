#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 冻结的嵌入执行配置，精确绑定供应商修订、模型、维度和索引代次。
/// </summary>
public sealed record AssistantKnowledgeEmbeddingExecutionProfile(
    string ProviderId,
    long ProviderRevision,
    string Model,
    int Dimension,
    long ProfileRevision,
    long Generation)
{
    /// <summary>
    /// 保存本次执行使用的供应商配置及其修订引用。
    /// </summary>
    public string ProviderProfileRef => $"{ProviderId}@{ProviderRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
