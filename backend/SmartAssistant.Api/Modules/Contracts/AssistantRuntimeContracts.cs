#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartAssistant.Modules.Contracts;

/// <summary>
/// 冻结的嵌入执行配置，精确绑定供应商修订、模型、维度和索引代次。
/// </summary>
public sealed record AssistantKnowledgeEmbeddingExecutionProfile(
    string ProviderId,
    long ProviderRevision,
    string Model,
    int Dimension,
    long ProfileRevision,
    long Generation)
{
    public string ProviderProfileRef => $"{ProviderId}@{ProviderRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// 区分公开聊天与草稿预览，避免两个运行范围相互访问。
/// </summary>
public enum AssistantExecutionMode
{
    Public = 1,
    Preview = 2
}

/// <summary>
/// 匿名会话的有效、过期或撤销状态。
/// </summary>
public enum AssistantSessionStatus
{
    Active = 1,
    Expired = 2,
    Revoked = 3
}

/// <summary>
/// 对话的有效、归档或过期状态。
/// </summary>
public enum AssistantConversationStatus
{
    Active = 1,
    Archived = 2,
    Expired = 3
}

/// <summary>
/// 用户消息轮次从接收到完成、失败、取消或过期的生命周期状态。
/// </summary>
public enum AssistantTurnStatus
{
    Accepted = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6
}

/// <summary>
/// 模型运行从接收到完成、失败、取消或过期的生命周期状态。
/// </summary>
public enum AssistantRunStatus
{
    Accepted = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6
}

/// <summary>
/// 消息的发送角色：用户、助手或工具。
/// </summary>
public enum AssistantMessageRole
{
    User = 1,
    Assistant = 2,
    Tool = 3
}

/// <summary>
/// 消息生成过程中的待处理、流式输出、完成或失败状态。
/// </summary>
public enum AssistantMessageStatus
{
    Pending = 1,
    Streaming = 2,
    Completed = 3,
    Failed = 4
}

/// <summary>
/// 工具调用从请求、执行到成功、拒绝或失败的状态。
/// </summary>
public enum AssistantToolCallStatus
{
    Requested = 1,
    Running = 2,
    Completed = 3,
    Denied = 4,
    Failed = 5
}

/// <summary>
/// 公开运行事件支持的传输方式。
/// </summary>
public enum AssistantTransportKind
{
    Sse = 1,
    WebSocket = 2
}

/// <summary>
/// 幂等请求的处理结果：创建新操作、重放已有结果或检测到冲突。
/// </summary>
public enum AssistantIdempotencyOutcome
{
    Created = 1,
    Replayed = 2,
    Conflict = 3
}

/// <summary>
/// 模型供应商流式片段类型，用于区分文本、工具参数、结束、用量和错误。
/// </summary>
public enum AssistantProviderChunkKind
{
    TextDelta = 1,
    ToolCallDelta = 2,
    Completed = 3,
    Usage = 4,
    Error = 5
}

/// <summary>
/// 定义前后端共享的运行事件类型字符串。
/// </summary>
public static class AssistantRunEventTypes
{
    public const string RunAccepted = "run.accepted";
    public const string RunStarted = "run.started";
    public const string MessageDelta = "message.delta";
    public const string MessageCompleted = "message.completed";
    public const string ToolCallStarted = "tool.call.started";
    public const string ToolCallCompleted = "tool.call.completed";
    public const string RunCompleted = "run.completed";
    public const string RunFailed = "run.failed";
    public const string RunCancelled = "run.cancelled";
    public const string RunExpired = "run.expired";
    public const string Heartbeat = "heartbeat";
    public const string ReplayGap = "replay.gap";
}

/// <summary>
/// 运行授权范围，绑定助手、租户、组织、发布版本和执行模式。
/// </summary>
public sealed record AssistantScopeSnapshot(
    string AssistantId,
    string TenantId,
    string OrgId,
    string PublishedVersionId,
    AssistantExecutionMode ExecutionMode,
    string? WebSearchConfigRef = null);

/// <summary>
/// 匿名会话的只读状态快照。
/// </summary>
public sealed record AnonymousAssistantSessionSnapshot(
    string Id,
    string AssistantId,
    string TenantId,
    string OrgId,
    string PublishedVersionId,
    string SubjectHash,
    string Locale,
    AssistantExecutionMode ExecutionMode,
    AssistantSessionStatus Status,
    DateTime CreatedAt,
    DateTime LastSeenAt,
    DateTime ExpiresAt);

/// <summary>
/// 对话的只读状态快照，供运行服务和客户端传递对话信息。
/// </summary>
public sealed record AssistantConversationSnapshot(
    string Id,
    string AssistantId,
    string SessionId,
    string TenantId,
    string OrgId,
    string PublishedVersionId,
    string Locale,
    AssistantExecutionMode ExecutionMode,
    AssistantConversationStatus Status,
    long LastTurnNo,
    DateTime CreatedAt,
    DateTime LastActivityAt);

/// <summary>
/// 用户消息轮次的只读状态与请求信息。
/// </summary>
public sealed record AssistantTurnSnapshot(
    string Id,
    string ConversationId,
    string SessionId,
    string AssistantId,
    string TenantId,
    string OrgId,
    string PublishedVersionId,
    string ClientMessageId,
    string RequestHash,
    long TurnNo,
    AssistantExecutionMode ExecutionMode,
    AssistantTurnStatus Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? FailureCode);

/// <summary>
/// 一次助手执行的只读状态、模型信息和事件进度。
/// </summary>
public sealed record AssistantRunSnapshot(
    string Id,
    string TurnId,
    string ConversationId,
    string SessionId,
    string AssistantId,
    string TenantId,
    string OrgId,
    string PublishedVersionId,
    AssistantExecutionMode ExecutionMode,
    string ProviderProfileRef,
    string Model,
    AssistantRunStatus Status,
    long SequenceHead,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? FailureCode,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);

/// <summary>
/// 可传输的消息快照，包含角色、内容和生成状态。
/// </summary>
public sealed record AssistantMessageSnapshot(
    string Id,
    string ConversationId,
    string TurnId,
    string? RunId,
    AssistantMessageRole Role,
    AssistantMessageStatus Status,
    string ContentJson,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? ClientMessageId = null,
    AssistantTurnStatus? TurnStatus = null);

/// <summary>
/// 工具调用的只读快照，记录输入、结果和执行状态。
/// </summary>
public sealed record AssistantToolCallSnapshot(
    string Id,
    string RunId,
    string ToolId,
    string ContractVersion,
    string InputHash,
    AssistantToolCallStatus Status,
    string? OutputJson,
    string? FailureCode,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>
/// 创建公开助手匿名会话的输入及客户端上下文。
/// </summary>
public sealed record CreateAnonymousAssistantSessionCommand(
    string AssistantKey,
    string SubjectHash,
    string Locale,
    AssistantExecutionMode ExecutionMode = AssistantExecutionMode.Public);

/// <summary>
/// 在公开助手的匿名会话中创建对话的命令。
/// </summary>
public sealed record CreateAssistantConversationCommand(
    string AssistantKey,
    string SessionId,
    string Locale);

/// <summary>
/// 公开聊天页面初始化数据，包含展示配置和可用能力。
/// </summary>
public sealed record AssistantPublicBootstrap(
    string AssistantKey,
    string AssistantId,
    string PublishedVersionId,
    string Locale,
    string? DisplayName,
    string? Description,
    string? WelcomeMessage,
    string? HomeIconUrl,
    IReadOnlySet<AssistantTransportKind> SupportedTransports,
    bool AnonymousChatEnabled,
    bool HistoryEnabled,
    AssistantExecutionMode ExecutionMode,
    string PrimaryColor = "#2563eb",
    IReadOnlyList<string>? SuggestedQuestions = null,
    IReadOnlyList<AssistantQuickLink>? QuickLinks = null,
    string? ChatBackgroundColor = null);

/// <summary>
/// 公开聊天页面展示的快捷链接及标题。
/// </summary>
public sealed record AssistantQuickLink(
    string Id,
    string Label,
    string Url,
    string Icon = "link",
    string? IconUrl = null);

/// <summary>
/// 创建对话成功后返回的对话标识与相关状态。
/// </summary>
public sealed record AssistantConversationAccepted(
    string ConversationId,
    string SessionId,
    string PublishedVersionId,
    string Locale);

/// <summary>
/// 提交用户消息的命令，包含对话标识、客户端幂等键和消息内容。
/// </summary>
public sealed record SubmitAssistantTurnCommand(
    string AssistantKey,
    string ConversationId,
    string ClientMessageId,
    string MessageType,
    string Text,
    string Locale);

/// <summary>
/// 轮次受理结果，返回运行标识、事件序号及可用于订阅的流票据。
/// </summary>
public sealed record AssistantTurnAccepted(
    string TurnId,
    string RunId,
    string ConversationId,
    string PublishedVersionId,
    string StreamTicket,
    long AcceptedSequence,
    DateTime ExpiresAt);

/// <summary>
/// 对话消息分页结果及继续查询所需的游标。
/// </summary>
public sealed record AssistantMessagesPage(
    IReadOnlyList<AssistantMessageSnapshot> Items,
    string? NextCursor,
    bool HasMore);

/// <summary>
/// 管理端会话分析列表中的单条会话信息。
/// </summary>
public sealed record AssistantConversationAnalyticsDto(
    string ConversationId,
    string Locale,
    AssistantConversationStatus Status,
    long LastTurnNo,
    DateTime CreatedAt,
    DateTime LastActivityAt);

/// <summary>
/// 会话分析分页结果，包含列表和分页统计。
/// </summary>
public sealed record AssistantConversationsPage(
    IReadOnlyList<AssistantConversationAnalyticsDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// 助手会话与运行结果的汇总统计。
/// </summary>
public sealed record AssistantAnalyticsSummaryDto(
    int ConversationCount,
    int TurnCount,
    int CompletedTurnCount,
    int FailedTurnCount,
    int RunCount,
    int ToolCallCount,
    int CustomQaAnswerCount,
    int KnowledgeAnswerCount,
    int ModelAnswerCount,
    int KnowledgeNoAnswerCount,
    int ToolCallCompletedCount,
    int ToolCallFailedCount,
    int ToolCallDeniedCount,
    double? AverageToolLatencyMs);

/// <summary>
/// 取消运行请求的处理结果和当前状态。
/// </summary>
public sealed record AssistantCancelResult(
    string RunId,
    AssistantRunStatus Status,
    bool Changed);

/// <summary>
/// 说明客户端请求的事件已超出保留窗口，并提供当前可恢复的进度。
/// </summary>
public sealed record AssistantReplayGap(
    string RunId,
    long RequestedAfterSequence,
    long AvailableFromSequence,
    long CurrentSequence,
    AssistantRunStatus PersistedStatus);

/// <summary>
/// 轮次提交的幂等判定及可复用的已有运行信息。
/// </summary>
public sealed record AssistantTurnIdempotencyResult(
    AssistantIdempotencyOutcome Outcome,
    string TurnId,
    string RunId,
    string RequestHash);

/// <summary>
/// 绑定授权范围、运行和传输类型的短期流式订阅凭据。
/// </summary>
public sealed record AssistantStreamTicket(
    string Value,
    string AssistantId,
    string PublishedVersionId,
    string SessionId,
    string ConversationId,
    string TurnId,
    string RunId,
    AssistantExecutionMode ExecutionMode,
    IReadOnlySet<AssistantTransportKind> AllowedTransports,
    DateTime ExpiresAt);

/// <summary>
/// 恢复事件订阅所需的运行、票据和已接收事件序号。
/// </summary>
public sealed record AssistantRunResumeRequest(
    string RunId,
    long AfterSequence,
    AssistantTransportKind Transport);

/// <summary>
/// 统一运行事件信封，供不同传输协议共享类型、顺序和负载格式。
/// </summary>
public sealed record AssistantRunEventEnvelope(
    string EventId,
    string RunId,
    string TurnId,
    long Sequence,
    string Type,
    DateTime OccurredAt,
    JsonElement Payload)
{
    public static AssistantRunEventEnvelope Create<TPayload>(
        string eventId,
        string runId,
        string turnId,
        long sequence,
        string type,
        DateTime occurredAt,
        TPayload payload)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("Event id is required.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Run id is required.", nameof(runId));
        if (string.IsNullOrWhiteSpace(turnId))
            throw new ArgumentException("Turn id is required.", nameof(turnId));
        if (sequence < 1)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must start at one.");
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Event type is required.", nameof(type));

        return new AssistantRunEventEnvelope(
            eventId.Trim(),
            runId.Trim(),
            turnId.Trim(),
            sequence,
            type.Trim(),
            occurredAt,
            JsonSerializer.SerializeToElement(payload));
    }
}

/// <summary>
/// 发送给模型供应商的消息，包含角色、正文及工具调用上下文。
/// </summary>
public sealed record AssistantProviderMessage(
    AssistantMessageRole Role,
    string Content,
    string? Name = null,
    string? ToolCallId = null,
    IReadOnlyList<AssistantProviderToolCall>? ToolCalls = null);

/// <summary>
/// 模型消息中的工具调用请求及其参数。
/// </summary>
public sealed record AssistantProviderToolCall(
    string ToolCallId,
    string Name,
    string ArgumentsJson);

/// <summary>
/// 工具的名称、用途和输入结构，供模型声明与运行时校验使用。
/// </summary>
public sealed record AssistantToolDefinition(
    string ToolId,
    string ContractVersion,
    string Name,
    string Description,
    JsonElement InputSchema);

/// <summary>
/// 一次模型流式调用的请求，包含消息、模型参数和允许使用的工具。
/// </summary>
public sealed record AssistantProviderRequest(
    string ProviderProfileRef,
    string Model,
    IReadOnlyList<AssistantProviderMessage> Messages,
    double? Temperature = null,
    double? TopP = null,
    int? MaxTokens = null,
    IReadOnlyList<AssistantToolDefinition>? Tools = null,
    string? ToolChoice = null,
    bool? ParallelToolCalls = null,
    bool IncludeUsage = true,
    string TenantId = "",
    string OrgId = "",
    string? Instructions = null);

/// <summary>
/// 模型供应商报告的令牌用量。
/// </summary>
public sealed record AssistantProviderUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);

/// <summary>
/// 工具调用参数的流式增量，需按调用标识合并后才能执行。
/// </summary>
public sealed record AssistantProviderToolCallDelta(
    int Index,
    string? ToolCallId,
    string? Name,
    string? ArgumentsDelta);

/// <summary>
/// 模型适配器向编排器返回的标准化错误信息。
/// </summary>
public sealed record AssistantProviderError(
    string Code,
    string Message,
    bool Retryable,
    int? StatusCode = null,
    string? ProviderRequestId = null);

/// <summary>
/// 模型适配器返回的统一流式片段。
/// </summary>
public sealed record AssistantProviderChunk(
    AssistantProviderChunkKind Kind,
    string? TextDelta = null,
    AssistantProviderToolCallDelta? ToolCall = null,
    string? FinishReason = null,
    AssistantProviderUsage? Usage = null,
    AssistantProviderError? Error = null,
    string? ProviderRequestId = null);

/// <summary>
/// 定义模型流式调用接口，使编排器不依赖供应商的传输格式。
/// </summary>
public interface IChatModelProvider
{
    /// <summary>
    /// 发送模型请求并按到达顺序返回文本、工具参数、用量或错误片段，响应调用方取消。
    /// </summary>
    IAsyncEnumerable<AssistantProviderChunk> StreamAsync(
        AssistantProviderRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义助手轮次提交与运行取消的编排契约。
/// </summary>
public interface IAssistantOrchestrator
{
    Task<AssistantTurnAccepted> SubmitTurnAsync(
        SubmitAssistantTurnCommand command,
        CancellationToken cancellationToken = default);

    Task CancelRunAsync(
        string assistantKey,
        string runId,
        string sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 定义运行事件发布与可恢复订阅契约，隐藏具体的事件存储和传输实现。
/// </summary>
public interface IRunEventBroker
{
    /// <summary>
    /// 按运行范围发布有序事件，并维护恢复订阅所需的事件进度。
    /// </summary>
    Task PublishAsync(
        AssistantRunEventEnvelope envelope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 先读取指定序号之后的保留事件，再接收后续事件；窗口缺口按统一契约返回。
    /// </summary>
    IAsyncEnumerable<AssistantRunEventEnvelope> SubscribeAsync(
        AssistantRunResumeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 签发并验证绑定到运行范围和传输类型的短期流票据。
/// </summary>
public interface IStreamTicketService
{
    /// <summary>
    /// 签发绑定执行范围、会话、运行、有效期和允许传输方式的流票据。
    /// </summary>
    Task<AssistantStreamTicket> IssueAsync(
        AssistantScopeSnapshot scope,
        string sessionId,
        string conversationId,
        string turnId,
        string runId,
        IReadOnlySet<AssistantTransportKind> allowedTransports,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验票据签名、有效期、运行标识和传输方式，拒绝不匹配的订阅请求。
    /// </summary>
    Task<AssistantStreamTicket> ValidateAsync(
        string ticket,
        AssistantTransportKind transport,
        AssistantScopeSnapshot scope,
        string sessionId,
        string conversationId,
        string turnId,
        string runId,
        DateTime now,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 根据授权范围和工具契约解析可用定义及对应处理器。
/// </summary>
public interface IAssistantToolRegistry
{
    /// <summary>
    /// 根据工具标识与契约版本读取允许使用的工具定义。
    /// </summary>
    Task<AssistantToolDefinition?> ResolveAsync(
        string toolId,
        string contractVersion,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在运行范围内解析指定工具的处理器，拒绝未注册的工具。
    /// </summary>
    Task<IAssistantToolHandler?> ResolveHandlerAsync(
        string toolId,
        string contractVersion,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 执行已通过授权的工具请求，并返回供模型使用的结果。
/// </summary>
public interface IAssistantToolHandler
{
    string ToolId { get; }
    string ContractVersion { get; }

    Task<JsonElement> ExecuteAsync(
        JsonElement input,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 预留人工转接能力的服务边界。
/// </summary>
public interface IHumanHandoffProvider
{
    Task RequestAsync(
        string runId,
        string reason,
        CancellationToken cancellationToken = default);
}
