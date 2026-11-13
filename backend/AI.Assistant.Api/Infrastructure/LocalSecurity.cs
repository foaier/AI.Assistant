using System.Security.Cryptography;
using System.Text.Json;
using AI.Assistant.Infrastructure.Security;
using AI.Assistant.Models.Configuration;

namespace AI.Assistant;

/// <summary>
/// 从本地配置或显式环境变量引用中解析凭据。
/// 兼容旧 Apollo 引用格式，但解析过程只读取本地配置。
/// </summary>
public sealed class ConfigurationCredentialMaterialResolver(IConfiguration configuration) : ICredentialMaterialResolver
{
    /// <summary>
    /// 解析必填凭据引用；配置缺失、内容为空或引用格式不受支持时抛出异常。
    /// </summary>
    public Task<string> ResolveRequiredAsync(string reference, string version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        var separator = reference.IndexOf("://", StringComparison.Ordinal);
        if (separator < 1 || separator + 3 == reference.Length)
            throw new InvalidOperationException("credential_reference_invalid");
        var scheme = reference[..separator].ToLowerInvariant();
        var key = reference[(separator + 3)..];
        /*
         * 历史模型修订中的 Apollo 引用仅从本地配置解析，不请求远程配置中心。
         */
        var value = scheme switch
        {
            "env" => Environment.GetEnvironmentVariable(key),
            "config" or "appsettings" or "apollo" => configuration[key],
            _ => throw new InvalidOperationException("credential_reference_scheme_unsupported")
        };
        return Task.FromResult(!string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException("credential_unavailable"));
    }
    /// <summary>
    /// 引用为空时返回空值，否则按必填凭据规则解析并校验。
    /// </summary>
    public async Task<string?> ResolveOptionalAsync(string? reference, string? version, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(reference) ? null : await ResolveRequiredAsync(reference, version ?? "v1", cancellationToken);
}
/// <summary>
/// 初始化独立数据目录与本地密钥，并为未显式配置的安全选项填充默认值。
/// </summary>
public static class LocalSecurity
{
    /// <summary>
    /// 解析数据目录，首次运行时生成并保存密钥，再补充未显式设置的签名和加密配置。
    /// </summary>
    public static string Configure(ConfigurationManager configuration, string contentRoot)
    {
        var configuredDirectory = configuration["Assistant:DataDirectory"];
        var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredDirectory) ? "../../data" : configuredDirectory, contentRoot);
        Directory.CreateDirectory(directory);
        configuration["Assistant:DataDirectory"] = directory;
        var keyFile = Path.Combine(directory, "keys.json");
        if (!File.Exists(keyFile))
        {
            var keys = new LocalKeys(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            using var stream = new FileStream(keyFile, FileMode.CreateNew);
            JsonSerializer.Serialize(stream, keys);
        }
        var material = JsonSerializer.Deserialize<LocalKeys>(File.ReadAllText(keyFile))!;
        configuration["Jwt:RootKey"] ??= material.JwtRootKey;
        configuration["AssistantCredentials:CurrentKeyId"] ??= "local-v1";
        configuration["AssistantCredentials:EncryptionKeys:local-v1"] ??= material.EncryptionKey;
        return directory;
    }
}
