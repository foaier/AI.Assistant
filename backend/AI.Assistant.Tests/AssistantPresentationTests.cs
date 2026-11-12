// Spec: REQ-20261105-013-customer-service-experience.md.
// Covers safe public navigation and deterministic suggestion composition.
using System.Text.Json;
using AI.Assistant.Services.Application;
using Xunit;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证助手展示配置行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantPresentationTests
{
    /// <summary>
    /// 验证站内和外部导航地址校验会拒绝可执行脚本链接。
    /// </summary>
    [Fact]
    public void Validates_external_and_same_site_navigation_without_accepting_active_urls()
    {
        using var valid = JsonDocument.Parse("""
            {"quickLinks":[
              {"id":"registration","label":"Registration","url":"https://example.test/register","icon":"calendar","enabled":true},
              {"id":"help","label":"Help","url":"/help","icon":"help","enabled":true},
              {"id":"exhibitors","label":"Exhibitors","url":"/exhibitors","icon":"exhibitor","iconUrl":"/api/media/assets/asset_test/content","enabled":true}],
             "suggestedQuestions":["How do I register?"],"historyEnabled":true,"quickLinksEnabled":true}
            """);
        AssistantPresentation.Validate(valid.RootElement);
        Assert.Equal(3, AssistantPresentation.Links(valid.RootElement).Count);
        Assert.Equal("exhibitor", AssistantPresentation.Links(valid.RootElement)[2].Icon);
        Assert.Equal("/api/media/assets/asset_test/content", AssistantPresentation.Links(valid.RootElement)[2].IconUrl);
        Assert.True(AssistantPresentation.IsSafeLink("/help?from=chat"));
        Assert.True(AssistantPresentation.IsSafeLink("https://example.test/path"));
        Assert.False(AssistantPresentation.IsSafeLink("//example.test/path"));
        Assert.False(AssistantPresentation.IsSafeLink("javascript:alert(1)"));
        Assert.False(AssistantPresentation.IsSafeLink("https://user:secret@example.test/"));
        Assert.False(AssistantPresentation.IsSafeLink("/\\\\evil.test"));
    }

    /// <summary>
    /// 验证不安全图标地址被拒绝，并从公开返回内容中移除。
    /// </summary>
    [Fact]
    public void Rejects_unsafe_custom_icon_urls_and_omits_them_from_public_readback()
    {
        using var unsafeIcon = JsonDocument.Parse("""
            {"quickLinks":[{"id":"help","label":"Help","url":"/help","icon":"help","iconUrl":"javascript:alert(1)"}]}
            """);
        Assert.Throws<AI.Assistant.Platform.BizResultException>(
            () => AssistantPresentation.Validate(unsafeIcon.RootElement));
        Assert.Null(AssistantPresentation.Links(unsafeIcon.RootElement).Single().IconUrl);

        using var insecureIcon = JsonDocument.Parse("""
            {"quickLinks":[{"id":"help","label":"Help","url":"/help","icon":"help","iconUrl":"http://example.test/icon.png"}]}
            """);
        Assert.Throws<AI.Assistant.Platform.BizResultException>(
            () => AssistantPresentation.Validate(insecureIcon.RootElement));
    }

    /// <summary>
    /// 验证重复导航标识被拒绝，建议问题按助手自有、知识库的顺序合并。
    /// </summary>
    [Fact]
    public void Rejects_duplicate_navigation_ids_and_merges_owned_then_knowledge_questions()
    {
        using var invalid = JsonDocument.Parse("""
            {"quickLinks":[
              {"id":"same","label":"One","url":"/one"},
              {"id":"same","label":"Two","url":"/two"}]}
            """);
        Assert.Throws<AI.Assistant.Platform.BizResultException>(() => AssistantPresentation.Validate(invalid.RootElement));

        using var configuration = JsonDocument.Parse("""
            {"suggestedQuestions":["  Registration status? ","When does it start?", "Registration status?"]}
            """);
        Assert.Equal(new[] { "Registration status?", "When does it start?", "Knowledge base question?" },
            AssistantPresentation.Questions(configuration.RootElement, ["Knowledge base question?", " Registration status? "]));
    }

    /// <summary>
    /// 验证聊天背景色的格式，并在未设置时保留旧版渐变行为。
    /// </summary>
    [Fact]
    public void Validates_chat_background_color_and_preserves_legacy_gradient_when_unset()
    {
        using var legacy = JsonDocument.Parse("{}");
        AssistantPresentation.Validate(legacy.RootElement);
        Assert.Null(AssistantPresentation.ChatBackgroundColor(legacy.RootElement));

        using var valid = JsonDocument.Parse("""
            {"primaryColor":"#0f766e","chatBackgroundColor":"#f1f8f6"}
            """);
        AssistantPresentation.Validate(valid.RootElement);
        Assert.Equal("#f1f8f6", AssistantPresentation.ChatBackgroundColor(valid.RootElement));

        using var malformed = JsonDocument.Parse("""
            {"chatBackgroundColor":"navy"}
            """);
        Assert.Throws<AI.Assistant.Platform.BizResultException>(() =>
            AssistantPresentation.Validate(malformed.RootElement));

        using var unreadable = JsonDocument.Parse("""
            {"primaryColor":"#2563eb","chatBackgroundColor":"#808080"}
            """);
        Assert.Throws<AI.Assistant.Platform.BizResultException>(() =>
            AssistantPresentation.Validate(unreadable.RootElement));
    }
}
