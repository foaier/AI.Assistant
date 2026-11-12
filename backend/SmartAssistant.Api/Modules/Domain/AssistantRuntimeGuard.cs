#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 集中执行运行范围、输入格式、时间窗口和状态迁移的领域校验。
/// </summary>
internal static class AssistantRuntimeGuard
{
    public static string Required(string? value, string field, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BizResultException(BusinessCodes.BusinessException, $"{field} required");

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new BizResultException(BusinessCodes.BusinessException, $"{field} too long");

        return normalized;
    }

    public static string Optional(string? value, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new BizResultException(BusinessCodes.BusinessException, "value too long");

        return normalized;
    }

    public static string NormalizeLocale(string? locale)
    {
        var normalized = Required(locale, "Locale", 32);
        return normalized.Replace('_', '-').ToLowerInvariant();
    }

    public static void RequireScope(
        string assistantId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        AssistantExecutionMode executionMode)
    {
        _ = Required(assistantId, "AssistantId");
        _ = Required(tenantId, "TenantId");
        _ = Required(orgId, "OrgId");
        _ = Required(publishedVersionId, "PublishedVersionId");
        if (!Enum.IsDefined(executionMode))
            throw new BizResultException(BusinessCodes.BusinessException, "ExecutionMode invalid");
    }

    /// <summary>
    /// 比较助手、租户、组织、发布版本和执行模式，阻止跨运行范围访问。
    /// </summary>
    public static void EnsureScope(
        string expectedAssistantId,
        string expectedTenantId,
        string expectedOrgId,
        string expectedPublishedVersionId,
        AssistantExecutionMode expectedExecutionMode,
        AssistantScopeSnapshot actual)
    {
        if (!string.Equals(expectedAssistantId, actual.AssistantId, StringComparison.Ordinal)
            || !string.Equals(expectedTenantId, actual.TenantId, StringComparison.Ordinal)
            || !string.Equals(expectedOrgId, actual.OrgId, StringComparison.Ordinal)
            || !string.Equals(expectedPublishedVersionId, actual.PublishedVersionId, StringComparison.Ordinal)
            || expectedExecutionMode != actual.ExecutionMode)
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_mismatch");
        }
    }

    public static void EnsureUtcWindow(DateTime now, DateTime expiresAt)
    {
        if (expiresAt <= now)
            throw new BizResultException(BusinessCodes.BusinessException, "expires_at_must_be_after_now");
    }

    public static string NormalizeRequestText(string? value)
        => (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    /// <summary>
    /// 根据标准化消息内容生成请求指纹，用于客户端幂等键的内容一致性校验。
    /// </summary>
    public static string ComputeRequestHash(string? messageType, string? text, string? locale)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            MessageType = (messageType ?? string.Empty).Trim().ToLowerInvariant(),
            Text = NormalizeRequestText(text),
            Locale = NormalizeLocale(locale)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string NormalizeJson(string? value, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        try
        {
            using var document = JsonDocument.Parse(normalized);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw new BizResultException(BusinessCodes.BusinessException, $"{field} must be valid JSON");
        }
    }

    public static void EnsureActive(AssistantSessionStatus status, DateTime now, DateTime expiresAt, string expiredCode)
    {
        if (status != AssistantSessionStatus.Active)
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_session_not_active");
        if (now >= expiresAt)
            throw new BizResultException(BusinessCodes.Forbidden, expiredCode);
    }

    public static void EnsureNotTerminal<TStatus>(TStatus status, Func<TStatus, bool> terminal, string entity)
        where TStatus : struct, Enum
    {
        if (terminal(status))
            throw new BizResultException(BusinessCodes.Conflict, $"{entity}_terminal_already_set");
    }
}
