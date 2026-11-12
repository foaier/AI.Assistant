// Regression: database API tokens must work for checks and model execution.
#nullable enable
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Platform.Security;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using Xunit;

namespace SmartAssistant.Tests;

public class AssistantModelSecretTests
{
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

    private static AssistantProviderSecretProtector CreateProtector()
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AssistantCredentials:CurrentKeyId"] = "test",
            ["AssistantCredentials:EncryptionKeys:test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build());

    private sealed class ReferenceResolver : ICredentialMaterialResolver
    {
        public Task<string?> ResolveOptionalAsync(string? reference, string? version, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("resolved-token");
        public string? Reference;
        public string? Version;
        public Task<string> ResolveRequiredAsync(string reference, string version, CancellationToken cancellationToken = default)
        {
            Reference = reference;
            Version = version;
            return Task.FromResult("resolved-token");
        }
    }
}
