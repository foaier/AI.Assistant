#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 创建知识库的 HTTP 请求体。
/// </summary>
public sealed class CreateKnowledgeBaseRequest
{
    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 记录此资源的可选说明。
    /// </summary>
    public string? Description { get; set; }
}
