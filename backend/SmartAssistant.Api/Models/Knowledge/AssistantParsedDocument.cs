#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 文档解析结果，包含可分块的正文和解析器版本。
/// </summary>
public sealed record AssistantParsedDocument(
    string Text,
    string ParserVersion);
