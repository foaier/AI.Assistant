#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Infrastructure.AssistantTransport;

/// <summary>
/// 对绑定运行范围的流票据进行签名与验证。
/// 签名密钥由服务端注入，不写入公开契约或日志负载。
/// </summary>
public sealed class SignedAssistantStreamTicketService : IStreamTicketService
{
    /// <summary>
    /// 保存用于助手流式输出票据服务事件编解码的序列化器。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 保存助手流式输出票据服务在处理签名密钥时所需的数据或状态。
    /// </summary>
    private readonly byte[] _signingKey;

    /// <summary>
    /// 接收并保存助手流式输出票据服务执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public SignedAssistantStreamTicketService(ReadOnlySpan<byte> signingKey)
    {
        if (signingKey.Length < 32)
            throw new ArgumentException("Stream-ticket signing key must be at least 32 bytes.", nameof(signingKey));
        _signingKey = signingKey.ToArray();
    }

    /// <summary>
    /// 签发绑定执行范围、会话、运行、有效期和允许传输方式的流票据。
    /// </summary>
    public Task<AssistantStreamTicket> IssueAsync(
        AssistantScopeSnapshot scope,
        string sessionId,
        string conversationId,
        string turnId,
        string runId,
        IReadOnlySet<AssistantTransportKind> allowedTransports,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(allowedTransports);
        if (allowedTransports.Count == 0)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_stream_transport_required");

        var now = DateTime.UtcNow;
        if (expiresAt <= now)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_stream_ticket_expired");

        var payload = new TicketPayload
        {
            Version = 1,
            Purpose = "assistant.run.stream",
            AssistantId = Required(scope.AssistantId, "AssistantId"),
            PublishedVersionId = Required(scope.PublishedVersionId, "PublishedVersionId"),
            SessionId = Required(sessionId, "SessionId"),
            ConversationId = Required(conversationId, "ConversationId"),
            TurnId = Required(turnId, "TurnId"),
            RunId = Required(runId, "RunId"),
            ExecutionMode = scope.ExecutionMode,
            AllowedTransports = allowedTransports.OrderBy(item => item).ToArray(),
            ExpiresAtUnixSeconds = new DateTimeOffset(expiresAt.ToUniversalTime()).ToUnixTimeSeconds(),
            Nonce = Base64Url(RandomNumberGenerator.GetBytes(24))
        };

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var payloadPart = Base64Url(payloadBytes);
        var signaturePart = Base64Url(HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(payloadPart)));
        var value = $"{payloadPart}.{signaturePart}";
        return Task.FromResult(new AssistantStreamTicket(
            value,
            payload.AssistantId,
            payload.PublishedVersionId,
            payload.SessionId,
            payload.ConversationId,
            payload.TurnId,
            payload.RunId,
            payload.ExecutionMode,
            new HashSet<AssistantTransportKind>(payload.AllowedTransports),
            DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnixSeconds).UtcDateTime));
    }

    /// <summary>
    /// 校验票据签名、有效期、运行标识和传输方式，拒绝不匹配的订阅请求。
    /// </summary>
    public Task<AssistantStreamTicket> ValidateAsync(
        string ticket,
        AssistantTransportKind transport,
        AssistantScopeSnapshot scope,
        string sessionId,
        string conversationId,
        string turnId,
        string runId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope);
        var payload = DecodeAndVerify(ticket);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnixSeconds).UtcDateTime;

        if (payload.Version != 1 || !string.Equals(payload.Purpose, "assistant.run.stream", StringComparison.Ordinal))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_invalid");
        if (now.ToUniversalTime() >= expiresAt)
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_expired");
        if (!payload.AllowedTransports.Contains(transport))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_transport_not_allowed");
        if (!string.Equals(payload.AssistantId, scope.AssistantId, StringComparison.Ordinal)
            || !string.Equals(payload.PublishedVersionId, scope.PublishedVersionId, StringComparison.Ordinal)
            || payload.ExecutionMode != scope.ExecutionMode
            || !string.Equals(payload.SessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(payload.ConversationId, conversationId, StringComparison.Ordinal)
            || !string.Equals(payload.TurnId, turnId, StringComparison.Ordinal)
            || !string.Equals(payload.RunId, runId, StringComparison.Ordinal))
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_scope_mismatch");
        }

        return Task.FromResult(new AssistantStreamTicket(
            ticket,
            payload.AssistantId,
            payload.PublishedVersionId,
            payload.SessionId,
            payload.ConversationId,
            payload.TurnId,
            payload.RunId,
            payload.ExecutionMode,
            new HashSet<AssistantTransportKind>(payload.AllowedTransports),
            expiresAt));
    }

    /// <summary>
    /// 解析并验证签名、有效期、范围与传输权限后返回流票据内容。
    /// </summary>
    private TicketPayload DecodeAndVerify(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_invalid");

        var parts = ticket.Split('.', StringSplitOptions.None);
        if (parts.Length != 2)
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_invalid");

        byte[] payloadBytes;
        byte[] suppliedSignature;
        try
        {
            payloadBytes = FromBase64Url(parts[0]);
            suppliedSignature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_invalid");
        }

        var expectedSignature = HMACSHA256.HashData(
            _signingKey,
            Encoding.UTF8.GetBytes(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, suppliedSignature))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_invalid");

        try
        {
            var payload = JsonSerializer.Deserialize<TicketPayload>(payloadBytes, JsonOptions)
                ?? throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_invalid");
            if (string.IsNullOrWhiteSpace(payload.Nonce))
                throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_invalid");
            return payload;
        }
        catch (JsonException)
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_stream_ticket_invalid");
        }
    }

    /// <summary>
    /// 校验字符串必填，并在返回前去除首尾空白。
    /// </summary>
    private static string Required(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();

    /// <summary>
    /// 将字节序列编码为不含 URL 保留字符的 Base64 表示。
    /// </summary>
    private static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>
    /// 校验并解码 URL 安全格式的 Base64 字符串。
    /// </summary>
    private static byte[] FromBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Invalid base64url length.")
        };
        var bytes = Convert.FromBase64String(normalized);
        var canonical = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        if (!string.Equals(canonical, value, StringComparison.Ordinal))
            throw new FormatException("Non-canonical base64url value.");
        return bytes;
    }

    /// <summary>
    /// 封装票据相关的内部状态或数据，供所属组件完成相应处理。
    /// </summary>
    private sealed class TicketPayload
    {
        /// <summary>
        /// 表示票据的版本信息。
        /// </summary>
        public int Version { get; init; }
        /// <summary>
        /// 记录流票据的用途，以便校验其允许执行的操作。
        /// </summary>
        public string Purpose { get; init; } = string.Empty;
        /// <summary>
        /// 关联此数据所属的助手。
        /// </summary>
        public string AssistantId { get; init; } = string.Empty;
        /// <summary>
        /// 绑定创建此会话或运行时使用的固定发布版本。
        /// </summary>
        public string PublishedVersionId { get; init; } = string.Empty;
        /// <summary>
        /// 关联创建该对话或请求的匿名会话。
        /// </summary>
        public string SessionId { get; init; } = string.Empty;
        /// <summary>
        /// 关联此消息、轮次或运行所属的对话。
        /// </summary>
        public string ConversationId { get; init; } = string.Empty;
        /// <summary>
        /// 关联此记录所属的用户请求轮次。
        /// </summary>
        public string TurnId { get; init; } = string.Empty;
        /// <summary>
        /// 关联产生此事件、消息或工具调用的执行运行。
        /// </summary>
        public string RunId { get; init; } = string.Empty;
        /// <summary>
        /// 区分公开执行和管理端预览执行。
        /// </summary>
        public AssistantExecutionMode ExecutionMode { get; init; }
        /// <summary>
        /// 表示票据的传输信息。
        /// </summary>
        public AssistantTransportKind[] AllowedTransports { get; init; } = Array.Empty<AssistantTransportKind>();
        /// <summary>
        /// 以 Unix 秒数记录票据失效时间。
        /// </summary>
        public long ExpiresAtUnixSeconds { get; init; }
        /// <summary>
        /// 保存票据中的随机标识，使不同签发结果具有不同的负载。
        /// </summary>
        public string Nonce { get; init; } = string.Empty;
    }
}
