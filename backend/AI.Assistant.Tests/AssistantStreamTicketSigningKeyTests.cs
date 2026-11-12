#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20260926-005-public-assistant-s2-runtime-wiring.md
using System;
using System.Linq;
using AI.Assistant.Services.Infrastructure.AssistantTransport;
using AI.Assistant.Platform.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证助手流式输出票据签名密钥行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantStreamTicketSigningKeyTests
{
    /// <summary>
    /// 定义助手流式输出票据签名密钥测试使用的密钥配置值。
    /// </summary>
    private const string RootKey = "assistant-test-root-key-with-at-least-32-bytes";

    /// <summary>
    /// 验证流票据从 JWT 根密钥派生独立密钥，并忽略旧配置。
    /// </summary>
    [Fact]
    public void Derives_a_distinct_stream_ticket_key_from_the_jwt_root_and_ignores_legacy_setting()
    {
        const string legacyKey = "obsolete-assistant-stream-ticket-key-that-must-not-be-used";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:RootKey"] = RootKey,
            ["Assistant:StreamTicket:SigningKey"] = legacyKey
        }).Build();

        var key = AssistantStreamTicketSigningKey.Resolve(configuration);

        Assert.Equal(32, key.Length);
        Assert.Equal(JwtKeyResolver.ResolvePurposeKeyBytes(configuration, "assistant-stream-ticket"), key);
        Assert.False(key.SequenceEqual(JwtKeyResolver.ResolveAdminKeyBytes(configuration)));
        Assert.False(key.SequenceEqual(JwtKeyResolver.ResolveConsumerKeyBytes(configuration)));
        var tickets = new SignedAssistantStreamTicketService(key);
        Assert.NotNull(tickets);
    }

    /// <summary>
    /// 验证即使存在旧配置，缺少 JWT 根密钥仍会被拒绝。
    /// </summary>
    [Fact]
    public void Rejects_missing_jwt_root_even_when_legacy_setting_exists()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assistant:StreamTicket:SigningKey"] = "obsolete-assistant-stream-ticket-key-that-must-not-be-used"
        }).Build();

        Assert.Throws<InvalidOperationException>(() =>
            AssistantStreamTicketSigningKey.Resolve(configuration));
    }
}
