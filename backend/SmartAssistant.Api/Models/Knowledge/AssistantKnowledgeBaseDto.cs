#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 知识库概览，包含名称、问答数量、文档数量和更新时间。
/// </summary>
public sealed record AssistantKnowledgeBaseDto(
    string Id,
    string Name,
    string? Description,
    int QnaCount,
    int DocumentCount);
