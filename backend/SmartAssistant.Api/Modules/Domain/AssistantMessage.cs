#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 维护用户、助手及工具消息的正文、关联关系和生成状态。
/// </summary>
public sealed class AssistantMessage
{
    private AssistantMessage() { }

    public string Id { get; private set; } = string.Empty;
    public string ConversationId { get; private set; } = string.Empty;
    public string TurnId { get; private set; } = string.Empty;
    public string? RunId { get; private set; }
    public AssistantMessageRole Role { get; private set; }
    public AssistantMessageStatus Status { get; private set; }
    public string ContentJson { get; private set; } = "{}";
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public static AssistantMessage CreateUser(
        string id,
        string conversationId,
        string turnId,
        string text,
        DateTime now)
        => Create(
            id,
            conversationId,
            turnId,
            null,
            AssistantMessageRole.User,
            AssistantMessageStatus.Completed,
            JsonContent(text),
            now);

    public static AssistantMessage CreateAssistant(
        string id,
        string conversationId,
        string turnId,
        string runId,
        DateTime now)
        => Create(
            id,
            conversationId,
            turnId,
            runId,
            AssistantMessageRole.Assistant,
            AssistantMessageStatus.Streaming,
            "{}",
            now);

    public static AssistantMessage CreateTool(
        string id,
        string conversationId,
        string turnId,
        string runId,
        string contentJson,
        DateTime now)
        => Create(
            id,
            conversationId,
            turnId,
            runId,
            AssistantMessageRole.Tool,
            AssistantMessageStatus.Completed,
            AssistantRuntimeGuard.NormalizeJson(contentJson, "ContentJson"),
            now);

    public static AssistantMessage CreateAssistantToolCalls(
        string id,
        string conversationId,
        string turnId,
        string runId,
        IReadOnlyList<AssistantProviderToolCall> toolCalls,
        DateTime now)
    {
        if (toolCalls is null || toolCalls.Count == 0)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_tool_calls_required");
        var content = JsonSerializer.Serialize(new
        {
            type = "tool_calls",
            toolCalls = toolCalls.Select(call => new
            {
                id = call.ToolCallId,
                name = call.Name,
                arguments = ParseJson(call.ArgumentsJson)
            }).ToArray()
        });
        return Create(
            id,
            conversationId,
            turnId,
            runId,
            AssistantMessageRole.Assistant,
            AssistantMessageStatus.Completed,
            content,
            now);
    }

    public static AssistantMessage Rehydrate(
        string id,
        string conversationId,
        string turnId,
        string? runId,
        AssistantMessageRole role,
        AssistantMessageStatus status,
        string contentJson,
        DateTime createdAt,
        DateTime? completedAt)
        => new()
        {
            Id = id,
            ConversationId = conversationId,
            TurnId = turnId,
            RunId = runId,
            Role = role,
            Status = status,
            ContentJson = contentJson,
            CreatedAt = createdAt,
            CompletedAt = completedAt
        };

    /// <summary>
    /// 仅对允许继续输出的助手消息追加文本，并更新流式生成状态。
    /// </summary>
    public void AppendAssistantText(string text)
    {
        if (Role != AssistantMessageRole.Assistant)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_message_role_mismatch");
        if (Status != AssistantMessageStatus.Streaming)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_message_not_streaming");

        using var document = System.Text.Json.JsonDocument.Parse(ContentJson);
        var current = document.RootElement;
        var currentText = current.TryGetProperty("text", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;
        ContentJson = JsonContent(currentText + (text ?? string.Empty));
    }

    public void Complete(string? contentJson, DateTime now)
    {
        if (Role != AssistantMessageRole.Assistant)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_message_role_mismatch");
        if (Status != AssistantMessageStatus.Streaming)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_message_not_streaming");

        ContentJson = string.IsNullOrWhiteSpace(contentJson)
            ? ContentJson
            : AssistantRuntimeGuard.NormalizeJson(contentJson, "ContentJson");
        Status = AssistantMessageStatus.Completed;
        CompletedAt = now;
    }

    public void Fail(DateTime now)
    {
        if (Role != AssistantMessageRole.Assistant)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_message_role_mismatch");
        if (Status != AssistantMessageStatus.Streaming)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_message_terminal_already_set");
        Status = AssistantMessageStatus.Failed;
        CompletedAt = now;
    }

    private static AssistantMessage Create(
        string id,
        string conversationId,
        string turnId,
        string? runId,
        AssistantMessageRole role,
        AssistantMessageStatus status,
        string contentJson,
        DateTime now)
    {
        if (!Enum.IsDefined(role))
            throw new BizResultException(BusinessCodes.BusinessException, "MessageRole invalid");
        if (!Enum.IsDefined(status))
            throw new BizResultException(BusinessCodes.BusinessException, "MessageStatus invalid");

        return new AssistantMessage
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            ConversationId = AssistantRuntimeGuard.Required(conversationId, "ConversationId"),
            TurnId = AssistantRuntimeGuard.Required(turnId, "TurnId"),
            RunId = string.IsNullOrWhiteSpace(runId) ? null : runId.Trim(),
            Role = role,
            Status = status,
            ContentJson = AssistantRuntimeGuard.NormalizeJson(contentJson, "ContentJson"),
            CreatedAt = now,
            CompletedAt = status == AssistantMessageStatus.Completed ? now : null
        };
    }

    private static string JsonContent(string text)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "text",
            text = AssistantRuntimeGuard.NormalizeRequestText(text)
        });

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
