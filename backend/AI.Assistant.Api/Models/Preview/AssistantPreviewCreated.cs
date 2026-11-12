#nullable enable
namespace AI.Assistant.Models.Preview;

/// <summary>
/// 草稿预览创建结果，包含预览会话、使用的草稿修订和有效期。
/// </summary>
public sealed record AssistantPreviewCreated(
    string Id,
    string ConversationId,
    long DraftRevision,
    DateTime ExpiresAt,
    string Model);
