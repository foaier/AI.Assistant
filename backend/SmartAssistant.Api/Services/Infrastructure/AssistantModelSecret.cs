#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Platform.Security;

namespace SmartAssistant.Services.Infrastructure;

/// <summary>
/// 将保存的模型凭据还原为调用所需的令牌，支持密文、配置引用及兼容的明文形式。
/// </summary>
public static class AssistantModelSecret
{
    /// <summary>
    /// 按凭据格式解密、解析引用或读取兼容令牌，返回供上游请求使用的值。
    /// </summary>
    public static async Task<string> ResolveAsync(ICredentialMaterialResolver resolver,
        AssistantProviderSecretProtector protector, string value, string providerId, string version,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = value.Trim();
        if (protector.IsProtected(normalized)) return protector.Unprotect(normalized, providerId, version);
        if (!protector.IsReference(normalized)) return normalized;
        return await resolver.ResolveRequiredAsync(normalized, version, ct);
    }
}
