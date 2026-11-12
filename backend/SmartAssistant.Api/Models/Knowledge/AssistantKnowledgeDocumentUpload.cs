#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 知识文档上传参数，包含原始文件内容和文件属性。
/// </summary>
public sealed record AssistantKnowledgeDocumentUpload(
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content);
