#nullable enable
using System;
using SmartAssistant.Platform.Security;
using Microsoft.Extensions.Configuration;

namespace SmartAssistant.Services.Infrastructure.AssistantTransport;

/// <summary>
/// 解析流票据签名密钥，优先使用专用配置，否则按用途派生密钥。
/// </summary>
public static class AssistantStreamTicketSigningKey
{
    /// <summary>
    /// 优先读取显式配置的流票据密钥，未配置时使用根密钥按流票据用途派生。
    /// </summary>
    public static byte[] Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return JwtKeyResolver.ResolvePurposeKeyBytes(configuration, "assistant-stream-ticket");
    }
}
