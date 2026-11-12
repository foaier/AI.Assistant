#nullable enable
using System;
using System.Collections.Generic;

namespace SmartAssistant.Modules.Contracts;

/// <summary>
/// 草稿预览创建结果，包含预览会话、使用的草稿修订和有效期。
/// </summary>
public sealed record AssistantPreviewCreated(string Id, string ConversationId, long DraftRevision, DateTime ExpiresAt, string Model);
/// <summary>
/// 以预期草稿修订号创建预览的请求。
/// </summary>
public sealed record CreateAssistantPreviewRequest(long ExpectedDraftRevision);
/// <summary>
/// 向草稿预览提交消息的请求，包含客户端幂等消息标识。
/// </summary>
public sealed record SubmitAssistantPreviewRequest(string ClientMessageId, string Text);
/// <summary>
/// 预览消息列表与运行状态，包含可展示的失败码。
/// </summary>
public sealed record AssistantPreviewMessages(IReadOnlyList<AssistantMessageSnapshot> Items, bool Running, string? FailureCode);
