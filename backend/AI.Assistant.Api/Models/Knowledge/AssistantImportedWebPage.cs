#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 单个网页导入结果，包含规范来源地址、展示标题和原始 HTML。
/// </summary>
public sealed record AssistantImportedWebPage(string SourceUrl, string Title, string Html);
