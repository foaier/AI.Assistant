#nullable enable
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AI.Assistant.Models.Common;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Infrastructure;
using AI.Assistant.Services.Application;
using AI.Assistant.Services.Contracts;
using AI.Assistant.Services.Repositorys.AssistantTransport;

namespace AI.Assistant.Controllers;

/// <summary>
/// 提供匿名公开聊天接口，以及受票据保护的 SSE 和 WebSocket 订阅入口。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/assistants/{assistantKey}")]
public sealed class PublicAssistantsController : ControllerBase
{
    /// <summary>
    /// 保存助手公开运行时服务依赖，供公开助手控制器执行相关操作。
    /// </summary>
    private readonly IAssistantPublicRuntimeService _runtime;
    /// <summary>
    /// 保存执行事件事件代理依赖，供公开助手控制器执行相关操作。
    /// </summary>
    private readonly IRunEventBroker _broker;
    /// <summary>
    /// 保存流式输出票据服务依赖，供公开助手控制器执行相关操作。
    /// </summary>
    private readonly IStreamTicketService _tickets;
    /// <summary>
    /// 保存助手SSE流式输出适配器依赖，供公开助手控制器执行相关操作。
    /// </summary>
    private readonly AssistantSseStreamAdapter _sse;
    /// <summary>
    /// 保存助手网络套接字流式输出适配器依赖，供公开助手控制器执行相关操作。
    /// </summary>
    private readonly AssistantWebSocketStreamAdapter _webSocket;

