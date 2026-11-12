#nullable enable
using System.Text.Json;
using AI.Assistant.Models.AI;
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Services.Contracts;

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
    /// <summary>
    /// 受理并提交轮次，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantTurnAccepted> SubmitTurnAsync(
        SubmitAssistantTurnCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求取消指定运行，并将取消信号传递到正在执行的模型调用。
    /// </summary>
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
    /// <summary>
    /// 关联当前使用的工具定义。
    /// </summary>
    string ToolId { get; }
    /// <summary>
    /// 表示助手工具处理器的契约版本信息。
    /// </summary>
    string ContractVersion { get; }

    /// <summary>
    /// 执行助手工具操作，并响应调用方的取消请求。
    /// </summary>
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
    /// <summary>
    /// 向人工接管服务提交运行标识和转接原因，并响应调用方的取消请求。
    /// </summary>
    Task RequestAsync(
        string runId,
        string reason,
        CancellationToken cancellationToken = default);
}
