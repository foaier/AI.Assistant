// Spec: specs/136-public-assistant/requests/REQ-20261105-012-platform-model-services-immediate-availability.md
#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using AI.Assistant.Services.Application;
using AI.Assistant.Services.Infrastructure;
using AI.Assistant.Services.Infrastructure.AI;
using AI.Assistant.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;
using AI.Assistant.Services.Domain.Entities;
using AI.Assistant.Models.AI;
using AI.Assistant.Models.Policies;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证助手可用模型供应商行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantAvailableProvidersTests
{
    /// <summary>
    /// 验证已启用的平台聊天供应商均可选择，无需额外业务配置。
    /// </summary>
    [Fact]
    public async Task Policy_choices_include_enabled_platform_chat_providers_without_operational_settings()
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

    /// <summary>
    /// 验证已启用平台服务的凭据可解析，并兼容旧授权记录。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enabled_platform_service_credentials_resolve_even_with_legacy_grants(bool legacyGrants)
    {
        var id = Guid.NewGuid().ToString("N");
        var provider = Provider(id, "chat", true, legacyGrants);
        var secrets = new AssistantPolicyTestDatabase.TestSecrets();
        var resolver = new AssistantProviderCredentialResolver(new StaticRepository(new[] { provider }), secrets,
            new AssistantProviderSecretProtector(new ConfigurationBuilder().Build()));

        var credential = await resolver.ResolveAsync(new AssistantProviderAccess($"{id}@1", "model"));
        Assert.Equal("https://models.example.test/", credential.BaseUrl.ToString());
        Assert.Equal("test-key-material", credential.ApiKey);
        Assert.Equal(1, secrets.Calls);
    }

    /// <summary>
    /// 校验模型供应商设置及其可用模型约束。
    /// </summary>
    private static AssistantPolicyEntity Provider(string id, string purpose, bool enabled, bool legacyGrants = false)
    {
        var settings = new AssistantModelServiceSettings(id, "https://models.example.test", "env://MODEL_KEY", "v1",
            new[] { new AssistantModelOption("model", purpose) });
        var json = JsonNode.Parse(JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();
        if (legacyGrants) json["grants"] = new JsonArray();
        return new AssistantPolicyEntity
        {
            Id = id,
            Name = id,
            Kind = "provider",
            Revision = 1,
            Enabled = enabled,
            SettingsJson = json.ToJsonString()
        };
    }

    /// <summary>
    /// 测试用仓储替身，按预设数据实现所需查询并避免连接外部数据库。
    /// </summary>
    private sealed class StaticRepository(IReadOnlyList<AssistantPolicyEntity> records) : IAssistantPolicyRepository
    {
        /// <summary>
        /// 验证列出相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantPolicyEntity>> ListAsync(string kind, CancellationToken ct)
            => Task.FromResult(records);
        /// <summary>
        /// 验证获取相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPolicyEntity?> GetAsync(string kind, string id, CancellationToken ct)
            => Task.FromResult(records.FirstOrDefault(record => record.Kind == kind && record.Id == id));
        /// <summary>
        /// 验证获取模型供应商相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPolicyEntity?> GetProviderAsync(string id, CancellationToken ct)
            => throw new NotSupportedException();
        /// <summary>
        /// 验证获取修订相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPolicyRevisionEntity?> GetRevisionAsync(string kind, string id, long revision, CancellationToken ct)
        {
            var record = records.FirstOrDefault(item => item.Kind == kind && item.Id == id && item.Revision == revision);
            return Task.FromResult(record is null ? null : new AssistantPolicyRevisionEntity
            {
                RecordId = id,
                Kind = kind,
                Revision = revision,
                Enabled = record.Enabled,
                SettingsJson = record.SettingsJson
            });
        }
        /// <summary>
        /// 验证保存相关行为、预期结果及错误边界。
        /// </summary>
        public Task SaveAsync(AssistantPolicyEntity record, long expectedRevision, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
