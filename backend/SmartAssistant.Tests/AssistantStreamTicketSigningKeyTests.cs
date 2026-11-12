#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20260926-005-public-assistant-s2-runtime-wiring.md
using System;
using System.Linq;
using SmartAssistant.Modules.Infrastructure.AssistantTransport;
using SmartAssistant.Platform.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantStreamTicketSigningKeyTests
{
    private const string RootKey = "assistant-test-root-key-with-at-least-32-bytes";

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
