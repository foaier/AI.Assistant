#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20261004-008-business-model-policy-and-runtime-closure.md
using System.Text.Json;
using System.Reflection;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantProviderDatabaseTokenTests
{
    private const string PlatformScope = "__assistant_platform__";

    [Theory]
    [InlineData("missing", "assistant_provider_not_found")]
    [InlineData("disabled", "assistant_provider_disabled")]
    [InlineData("revision-missing", "assistant_provider_revision_unavailable")]
    [InlineData("revision-disabled", "assistant_provider_revision_unavailable")]
    public async Task Policy_save_reports_the_provider_condition(string condition, string expectedMessage)
    {
        var repository = new MemoryRepository();
        var providerSettings = new AssistantModelServiceSettings("模型", "https://models.example.test/v1", "env://TOKEN", "v1",
            new[] { new AssistantModelOption("chat-v1", "chat") });
        if (condition != "missing") repository.Seed(new AssistantPolicyRecord
        {
            Id = "provider", TenantId = PlatformScope, OrgId = PlatformScope, Kind = "provider",
            Enabled = condition != "disabled", SettingsJson = JsonSerializer.Serialize(providerSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        if (condition is not ("revision-missing" or "missing")) repository.Version = new AssistantPolicyRevisionRecord
        {
            Enabled = condition != "revision-disabled",
            SettingsJson = JsonSerializer.Serialize(providerSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var service = new AssistantPolicyService(repository, DispatchProxy.Create<IDbContext, NoopDbContext>(),
            new NoSecrets(), new NoHttp(), new AssistantProviderSecretProtector(new ConfigurationBuilder().Build()));

        var error = await Assert.ThrowsAsync<BizResultException>(() => service.SavePolicyAsync("editor", "tenant", "org", null,
            new(new AssistantBusinessPolicySettings("客服", "customer_service", "provider", 10, "chat-v1", "礼貌回答"), 0)));

        Assert.Equal(expectedMessage, error.BusinessMessage);
    }

    [Fact]
    public async Task Runtime_policy_resolution_keeps_provider_unavailability_generic()
    {
        var repository = new MemoryRepository();
        var providerSettings = new AssistantModelServiceSettings("模型", "https://models.example.test/v1", "env://TOKEN", "v1",
            new[] { new AssistantModelOption("chat-v1", "chat") });
        repository.Seed(new AssistantPolicyRecord
        {
            Id = "provider", TenantId = PlatformScope, OrgId = PlatformScope, Kind = "provider", Revision = 1,
            SettingsJson = JsonSerializer.Serialize(providerSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        repository.Version = new AssistantPolicyRevisionRecord
        {
            Enabled = true,
            SettingsJson = JsonSerializer.Serialize(providerSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var service = new AssistantPolicyService(repository, DispatchProxy.Create<IDbContext, NoopDbContext>(),
            new NoSecrets(), new NoHttp(), new AssistantProviderSecretProtector(new ConfigurationBuilder().Build()));
        var policy = await service.SavePolicyAsync("editor", "tenant", "org", null,
            new(new AssistantBusinessPolicySettings("客服", "customer_service", "provider", 10, "chat-v1", "礼貌回答"), 0));
        await service.SetProviderStatusAsync("admin", "provider", new(false, 1));

        var error = await Assert.ThrowsAsync<BizResultException>(() => service.ResolveAsync("tenant", "org", policy.Id));

        Assert.Equal("assistant_provider_unavailable", error.BusinessMessage);
    }

    [Fact]
    public async Task Provider_token_is_saved_and_preserved_without_encryption_key_but_never_returned()
    {
        var repository = new MemoryRepository();
        var db = DispatchProxy.Create<IDbContext, NoopDbContext>();
        var configuration = new ConfigurationBuilder().Build();
        var service = new AssistantPolicyService(repository, db, new NoSecrets(), new NoHttp(),
            new AssistantProviderSecretProtector(configuration));
        var settings = new AssistantModelServiceSettings("模型", "https://models.example.test/v1", "sk-db-token", "v1",
            new[] { new AssistantModelOption("chat-v1", "chat") });

        var created = await service.SaveProviderAsync("admin", null, new(settings, 0));

        Assert.Equal(AssistantPolicyService.ConfiguredCredentialMarker, created.Settings!.SecretReference);
        Assert.DoesNotContain("sk-db-token", JsonSerializer.Serialize(created));
        var stored = await repository.GetAsync(PlatformScope, PlatformScope,
            "provider", created.Id, default);
        Assert.Contains("sk-db-token", stored!.SettingsJson);
        var listed = await service.ListProvidersAsync(true);
        Assert.Equal(AssistantPolicyService.ConfiguredCredentialMarker, listed.Single().Settings!.SecretReference);
        Assert.DoesNotContain("sk-db-token", JsonSerializer.Serialize(listed));
        var edit = await service.GetProviderForEditAsync(created.Id);
        Assert.Equal("sk-db-token", edit.Settings!.SecretReference);

        var updated = await service.SaveProviderAsync("admin", created.Id,
            new(created.Settings with { Name = "更新名称", SecretReference = AssistantPolicyService.ConfiguredCredentialMarker }, created.Revision));

        Assert.Equal(2, updated.Revision);
        Assert.Equal(AssistantPolicyService.ConfiguredCredentialMarker, updated.Settings!.SecretReference);
        stored = await repository.GetAsync(PlatformScope, PlatformScope,
            "provider", created.Id, default);
        Assert.Contains("sk-db-token", stored!.SettingsJson);
        Assert.Equal("更新名称", JsonDocument.Parse(stored.SettingsJson).RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Provider_edit_readback_unprotects_database_token()
    {
        var repository = new MemoryRepository();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AssistantCredentials:CurrentKeyId"] = "test",
            ["AssistantCredentials:EncryptionKeys:test"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        }).Build();
        var protector = new AssistantProviderSecretProtector(configuration);
        var settings = new AssistantModelServiceSettings("模型", "https://models.example.test/v1",
            protector.Protect("sk-encrypted-token", "provider-1", "v3"), "v3",
            new[] { new AssistantModelOption("chat-v1", "chat") });
        repository.Seed(new AssistantPolicyRecord
        {
            Id = "provider-1", TenantId = PlatformScope, OrgId = PlatformScope, Kind = "provider", Revision = 3,
            SettingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        var service = new AssistantPolicyService(repository, DispatchProxy.Create<IDbContext, NoopDbContext>(),
            new NoSecrets(), new NoHttp(), protector);

        var edit = await service.GetProviderForEditAsync("provider-1");

        Assert.Equal("sk-encrypted-token", edit.Settings!.SecretReference);
        Assert.DoesNotContain("sk-encrypted-token", (await service.ListProvidersAsync(true)).Select(x => JsonSerializer.Serialize(x)));
    }

    [Fact]
    public async Task Provider_edit_readback_resolves_external_credential_reference()
    {
        var repository = new MemoryRepository();
        var settings = new AssistantModelServiceSettings("模型", "https://models.example.test/v1", "env://MODEL_TOKEN", "v2",
            new[] { new AssistantModelOption("chat-v1", "chat") });
        repository.Seed(new AssistantPolicyRecord
        {
            Id = "provider-1", TenantId = PlatformScope, OrgId = PlatformScope, Kind = "provider", Revision = 2,
            SettingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        var secrets = new FixedSecrets();
        var service = new AssistantPolicyService(repository, DispatchProxy.Create<IDbContext, NoopDbContext>(),
            secrets, new NoHttp(), new AssistantProviderSecretProtector(new ConfigurationBuilder().Build()));

        var edit = await service.GetProviderForEditAsync("provider-1");

        Assert.Equal("resolved-token", edit.Settings!.SecretReference);
        Assert.Equal("env://MODEL_TOKEN", secrets.Reference);
        Assert.Equal("v2", secrets.Version);
        Assert.Contains("env://MODEL_TOKEN", JsonSerializer.Serialize((await service.ListProvidersAsync(true)).Single()));
        Assert.DoesNotContain("resolved-token", JsonSerializer.Serialize(await service.ListProvidersAsync(true)));

        await service.SaveProviderAsync("admin", "provider-1",
            new(edit.Settings! with { Name = "更新名称" }, edit.Revision));
        var stored = await repository.GetAsync(PlatformScope, PlatformScope, "provider", "provider-1", default);
        Assert.Contains("env://MODEL_TOKEN", stored!.SettingsJson);
        Assert.DoesNotContain("resolved-token", stored.SettingsJson);
    }

    private sealed class MemoryRepository : IAssistantPolicyRepository
    {
        private readonly Dictionary<string, AssistantPolicyRecord> _records = new();
        public AssistantPolicyRevisionRecord? Version { get; set; }
        public void Seed(AssistantPolicyRecord record) => _records[record.Id] = record;
        public Task<IReadOnlyList<AssistantPolicyRecord>> ListAsync(string tenantId, string orgId, string kind, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantPolicyRecord>>(_records.Values.ToArray());
        public Task<AssistantPolicyRecord?> GetAsync(string tenantId, string orgId, string kind, string id, CancellationToken ct)
        {
            _records.TryGetValue(id, out var record);
            return Task.FromResult(record is not null && record.TenantId == tenantId && record.OrgId == orgId && record.Kind == kind
                ? record : null);
        }
        public Task<AssistantPolicyRecord?> GetProviderAsync(string id, CancellationToken ct)
            => Task.FromResult(_records.GetValueOrDefault(id));
        public Task<AssistantPolicyRevisionRecord?> GetRevisionAsync(string tenantId, string orgId, string kind, string id, long revision, CancellationToken ct)
            => Task.FromResult(Version);
        public Task SaveAsync(AssistantPolicyRecord record, long expectedRevision, CancellationToken ct)
        {
            _records[record.Id] = record;
            return Task.CompletedTask;
        }
    }

    private sealed class NoSecrets : ICredentialMaterialResolver
    {
        public Task<string> ResolveRequiredAsync(string reference, string version, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Direct database credentials must not require the reference resolver.");
        public Task<string?> ResolveOptionalAsync(string? reference, string? version, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Direct database credentials must not require the reference resolver.");
    }

    private sealed class FixedSecrets : ICredentialMaterialResolver
    {
        public string? Reference { get; private set; }
        public string? Version { get; private set; }
        public Task<string> ResolveRequiredAsync(string reference, string version, CancellationToken cancellationToken = default)
        {
            Reference = reference;
            Version = version;
            return Task.FromResult("resolved-token");
        }
        public Task<string?> ResolveOptionalAsync(string? reference, string? version, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Saving a provider must not call a model service.");
    }

    public class NoopDbContext : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.Name switch
            {
                "CommitAsync" => Task.FromResult(1),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
    }
}
