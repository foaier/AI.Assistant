using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace AI.Assistant.Infrastructure.Security
{
    /// <summary>
    /// 校验签名密钥长度，并按用途派生或读取令牌签名密钥。
    /// </summary>
    public static class JwtKeyResolver
    {
        /// <summary>
        /// 定义JWT密钥解析器使用的HMAC密钥配置值。
        /// </summary>
        private const int MinimumHmacSha256KeyBytes = 32;

        /// <summary>
        /// 解析并返回管理密钥。
        /// </summary>
        public static byte[] ResolveAdminKeyBytes(IConfiguration configuration)
        {
            return ResolvePurposeKeyBytes(configuration, "admin");
        }

        /// <summary>
        /// 解析并返回密钥。
        /// </summary>
        public static byte[] ResolveConsumerKeyBytes(IConfiguration configuration)
        {
            return ResolvePurposeKeyBytes(configuration, "consumer");
        }

        /// <summary>
        /// 校验根密钥后按用途生成独立签名材料，避免不同令牌用途直接共用同一密钥。
        /// </summary>
        public static byte[] ResolvePurposeKeyBytes(IConfiguration configuration, string purpose)
        {
            if (string.IsNullOrWhiteSpace(purpose))
                throw new InvalidOperationException("JWT key purpose is required and must not be empty.");

            var rootKey = configuration["Jwt:RootKey"];
            ValidateKey(rootKey ?? string.Empty, "Jwt:RootKey");
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes($"glevent::{purpose.Trim()}::{rootKey}"));
        }

        /// <summary>
        /// 读取指定配置节中的独立密钥，并按 UTF-8 字节长度校验最低安全要求。
        /// </summary>
        public static byte[] ResolveExplicitKeyBytes(IConfiguration configuration, string sectionName)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (string.IsNullOrWhiteSpace(sectionName))
                throw new InvalidOperationException("JWT key configuration section is required and must not be empty.");

            var normalizedSectionName = sectionName.Trim();
            var keyName = $"{normalizedSectionName}:Key";
            var explicitKey = configuration[keyName];
            ValidateKey(explicitKey ?? string.Empty, keyName);
            return Encoding.UTF8.GetBytes(explicitKey!);
        }

        /// <summary>
        /// 校验签名密钥已配置且达到 HS256 所需的最小字节长度。
        /// </summary>
        private static void ValidateKey(string key, string keyName)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException($"{keyName} is required and must not be empty.");

            var byteCount = Encoding.UTF8.GetByteCount(key);
            if (byteCount < MinimumHmacSha256KeyBytes)
            {
                throw new InvalidOperationException(
                    $"{keyName} must be at least {MinimumHmacSha256KeyBytes} UTF-8 bytes for HS256; current value has {byteCount} bytes.");
            }
        }
    }
}
