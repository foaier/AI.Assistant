#nullable enable
namespace SmartAssistant.Models.Configuration;

/// <summary>
/// 本地持久化的令牌签名根密钥和凭据加密密钥。
/// </summary>
internal sealed record LocalKeys(
    string JwtRootKey,
    string EncryptionKey);