    /// <summary>
    /// 接收并保存公开助手控制器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public PublicAssistantsController(
        IAssistantPublicRuntimeService runtime,
        IRunEventBroker broker,
        IStreamTicketService tickets,
        AssistantSseStreamAdapter sse,
        AssistantWebSocketStreamAdapter webSocket)
    {
        _runtime = runtime;
        _broker = broker;
        _tickets = tickets;
        _sse = sse;
        _webSocket = webSocket;
    }

    /// <summary>
    /// 读取公开助手初始化数据，供聊天页面展示欢迎信息与能力。
    /// </summary>
    [HttpGet("bootstrap")]
    public async Task<BizResult<AssistantPublicBootstrap>> BootstrapAsync(string assistantKey, [FromQuery] string? locale, CancellationToken cancellationToken)
        => BizResult.BusinessSuccess(await _runtime.GetBootstrapAsync(assistantKey, locale ?? "zh-cn", cancellationToken));

    /// <summary>
    /// 为公开助手创建匿名会话并返回访问令牌。
    /// </summary>
    [HttpPost("sessions")]
    public async Task<BizResult<AssistantSessionCreated>> CreateSessionAsync(
        string assistantKey,
        [FromBody] CreatePublicSessionRequest request,
        CancellationToken cancellationToken)
        => BizResult.BusinessSuccess(await _runtime.CreateSessionAsync(
            new CreateAnonymousAssistantSessionCommand(
                assistantKey,
                string.IsNullOrWhiteSpace(request.SubjectHash) ? "anonymous" : request.SubjectHash,
                request.Locale ?? "zh-cn",
                AssistantExecutionMode.Public), cancellationToken));

    /// <summary>
    /// 读取匿名会话凭据并请求创建对话。
    /// </summary>
    [HttpPost("conversations")]
    public async Task<BizResult<AssistantConversationAccepted>> CreateConversationAsync(string assistantKey, [FromBody] CreatePublicConversationRequest request, CancellationToken cancellationToken)
    {
        var sessionId = RequireSessionId();
        var bearer = RequireBearerToken();
        return BizResult.BusinessSuccess(await _runtime.CreateConversationAsync(
            new CreateAssistantConversationCommand(assistantKey, sessionId, request.Locale ?? "zh-cn"),
            sessionId, bearer, cancellationToken));
    }

    /// <summary>
    /// 读取会话凭据并提交用户消息，返回轮次和流式订阅信息。
    /// </summary>
    [HttpPost("conversations/{conversationId}/turns")]
    public async Task<BizResult<AssistantTurnAccepted>> SubmitTurnAsync(
        string assistantKey,
        string conversationId,
        [FromBody] SubmitPublicTurnRequest request,
        CancellationToken cancellationToken)
    {
        var sessionId = RequireSessionId();
        var bearer = RequireBearerToken();
        return BizResult.BusinessSuccess(await _runtime.SubmitTurnAsync(
            new SubmitAssistantTurnCommand(
                assistantKey,
                conversationId,
                request.ClientMessageId,
                request.MessageType ?? "text",
                request.Text,
                request.Locale ?? "zh-cn"),
            sessionId, bearer, cancellationToken));
    }

    /// <summary>
    /// 读取会话凭据并请求取消指定运行。
    /// </summary>
    [HttpPost("runs/{runId}/cancel")]
    public async Task<BizResult<AssistantCancelResult>> CancelRunAsync(string assistantKey, string runId, CancellationToken cancellationToken)
    {
        var sessionId = RequireSessionId();
        var bearer = RequireBearerToken();
        return BizResult.BusinessSuccess(await _runtime.CancelRunAsync(
            assistantKey, runId, sessionId, bearer, cancellationToken));
    }

    /// <summary>
    /// 读取会话凭据后分页返回对话消息。
    /// </summary>
    [HttpGet("conversations/{conversationId}/messages")]
    public async Task<BizResult<AssistantMessagesPage>> MessagesAsync(
        string assistantKey,
        string conversationId,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = RequireSessionId();
        var bearer = RequireBearerToken();
        return BizResult.BusinessSuccess(await _runtime.GetMessagesAsync(
            assistantKey, conversationId, sessionId, bearer, limit, cursor, cancellationToken));
    }

    /// <summary>
    /// 验证会话和流票据后通过 SSE 输出运行事件。
    /// </summary>
    [HttpGet("runs/{runId}/events")]
    [Produces("text/event-stream")]
    public async Task EventsAsync(
        string assistantKey,
        string runId,
        [FromQuery] string ticket,
        [FromQuery] long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        var sessionId = RequireSessionId();
        var bearer = RequireBearerToken();
        var conversationId = RequireQuery("conversationId");
        var validated = await _runtime.ValidateStreamTicketAsync(
            assistantKey, conversationId, sessionId, bearer, runId, ticket,
            AssistantTransportKind.Sse, cancellationToken);
        await _sse.StreamAsync(HttpContext.Response, _broker,
            new AssistantRunResumeRequest(validated.RunId, afterSequence, AssistantTransportKind.Sse),
            cancellationToken);
    }

    /// <summary>
    /// 从 WebSocket 首帧读取凭据并验证授权，再输出运行事件；凭据不放入 URL。
    /// </summary>
    [HttpGet("runs/{runId}/events/ws")]
    public async Task EventsWebSocketAsync(
        string assistantKey,
        string runId,
        [FromQuery] string ticket,
        [FromQuery] long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_websocket_required");
        var conversationId = RequireQuery("conversationId");
        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        AssistantStreamTicket validated;
        try
        {
            /*
             * 浏览器 WebSocket 无法设置 Authorization 请求头。
             * 凭据仅通过首帧传递，避免出现在 URL 中。
             */
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var buffer = new byte[8192];
            var count = 0;
            ValueWebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer.AsMemory(count), handshake.Token);
                if (received.MessageType != WebSocketMessageType.Text || received.Count == 0)
                    throw new JsonException();
                count += received.Count;
                if (count == buffer.Length && !received.EndOfMessage) throw new JsonException();
            } while (!received.EndOfMessage);
            using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
            var sessionId = json.RootElement.GetProperty("sessionId").GetString() ?? "";
            var bearer = json.RootElement.GetProperty("bearerToken").GetString() ?? "";
            validated = await _runtime.ValidateStreamTicketAsync(assistantKey, conversationId, sessionId, bearer, runId, ticket,
                AssistantTransportKind.WebSocket, handshake.Token);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException
            or BizResultException or OperationCanceledException or WebSocketException)
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "session_unavailable", CancellationToken.None);
            return;
        }
        await _webSocket.StreamAsync(socket, _broker,
            new AssistantRunResumeRequest(validated.RunId, afterSequence, AssistantTransportKind.WebSocket),
            cancellationToken);
    }

    /// <summary>
    /// 读取并校验当前请求绑定的会话标识；标识缺失时拒绝继续处理。
    /// </summary>
    private string RequireSessionId()
        => Request.Headers.TryGetValue("X-Assistant-Session-Id", out var value)
           && !string.IsNullOrWhiteSpace(value)
            ? value.ToString().Trim()
            : throw new BizResultException(BusinessCodes.Forbidden, "assistant_session_required");

    /// <summary>
    /// 读取并校验请求中的 Bearer 访问令牌；令牌缺失或格式错误时拒绝继续处理。
    /// </summary>
    private string RequireBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && header.Length > prefix.Length
            ? header[prefix.Length..].Trim()
            : throw new BizResultException(BusinessCodes.Forbidden, "assistant_bearer_required");
    }

    /// <summary>
    /// 读取并规范化必填检索文本；查询为空时返回输入校验错误。
    /// </summary>
    private string RequireQuery(string name)
        => Request.Query.TryGetValue(name, out var value)
           && !string.IsNullOrWhiteSpace(value)
            ? value.ToString().Trim()
            : throw new BizResultException(BusinessCodes.BusinessException, $"{name}_required");
}
