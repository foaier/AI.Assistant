#nullable enable
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Contracts;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantPublicThemeBootstrapTests
{
    [Fact]
    public async Task Bootstrap_returns_the_published_chat_background_color()
    {
        var bootstrap = await CreateService("""
            {"primaryColor":"#0f766e","chatBackgroundColor":"#f1f8f6"}
            """).GetBootstrapAsync("assistant-key", "zh-cn", CancellationToken.None);

        Assert.Equal("#f1f8f6", bootstrap.ChatBackgroundColor);
    }

    [Fact]
    public async Task Bootstrap_leaves_background_unset_for_legacy_publications()
    {
        var bootstrap = await CreateService("""
            {"primaryColor":"#2563eb"}
            """).GetBootstrapAsync("assistant-key", "zh-cn", CancellationToken.None);

        Assert.Null(bootstrap.ChatBackgroundColor);
    }

    [Theory]
    [InlineData("/api/media/assets/asset_123/content", "/api/media/assets/asset_123/content")]
    [InlineData("https://cdn.example.test/assistant.png", "https://cdn.example.test/assistant.png")]
    [InlineData("//cdn.example.test/assistant.png", null)]
    [InlineData("/\\cdn.example.test/assistant.png", null)]
    [InlineData("javascript:alert(1)", null)]
    public async Task Bootstrap_exposes_only_safe_assistant_avatar_urls(
        string? configuredUrl,
        string? expectedUrl)
    {
        var configuration = JsonSerializer.Serialize(new { iconUrl = configuredUrl });
        var bootstrap = await CreateService(configuration)
            .GetBootstrapAsync("assistant-key", "zh-cn", CancellationToken.None);

        Assert.Equal(expectedUrl, bootstrap.HomeIconUrl);
    }

    private static AssistantPublicRuntimeService CreateService(string configurationJson)
    {
        using var document = JsonDocument.Parse(configurationJson);
        var authority = new AssistantPublicAuthority(
            "assistant-key",
            new AssistantScopeSnapshot(
                "assistant-id",
                "tenant-id",
                "org-id",
                "published-version-id",
                AssistantExecutionMode.Public),
            "Assistant",
            null,
            document.RootElement.Clone(),
            1,
            "snapshot-hash");
        return new AssistantPublicRuntimeService(
            new StaticAuthorityResolver(authority),
            null!,
            null!,
            null!,
            null!);
    }

    private sealed class StaticAuthorityResolver(AssistantPublicAuthority authority)
        : IAssistantPublicAuthorityResolver
    {
        public Task<AssistantPublicAuthority?> ResolveAsync(
            string assistantKey,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AssistantPublicAuthority?>(authority);
    }
}
