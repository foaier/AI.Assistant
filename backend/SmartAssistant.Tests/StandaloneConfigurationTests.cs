using System.Text;
using Microsoft.Extensions.Configuration;
using SmartAssistant;
using Xunit;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证独立项目配置行为和边界条件的自动化测试集合。
/// </summary>
public sealed class StandaloneConfigurationTests
{
    /// <summary>
    /// 验证凭据引用可从本地应用配置中解析。
    /// </summary>
    [Theory]
    [InlineData("config://AssistantSecrets:ModelKey")]
    [InlineData("appsettings://AssistantSecrets:ModelKey")]
    [InlineData("apollo://AssistantSecrets:ModelKey")]
    public async Task Credential_references_resolve_from_local_appsettings_configuration(string reference)
    {
        using var json = new MemoryStream(Encoding.UTF8.GetBytes("""{"AssistantSecrets":{"ModelKey":"fixture-json-key"}}"""));
        var configuration = new ConfigurationBuilder().AddJsonStream(json).AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AssistantSecrets:ModelKey"] = "fixture-local-override"
        }).Build();
        var resolver = new ConfigurationCredentialMaterialResolver(configuration);
        Assert.Equal("fixture-local-override", await resolver.ResolveRequiredAsync(reference, "v1"));
    }

    /// <summary>
    /// 验证凭据不可用时报告失败，且不泄漏引用或配置内容。
    /// </summary>
    [Theory]
    [InlineData("config://AssistantSecrets:Missing")]
    [InlineData("apollo://AssistantSecrets:Missing")]
    [InlineData("config://AssistantSecrets:Empty")]
    [InlineData("unsupported://sensitive-key-name")]
    public async Task Unavailable_credentials_fail_without_disclosing_reference_or_configuration(string reference)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AssistantSecrets:Empty"] = " ",
            ["AssistantSecrets:Unrelated"] = "sensitive-test-value"
        }).Build();
        var resolver = new ConfigurationCredentialMaterialResolver(configuration);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveRequiredAsync(reference, "v1"));
        Assert.DoesNotContain(reference, error.Message);
        Assert.DoesNotContain("sensitive-key-name", error.Message);
        Assert.DoesNotContain("sensitive-test-value", error.Message);
    }

    /// <summary>
    /// 验证环境变量凭据引用始终使用明确指定的环境来源。
    /// </summary>
    [Fact]
    public async Task Environment_references_keep_the_explicit_environment_source()
    {
        var variable = "ASSISTANT_FIXTURE_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "fixture-env-value");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [variable] = "fixture-json-shadow"
            }).Build();
            var resolver = new ConfigurationCredentialMaterialResolver(configuration);
            Assert.Equal("fixture-env-value", await resolver.ResolveRequiredAsync("env://" + variable, "v1"));
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    /// <summary>
    /// 验证可选空引用返回空值，必填凭据读取会响应取消。
    /// </summary>
    [Fact]
    public async Task Optional_blank_references_return_null_and_required_reads_honor_cancellation()
    {
        var resolver = new ConfigurationCredentialMaterialResolver(new ConfigurationBuilder().Build());
        Assert.Null(await resolver.ResolveOptionalAsync(null, null));
        Assert.Null(await resolver.ResolveOptionalAsync(" ", "v1"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveRequiredAsync("config://AssistantSecrets:Key", "v1", cancellation.Token));
    }
}
