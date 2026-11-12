#nullable enable
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI.Assistant.Services.Application;
using AI.Assistant.Services.Contracts;
using Xunit;
using AI.Assistant.Models.Publication;
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证助手公开主题初始化行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantPublicThemeBootstrapTests
{
    /// <summary>
    /// 验证公开初始化数据返回已发布的聊天背景色。
    /// </summary>
    [Fact]
    public async Task Bootstrap_returns_the_published_chat_background_color()
    {
        var bootstrap = await CreateService("""
            {"primaryColor":"#0f766e","chatBackgroundColor":"#f1f8f6"}
            """).GetBootstrapAsync("assistant-key", "zh-cn", CancellationToken.None);

        Assert.Equal("#f1f8f6", bootstrap.ChatBackgroundColor);
    }

    /// <summary>
    /// 验证旧版发布记录未配置背景时，初始化数据保持背景为空。
    /// </summary>
    [Fact]
    public async Task Bootstrap_leaves_background_unset_for_legacy_publications()
    {
        var bootstrap = await CreateService("""
            {"primaryColor":"#2563eb"}
            """).GetBootstrapAsync("assistant-key", "zh-cn", CancellationToken.None);

        Assert.Null(bootstrap.ChatBackgroundColor);
    }

    /// <summary>
    /// 验证公开初始化数据仅暴露安全的助手头像地址。
    /// </summary>
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

    /// <summary>
    /// 验证创建服务相关行为、预期结果及错误边界。
    /// </summary>
    private static AssistantPublicRuntimeService CreateService(string configurationJson)
    {
        using var document = JsonDocument.Parse(configurationJson);
        var authority = new AssistantPublicAuthority(
            "assistant-key",
            new AssistantScopeSnapshot(
                "assistant-id",
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

    /// <summary>
    /// 测试用解析器，为凭据或配置提供确定的固定结果。
    /// </summary>
    private sealed class StaticAuthorityResolver(AssistantPublicAuthority authority)
        : IAssistantPublicAuthorityResolver
    {
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPublicAuthority?> ResolveAsync(
            string assistantKey,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AssistantPublicAuthority?>(authority);
    }
}
