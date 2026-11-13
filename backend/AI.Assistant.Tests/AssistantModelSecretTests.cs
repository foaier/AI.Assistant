// Regression: database API tokens must work for checks and model execution.
#nullable enable
using AI.Assistant.Services.Repositorys;
using AI.Assistant.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using Xunit;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证助手模型密钥行为和边界条件的自动化测试集合。
/// </summary>
public class AssistantModelSecretTests
{
    /// <summary>
    /// 验证数据库已保存令牌时不会访问外部凭据引用存储。
    /// </summary>
    [Fact]
    public async Task Stored_token_bypasses_reference_store()
    {
        var resolver = new ReferenceResolver();
        var protector = CreateProtector();
        var encrypted = protector.Protect("sk-test-token", "provider", "v1");
        Assert.DoesNotContain("sk-test-token", encrypted);
        Assert.Equal("sk-test-token", await AssistantModelSecret.ResolveAsync(resolver, protector,
            encrypted, "provider", "v1"));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(encrypted, "another-provider", "v1"));
        Assert.Null(resolver.Reference);
    }

    /// <summary>
    /// 验证凭据引用按指定版本解析。
    /// </summary>
    [Theory]
    [InlineData("env://MODEL_KEY")]
    [InlineData("config://Model:Key")]
    [InlineData("apollo://Model:Key")]
    [InlineData("db://11111111111111111111111111111111")]
    public async Task References_keep_versioned_resolution(string reference)
    {
        var resolver = new ReferenceResolver();
        Assert.Equal("resolved-token", await AssistantModelSecret.ResolveAsync(resolver, CreateProtector(), reference, "provider", "v2"));
        Assert.Equal(reference, resolver.Reference);
        Assert.Equal("v2", resolver.Version);
    }

    /// <summary>
    /// 验证创建相关行为、预期结果及错误边界。
    /// </summary>
    private static AssistantProviderSecretProtector CreateProtector()
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AssistantCredentials:CurrentKeyId"] = "test",
            ["AssistantCredentials:EncryptionKeys:test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build());

    /// <summary>
    /// 测试用解析器，为凭据或配置提供确定的固定结果。
    /// </summary>
    private sealed class ReferenceResolver : ICredentialMaterialResolver
    {
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public Task<string?> ResolveOptionalAsync(string? reference, string? version, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("resolved-token");
        /// <summary>
        /// 保存引用解析器在处理引用时所需的数据或状态。
        /// </summary>
        public string? Reference;
        /// <summary>
        /// 保存引用解析器在处理版本时所需的数据或状态。
        /// </summary>
        public string? Version;
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public Task<string> ResolveRequiredAsync(string reference, string version, CancellationToken cancellationToken = default)
        {
            Reference = reference;
            Version = version;
            return Task.FromResult("resolved-token");
        }
    }
}
