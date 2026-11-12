#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace SmartAssistant.Platform.Security;

/// <summary>
/// 定义凭据引用的解析边界，使调用方无需了解具体的密钥存储位置。
/// </summary>
public interface ICredentialMaterialResolver
{
    /// <summary>
    /// 解析必填凭据引用；配置缺失、内容为空或引用格式不受支持时抛出异常。
    /// </summary>
    Task<string> ResolveRequiredAsync(
        string reference,
        string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 引用为空时返回空值，否则按必填凭据规则解析并校验。
    /// </summary>
    Task<string?> ResolveOptionalAsync(
        string? reference,
        string? version,
        CancellationToken cancellationToken = default);
}
