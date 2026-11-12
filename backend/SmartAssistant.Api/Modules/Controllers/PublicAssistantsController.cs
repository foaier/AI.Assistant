#nullable enable
using System;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Infrastructure.AssistantTransport;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Modules.Controllers;

/// <summary>
/// 提供匿名公开聊天接口，以及受票据保护的 SSE 和 WebSocket 订阅入口。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/assistants/{assistantKey}")]
public sealed class PublicAssistantsController : ControllerBase
{
    private readonly IAssistantPublicRuntimeService _runtime;
    private readonly IRunEventBroker _broker;
    private readonly IStreamTicketService _tickets;
    private readonly AssistantSseStreamAdapter _sse;
    private readonly AssistantWebSocketStreamAdapter _webSocket;

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
    public async Task<BizResult<AssistantPublicBootstrap>> BootstrapAsync(
        string assistantKey,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
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
    public async Task<BizResult<AssistantConversationAccepted>> CreateConversationAsync(
        string assistantKey,
        [FromBody] CreatePublicConversationRequest request,
        CancellationToken cancellationToken)
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
    public async Task<BizResult<AssistantCancelResult>> CancelRunAsync(
        string assistantKey,
        string runId,
        CancellationToken cancellationToken)
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

    private string RequireSessionId()
        => Request.Headers.TryGetValue("X-Assistant-Session-Id", out var value)
           && !string.IsNullOrWhiteSpace(value)
            ? value.ToString().Trim()
            : throw new BizResultException(BusinessCodes.Forbidden, "assistant_session_required");

    private string RequireBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && header.Length > prefix.Length
            ? header[prefix.Length..].Trim()
            : throw new BizResultException(BusinessCodes.Forbidden, "assistant_bearer_required");
    }

    private string RequireQuery(string name)
        => Request.Query.TryGetValue(name, out var value)
           && !string.IsNullOrWhiteSpace(value)
            ? value.ToString().Trim()
            : throw new BizResultException(BusinessCodes.BusinessException, $"{name}_required");
}

/// <summary>
/// 公开聊天创建匿名会话时提交的客户端信息。
/// </summary>
public sealed class CreatePublicSessionRequest
{
    public string? SubjectHash { get; set; }
    public string? Locale { get; set; }
}

/// <summary>
/// 公开聊天创建对话时提交的语言设置。
/// </summary>
public sealed class CreatePublicConversationRequest
{
    public string? Locale { get; set; }
}

/// <summary>
/// 公开聊天提交消息的请求体，包含客户端幂等键、消息类型、文本和语言。
/// </summary>
public sealed class SubmitPublicTurnRequest
{
    public string ClientMessageId { get; set; } = string.Empty;
    public string? MessageType { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Locale { get; set; }
}
