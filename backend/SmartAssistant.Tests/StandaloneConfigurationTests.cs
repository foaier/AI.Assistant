using System.Text;
using Microsoft.Extensions.Configuration;
using SmartAssistant;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class StandaloneConfigurationTests
{
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
