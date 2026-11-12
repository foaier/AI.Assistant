#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SmartAssistant.Services.Infrastructure;

/// <summary>
/// 使用配置的密钥保护模型凭据，支持按密钥标识解密历史密文。
/// </summary>
public sealed class AssistantProviderSecretProtector(IConfiguration configuration)
{
    /// <summary>
    /// 定义助手模型供应商密钥使用的前缀配置值。
    /// </summary>
    private const string Prefix = "dbenc://";
    /// <summary>
    /// 定义助手模型供应商密钥使用的上下文配置值。
    /// </summary>
    private const string Context = "assistant-provider-secret/aes-gcm/v1";

    /// <summary>
    /// 判断是否满足助手模型供应商密钥。
    /// </summary>
    public bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// 判断是否满足引用。
    /// </summary>
    public bool IsReference(string value)
    {
        if (IsProtected(value)) return false;
        var separator = value.IndexOf("://", StringComparison.Ordinal);
        return separator > 0;
    }

    /// <summary>
    /// 使用 AES-GCM 加密令牌，并将供应商、版本和密钥标识作为认证上下文绑定。
    /// </summary>
    public string Protect(string material, string providerId, string version)
    {
        var (keyId, key) = ReadCurrentKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var plaintext = Encoding.UTF8.GetBytes(material);
        var ciphertext = new byte[plaintext.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Aad(providerId, version, keyId));
            return $"{Prefix}{Encode(Encoding.UTF8.GetBytes(keyId))}.{Encode(nonce)}.{Encode(tag)}.{Encode(ciphertext)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// 按密文中的密钥标识选择解密材料，并校验供应商和版本绑定。
    /// </summary>
    public string Unprotect(string protectedValue, string providerId, string version)
    {
        if (!IsProtected(protectedValue)) throw new CryptographicException("assistant_provider_secret_format_invalid");
        var parts = protectedValue[Prefix.Length..].Split('.');
        if (parts.Length != 4) throw new CryptographicException("assistant_provider_secret_format_invalid");
        var keyId = DecodeText(parts[0]);
        var nonce = Decode(parts[1]);
        var tag = Decode(parts[2]);
        var ciphertext = Decode(parts[3]);
        if (nonce.Length != 12 || tag.Length != 16) throw new CryptographicException("assistant_provider_secret_format_invalid");
        var key = ReadKey(keyId);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Aad(providerId, version, keyId));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// 读取当前密钥。
    /// </summary>
    private (string KeyId, byte[] Key) ReadCurrentKey()
    {
        var keyId = configuration["AssistantCredentials:CurrentKeyId"]
            ?? configuration["PaymentCredentials:CurrentKeyId"];
        if (string.IsNullOrWhiteSpace(keyId)) throw new CryptographicException("assistant_provider_secret_key_unavailable");
        return (keyId, ReadKey(keyId));
    }

    /// <summary>
    /// 从配置或密钥材料中读取指定密钥，并校验其可用性。
    /// </summary>
    private byte[] ReadKey(string keyId)
    {
        var encoded = configuration[$"AssistantCredentials:EncryptionKeys:{keyId}"]
            ?? configuration[$"PaymentCredentials:EncryptionKeys:{keyId}"];
        try
        {
            var key = Convert.FromBase64String(encoded ?? "");
            if (key.Length != 32) throw new CryptographicException("assistant_provider_secret_key_invalid");
            return key;
        }
        catch (FormatException)
        {
            throw new CryptographicException("assistant_provider_secret_key_invalid");
        }
    }

    /// <summary>
    /// 生成绑定供应商、版本和密钥编号的 AES-GCM 附加认证数据。
    /// </summary>
    private static byte[] Aad(string providerId, string version, string keyId)
        => JsonSerializer.SerializeToUtf8Bytes(new[] { Context, providerId, version, keyId });

    /// <summary>
    /// 对凭据密文组件执行 URL 安全编码。
    /// </summary>
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// 校验并解码凭据密文中的 URL 安全数据。
    /// </summary>
    private static byte[] Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }

    /// <summary>
    /// 解码凭据文本组件并以严格 UTF-8 读取。
    /// </summary>
    private static string DecodeText(string value) => Encoding.UTF8.GetString(Decode(value));
}
