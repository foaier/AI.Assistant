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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly byte[] _signingKey;

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

    private static string Required(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();

    private static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

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

    private sealed class TicketPayload
    {
        public int Version { get; init; }
        public string Purpose { get; init; } = string.Empty;
        public string AssistantId { get; init; } = string.Empty;
        public string PublishedVersionId { get; init; } = string.Empty;
        public string SessionId { get; init; } = string.Empty;
        public string ConversationId { get; init; } = string.Empty;
        public string TurnId { get; init; } = string.Empty;
        public string RunId { get; init; } = string.Empty;
        public AssistantExecutionMode ExecutionMode { get; init; }
        public AssistantTransportKind[] AllowedTransports { get; init; } = Array.Empty<AssistantTransportKind>();
        public long ExpiresAtUnixSeconds { get; init; }
        public string Nonce { get; init; } = string.Empty;
    }
}
