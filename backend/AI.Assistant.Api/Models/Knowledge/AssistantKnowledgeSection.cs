#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 封装分段相关的内部状态或数据，供所属组件完成相应处理。
/// </summary>
internal sealed record AssistantKnowledgeSection(
    string Path,
    string Text);
