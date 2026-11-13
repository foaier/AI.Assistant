#nullable enable
using System.Text.Json;
using AI.Assistant.Models.AI;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Infrastructure;

namespace AI.Assistant.Services.Domain;

/// <summary>
/// 维护用户、助手及工具消息的正文、关联关系和生成状态。
/// </summary>
public sealed class AssistantMessage
{
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AssistantMessage() { }

    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此消息、轮次或运行所属的对话。
    /// </summary>
    public string ConversationId { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此记录所属的用户请求轮次。
    /// </summary>
    public string TurnId { get; private set; } = string.Empty;
    /// <summary>
    /// 关联产生此事件、消息或工具调用的执行运行。
    /// </summary>
    public string? RunId { get; private set; }
    /// <summary>
    /// 区分用户、助手和工具消息。
    /// </summary>
    public AssistantMessageRole Role { get; private set; }
    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    public AssistantMessageStatus Status { get; private set; }
    /// <summary>
    /// 保存消息正文及其附加字段的 JSON 内容。
    /// </summary>
    public string ContentJson { get; private set; } = "{}";
    /// <summary>
    /// 记录处理完成或进入终态的时间。
    /// </summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// 创建用户角色的消息，并保存请求正文及关联轮次。
    /// </summary>
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

    /// <summary>
    /// 创建助手角色的消息，用于保存模型或直接回答的正文。
    /// </summary>
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

    /// <summary>
    /// 创建工具角色的消息，用于保存指定工具调用的执行结果。
    /// </summary>
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

    /// <summary>
    /// 创建包含模型工具调用定义的助手消息。
    /// </summary>
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

    /// <summary>
    /// 从持久化数据还原助手消息的标识、访问范围和生命周期状态。
    /// </summary>
    public static AssistantMessage Rehydrate(
        string id,
        string conversationId,
        string turnId,
        string? runId,
        AssistantMessageRole role,
        AssistantMessageStatus status,
        string contentJson,
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

    /// <summary>
    /// 将助手消息标记为成功完成，并记录完成时间。
    /// </summary>
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

    /// <summary>
    /// 将助手消息标记为失败，并保留失败状态供调用方查询。
    /// </summary>
    public void Fail(DateTime now)
    {
        if (Role != AssistantMessageRole.Assistant)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_message_role_mismatch");
        if (Status != AssistantMessageStatus.Streaming)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_message_terminal_already_set");
        Status = AssistantMessageStatus.Failed;
        CompletedAt = now;
    }

    /// <summary>
    /// 根据校验后的输入创建助手消息领域对象，并初始化其状态和时间字段。
    /// </summary>
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
            CompletedAt = status == AssistantMessageStatus.Completed ? now : null
        };
    }

    /// <summary>
    /// 将消息正文序列化为稳定 JSON，并统一空值处理。
    /// </summary>
    private static string JsonContent(string text)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "text",
            text = AssistantRuntimeGuard.NormalizeRequestText(text)
        });

    /// <summary>
    /// 校验并解析消息 JSON 内容，返回格式有效的规范文本。
    /// </summary>
    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
