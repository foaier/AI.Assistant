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
    /// <summary>
    /// 校验字符串必填，并在返回前去除首尾空白。
    /// </summary>
    public static string Required(string? value, string field, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BizResultException(BusinessCodes.BusinessException, $"{field} required");

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new BizResultException(BusinessCodes.BusinessException, $"{field} too long");

        return normalized;
    }

    /// <summary>
    /// 将空白字符串转换为空值，并规范化有效字符串。
    /// </summary>
    public static string Optional(string? value, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new BizResultException(BusinessCodes.BusinessException, "value too long");

        return normalized;
    }

    /// <summary>
    /// 规范化语言标识；不支持的语言回退到默认语言。
    /// </summary>
    public static string NormalizeLocale(string? locale)
    {
        var normalized = Required(locale, "Locale", 32);
        return normalized.Replace('_', '-').ToLowerInvariant();
    }

    /// <summary>
    /// 校验助手、发布版本标识及执行模式有效。
    /// </summary>
    public static void RequireScope(
        string assistantId,
        string publishedVersionId,
        AssistantExecutionMode executionMode)
    {
        _ = Required(assistantId, "AssistantId");
        _ = Required(publishedVersionId, "PublishedVersionId");
        if (!Enum.IsDefined(executionMode))
            throw new BizResultException(BusinessCodes.BusinessException, "ExecutionMode invalid");
    }

    /// <summary>
    /// 比较助手、发布版本和执行模式，阻止跨运行范围访问。
    /// </summary>
    public static void EnsureScope(
        string expectedAssistantId,
        string expectedPublishedVersionId,
        AssistantExecutionMode expectedExecutionMode,
        AssistantScopeSnapshot actual)
    {
        if (!string.Equals(expectedAssistantId, actual.AssistantId, StringComparison.Ordinal)
            || !string.Equals(expectedPublishedVersionId, actual.PublishedVersionId, StringComparison.Ordinal)
            || expectedExecutionMode != actual.ExecutionMode)
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_mismatch");
        }
    }

    /// <summary>
    /// 校验时间范围使用 UTC 且起止顺序有效。
    /// </summary>
    public static void EnsureUtcWindow(DateTime now, DateTime expiresAt)
    {
        if (expiresAt <= now)
            throw new BizResultException(BusinessCodes.BusinessException, "expires_at_must_be_after_now");
    }

    /// <summary>
    /// 将输入规范化为请求文本，并统一相关格式。
    /// </summary>
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

    /// <summary>
    /// 将配置文本解析并重新序列化为规范 JSON；无效 JSON 按业务校验错误拒绝。
    /// </summary>
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

    /// <summary>
    /// 确认目标资源处于启用或生效状态；失效资源不得继续参与处理。
    /// </summary>
    public static void EnsureActive(AssistantSessionStatus status, DateTime now, DateTime expiresAt, string expiredCode)
    {
        if (status != AssistantSessionStatus.Active)
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_session_not_active");
        if (now >= expiresAt)
            throw new BizResultException(BusinessCodes.Forbidden, expiredCode);
    }

    /// <summary>
    /// 确认目标运行尚未进入终态，避免重复写入完成或失败结果。
    /// </summary>
    public static void EnsureNotTerminal<TStatus>(TStatus status, Func<TStatus, bool> terminal, string entity)
        where TStatus : struct, Enum
    {
        if (terminal(status))
            throw new BizResultException(BusinessCodes.Conflict, $"{entity}_terminal_already_set");
    }
}
