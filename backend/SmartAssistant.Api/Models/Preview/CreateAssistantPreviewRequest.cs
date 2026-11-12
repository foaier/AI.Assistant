#nullable enable
namespace SmartAssistant.Models.Preview;

/// <summary>
/// 以预期草稿修订号创建预览的请求。
/// </summary>
public sealed record CreateAssistantPreviewRequest(long ExpectedDraftRevision);
