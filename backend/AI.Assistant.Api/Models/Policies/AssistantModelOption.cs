#nullable enable
namespace AI.Assistant.Models.Policies;

/// <summary>
/// 模型供应商暴露的模型能力，包括用途、工具调用支持和嵌入维度。
/// </summary>
public sealed record AssistantModelOption(
    string Id,
    string Purpose,
    bool SupportsTools = false,
    int? Dimension = null);
