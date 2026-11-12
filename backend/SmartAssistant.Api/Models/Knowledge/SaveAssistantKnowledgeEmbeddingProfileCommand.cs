#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 保存嵌入模型选择的命令，携带预期修订号以检测并发修改。
/// </summary>
public sealed record SaveAssistantKnowledgeEmbeddingProfileCommand(
    string ProviderId,
    long ProviderRevision,
    string Model,
    int Dimension,
    long ExpectedRevision);
