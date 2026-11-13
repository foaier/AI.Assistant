#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20261004-008-business-model-policy-and-runtime-closure.md
using System.Text.Json;
using System.Reflection;
using AI.Assistant.Extensions.DAL;
using AI.Assistant.Services.Application;
using AI.Assistant.Services.Repositorys;
using AI.Assistant.Services.Contracts;
using AI.Assistant.Infrastructure;
using AI.Assistant.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Xunit;
using AI.Assistant.Services.Domain.Entities;
using AI.Assistant.Models.Policies;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证助手模型供应商数据库令牌行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantProviderDatabaseTokenTests
{
    /// <summary>
    /// 验证保存策略时会准确报告供应商当前的可用条件。
    /// </summary>
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
        if (condition != "missing") repository.Seed(new AssistantPolicyEntity
        {
            Id = "provider",
            Kind = "provider",
            Enabled = condition != "disabled",
            SettingsJson = JsonSerializer.Serialize(providerSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        if (condition is not ("revision-missing" or "missing")) repository.Version = new AssistantPolicyRevisionEntity
        {
            Enabled = condition != "revision-disabled",
            SettingsJson = JsonSerializer.Serialize(providerSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var service = new AssistantPolicyService(repository, DispatchProxy.Create<IDbContext, NoopDbContext>(),
            new NoSecrets(), new NoHttp(), new AssistantProviderSecretProtector(new ConfigurationBuilder().Build()));

        var error = await Assert.ThrowsAsync<BizResultException>(() => service.SavePolicyAsync(null,
            new(new AssistantBusinessPolicySettings("客服", "customer_service", "provider", 10, "chat-v1", "礼貌回答"), 0)));

        Assert.Equal(expectedMessage, error.BusinessMessage);
    }

    /// <summary>
    /// 验证运行时策略解析对外仅返回统一的供应商不可用错误。
    /// </summary>
    [Fact]
    public async Task Runtime_policy_resolution_keeps_provider_unavailability_generic()
    {
        var repository = new MemoryRepository();
        var providerSettings = new AssistantModelServiceSettings("模型", "https://models.example.test/v1", "env://TOKEN", "v1",
            new[] { new AssistantModelOption("chat-v1", "chat") });
        repository.Seed(new AssistantPolicyEntity
        {
            Id = "provider",
            Kind = "provider",
            Revision = 1,
            SettingsJson = JsonSerializer.Serialize(providerSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        repository.Version = new AssistantPolicyRevisionEntity
        {
            Enabled = true,
            SettingsJson = JsonSerializer.Serialize(providerSettings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var service = new AssistantPolicyService(repository, DispatchProxy.Create<IDbContext, NoopDbContext>(),
            new NoSecrets(), new NoHttp(), new AssistantProviderSecretProtector(new ConfigurationBuilder().Build()));
        var policy = await service.SavePolicyAsync(null,
            new(new AssistantBusinessPolicySettings("客服", "customer_service", "provider", 10, "chat-v1", "礼貌回答"), 0));
        await service.SetProviderStatusAsync("provider", new(false, 1));

        var error = await Assert.ThrowsAsync<BizResultException>(() => service.ResolveAsync(policy.Id));

        Assert.Equal("assistant_provider_unavailable", error.BusinessMessage);
    }

    /// <summary>
    /// 验证未配置加密密钥时仍能保存及保留令牌，普通返回内容不会泄漏原文。
    /// </summary>
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

        var created = await service.SaveProviderAsync(null, new(settings, 0));

        Assert.Equal(AssistantPolicyService.ConfiguredCredentialMarker, created.Settings!.SecretReference);
        Assert.DoesNotContain("sk-db-token", JsonSerializer.Serialize(created));
        var stored = await repository.GetAsync("provider", created.Id, default);
        Assert.Contains("sk-db-token", stored!.SettingsJson);
        var listed = await service.ListProvidersAsync(true);
        Assert.Equal(AssistantPolicyService.ConfiguredCredentialMarker, listed.Single().Settings!.SecretReference);
        Assert.DoesNotContain("sk-db-token", JsonSerializer.Serialize(listed));
        var edit = await service.GetProviderForEditAsync(created.Id);
        Assert.Equal("sk-db-token", edit.Settings!.SecretReference);

        var updated = await service.SaveProviderAsync(created.Id,
            new(created.Settings with
            {
                Name = "更新名称",
                SecretReference = AssistantPolicyService.ConfiguredCredentialMarker
            }, created.Revision));

        Assert.Equal(2, updated.Revision);
        Assert.Equal(AssistantPolicyService.ConfiguredCredentialMarker, updated.Settings!.SecretReference);
        stored = await repository.GetAsync("provider", created.Id, default);
        Assert.Contains("sk-db-token", stored!.SettingsJson);
        Assert.Equal("更新名称", JsonDocument.Parse(stored.SettingsJson).RootElement.GetProperty("name").GetString());
    }

    /// <summary>
    /// 验证编辑读取能够还原数据库中保存的受保护令牌。
    /// </summary>
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
        repository.Seed(new AssistantPolicyEntity
        {
            Id = "provider-1",
            Kind = "provider",
            Revision = 3,
            SettingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        var service = new AssistantPolicyService(repository, DispatchProxy.Create<IDbContext, NoopDbContext>(),
            new NoSecrets(), new NoHttp(), protector);

        var edit = await service.GetProviderForEditAsync("provider-1");

        Assert.Equal("sk-encrypted-token", edit.Settings!.SecretReference);
        Assert.DoesNotContain("sk-encrypted-token", (await service.ListProvidersAsync(true)).Select(x => JsonSerializer.Serialize(x)));
    }

    /// <summary>
    /// 验证编辑读取能够解析外部凭据引用。
    /// </summary>
    [Fact]
    public async Task Provider_edit_readback_resolves_external_credential_reference()
    {
        var repository = new MemoryRepository();
        var settings = new AssistantModelServiceSettings("模型", "https://models.example.test/v1", "env://MODEL_TOKEN", "v2",
            new[] { new AssistantModelOption("chat-v1", "chat") });
        repository.Seed(new AssistantPolicyEntity
        {
            Id = "provider-1",
            Kind = "provider",
            Revision = 2,
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

        await service.SaveProviderAsync("provider-1",
            new(edit.Settings! with { Name = "更新名称" }, edit.Revision));
        var stored = await repository.GetAsync("provider", "provider-1", default);
        Assert.Contains("env://MODEL_TOKEN", stored!.SettingsJson);
        Assert.DoesNotContain("resolved-token", stored.SettingsJson);
    }

    /// <summary>
    /// 测试用仓储替身，按预设数据实现所需查询并避免连接外部数据库。
    /// </summary>
    private sealed class MemoryRepository : IAssistantPolicyRepository
    {
        /// <summary>
        /// 保存仓储在处理记录时所需的数据或状态。
        /// </summary>
        private readonly Dictionary<string, AssistantPolicyEntity> _records = new();
        /// <summary>
        /// 表示仓储的版本信息。
        /// </summary>
        public AssistantPolicyRevisionEntity? Version { get; set; }
        /// <summary>
        /// 验证仓储相关行为、预期结果及错误边界。
        /// </summary>
        public void Seed(AssistantPolicyEntity record) => _records[record.Id] = record;
        /// <summary>
        /// 验证列出相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantPolicyEntity>> ListAsync(string kind, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantPolicyEntity>>(_records.Values.ToArray());
        /// <summary>
        /// 验证获取相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPolicyEntity?> GetAsync(string kind, string id, CancellationToken ct)
        {
            _records.TryGetValue(id, out var record);
            return Task.FromResult(record is not null && record.Kind == kind
                ? record : null);
        }
        /// <summary>
        /// 验证获取模型供应商相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPolicyEntity?> GetProviderAsync(string id, CancellationToken ct)
            => Task.FromResult(_records.GetValueOrDefault(id));
        /// <summary>
        /// 验证获取修订相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPolicyRevisionEntity?> GetRevisionAsync(string kind, string id, long revision, CancellationToken ct)
            => Task.FromResult(Version);
        /// <summary>
        /// 验证保存相关行为、预期结果及错误边界。
        /// </summary>
        public Task SaveAsync(AssistantPolicyEntity record, long expectedRevision, CancellationToken ct)
        {
            _records[record.Id] = record;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 仅供助手模型供应商数据库令牌测试使用的测试辅助类型。
    /// </summary>
    private sealed class NoSecrets : ICredentialMaterialResolver
    {
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public Task<string> ResolveRequiredAsync(string reference, string version, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Direct database credentials must not require the reference resolver.");
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public Task<string?> ResolveOptionalAsync(string? reference, string? version, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Direct database credentials must not require the reference resolver.");
    }

    /// <summary>
    /// 仅供助手模型供应商数据库令牌测试使用的测试辅助类型。
    /// </summary>
    private sealed class FixedSecrets : ICredentialMaterialResolver
    {
        /// <summary>
        /// 表示密钥的引用信息。
        /// </summary>
        public string? Reference { get; private set; }
        /// <summary>
        /// 表示密钥的版本信息。
        /// </summary>
        public string? Version { get; private set; }
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public Task<string> ResolveRequiredAsync(string reference, string version, CancellationToken cancellationToken = default)
        {
            Reference = reference;
            Version = version;
            return Task.FromResult("resolved-token");
        }
        /// <summary>
        /// 验证解析相关行为、预期结果及错误边界。
        /// </summary>
        public Task<string?> ResolveOptionalAsync(string? reference, string? version, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    /// <summary>
    /// 仅供助手模型供应商数据库令牌测试使用的测试辅助类型。
    /// </summary>
    private sealed class NoHttp : IHttpClientFactory
    {
        /// <summary>
        /// 验证创建客户端相关行为、预期结果及错误边界。
        /// </summary>
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Saving a provider must not call a model service.");
    }

    /// <summary>
    /// 仅供助手模型供应商数据库令牌测试使用的测试辅助类型。
    /// </summary>
    public class NoopDbContext : DispatchProxy
    {
        /// <summary>
        /// 验证数据库上下文相关行为、预期结果及错误边界。
        /// </summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.Name switch
            {
                "CommitAsync" => Task.FromResult(1),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
    }
}
