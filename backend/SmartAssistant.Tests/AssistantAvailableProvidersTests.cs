// Spec: specs/136-public-assistant/requests/REQ-20261105-012-platform-model-services-immediate-availability.md
#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Infrastructure.AI;
using SmartAssistant.Modules.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantAvailableProvidersTests
{
    private const string PlatformScope = "__assistant_platform__";

    [Fact]
    public async Task Policy_choices_include_enabled_platform_chat_providers_for_every_organization_without_operational_settings()
    {
        var records = new[]
        {
            Provider("available", "chat", true),
            Provider("legacy-empty-grants", "chat", true, legacyGrants: true),
            Provider("disabled", "chat", false),
            Provider("embedding", "embedding", true)
        };
        var service = new AssistantPolicyService(new StaticRepository(records), null!, null!, null!,
            new AssistantProviderSecretProtector(new ConfigurationBuilder().Build()));

        var choices = await service.ListAvailableChatProvidersAsync();
        Assert.Equal(new[] { "available", "legacy-empty-grants" }, choices.Select(x => x.Id));
        Assert.All(choices, choice => Assert.Null(choice.Settings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enabled_platform_service_credentials_resolve_for_any_organization_even_with_legacy_grants(bool legacyGrants)
    {
        var id = Guid.NewGuid().ToString("N");
        var provider = Provider(id, "chat", true, legacyGrants);
        var secrets = new AssistantPolicyTestDatabase.TestSecrets();
        var resolver = new AssistantProviderCredentialResolver(new StaticRepository(new[] { provider }), secrets,
            new AssistantProviderSecretProtector(new ConfigurationBuilder().Build()));

        foreach (var (tenant, org) in new[] { ("tenant", "org"), ("other-tenant", "another-org") })
        {
            var credential = await resolver.ResolveAsync(new AssistantProviderAccess($"{id}@1", tenant, org, "model"));
            Assert.Equal("https://models.example.test/", credential.BaseUrl.ToString());
            Assert.Equal("test-key-material", credential.ApiKey);
        }

        Assert.Equal(2, secrets.Calls);
    }

    private static AssistantPolicyRecord Provider(string id, string purpose, bool enabled, bool legacyGrants = false)
    {
        var settings = new AssistantModelServiceSettings(id, "https://models.example.test", "env://MODEL_KEY", "v1",
            new[] { new AssistantModelOption("model", purpose) });
        var json = JsonNode.Parse(JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
        if (legacyGrants) json["grants"] = new JsonArray();
        return new AssistantPolicyRecord
        {
            Id = id, Name = id, TenantId = PlatformScope, OrgId = PlatformScope,
            Kind = "provider", Revision = 1, Enabled = enabled,
            SettingsJson = json.ToJsonString()
        };
    }

    private sealed class StaticRepository(IReadOnlyList<AssistantPolicyRecord> records) : IAssistantPolicyRepository
    {
        public Task<IReadOnlyList<AssistantPolicyRecord>> ListAsync(string tenantId, string orgId, string kind, CancellationToken ct)
            => Task.FromResult(records);
        public Task<AssistantPolicyRecord?> GetAsync(string tenantId, string orgId, string kind, string id, CancellationToken ct)
            => Task.FromResult(records.FirstOrDefault(record => record.TenantId == tenantId
                && record.OrgId == orgId && record.Kind == kind && record.Id == id));
        public Task<AssistantPolicyRecord?> GetProviderAsync(string id, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<AssistantPolicyRevisionRecord?> GetRevisionAsync(string tenantId, string orgId, string kind, string id, long revision, CancellationToken ct)
        {
            var record = records.FirstOrDefault(item => item.TenantId == tenantId && item.OrgId == orgId
                && item.Kind == kind && item.Id == id && item.Revision == revision);
            return Task.FromResult(record is null ? null : new AssistantPolicyRevisionRecord
            {
                RecordId = id, TenantId = tenantId, OrgId = orgId, Kind = kind,
                Revision = revision, Enabled = record.Enabled, SettingsJson = record.SettingsJson
            });
        }
        public Task SaveAsync(AssistantPolicyRecord record, long expectedRevision, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
