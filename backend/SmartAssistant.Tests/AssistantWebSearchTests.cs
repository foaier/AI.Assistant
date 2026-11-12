// Spec: specs/136-public-assistant/requests/REQ-20261106-014-volcengine-agent-web-search.md
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Infrastructure.AI;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Platform;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantWebSearchTests
{
    [Fact]
    public async Task Platform_config_masks_credential_preserves_blank_updates_and_honors_disable_and_revision()
    {
        var repository = new MemoryPolicyRepository();
        var db = DispatchProxy.Create<IDbContext, NoopDbContext>();
        var service = new AssistantWebSearchConfigurationService(repository, db);
        const string testCredential = "test-only-volc-key";

        var first = await service.SaveAsync("platform-admin", new(
            new(AssistantWebSearchConfigurationService.DefaultEndpoint,
                AssistantWebSearchConfigurationService.DefaultBotId,
                AssistantWebSearchConfigurationService.DefaultServiceName, testCredential), 0));

        Assert.True(first.Enabled);
        Assert.True(first.CredentialConfigured);
        Assert.NotEqual(testCredential, first.CredentialMasked);
        Assert.DoesNotContain(testCredential, JsonSerializer.Serialize(first));
        var reread = await service.GetForAdminAsync();
        Assert.Equal(first.CredentialMasked, reread.CredentialMasked);
        Assert.DoesNotContain(testCredential, JsonSerializer.Serialize(reread));
        Assert.Contains(testCredential, repository.Current!.SettingsJson);
        Assert.Contains(testCredential, repository.Revisions.Single().SettingsJson);
        Assert.Equal("volc-agent-chat@1", await InvokeInternalAsync<string>(
            service, "GetCurrentExecutionRefAsync", CancellationToken.None));

        var second = await service.SaveAsync("platform-admin", new(
            new(first.Endpoint, "rotated-bot", first.ServiceName, "", first.TimeoutSeconds), first.Revision));
        Assert.Equal(2, second.Revision);
        Assert.DoesNotContain(testCredential, JsonSerializer.Serialize(second));
        Assert.Contains(testCredential, repository.Current!.SettingsJson);

        var disabled = await service.SetStatusAsync("platform-admin", new(false, second.Revision));
        Assert.False(disabled.Enabled);
        var unavailable = await service.GetAvailabilityAsync();
        Assert.True(unavailable.Configured);
        Assert.False(unavailable.Enabled);
        await Assert.ThrowsAsync<BizResultException>(() => InvokeInternalAsync<(AssistantWebSearchSettings Settings, long Revision)>(
            service, "ResolveForRunAsync", "volc-agent-chat@1", CancellationToken.None));

        await service.SetStatusAsync("platform-admin", new(true, disabled.Revision));
        var frozen = await InvokeInternalAsync<(AssistantWebSearchSettings Settings, long Revision)>(
            service, "ResolveForRunAsync", "volc-agent-chat@1", CancellationToken.None);
        Assert.Equal(AssistantWebSearchConfigurationService.DefaultBotId, frozen.Settings.BotId);
        Assert.Equal(testCredential, frozen.Settings.ApiKey);
        Assert.DoesNotContain(testCredential, JsonSerializer.Serialize(
            new AssistantWebSearchExecutionProfile("volc-agent-chat@1")));

        var conflict = await Assert.ThrowsAsync<BizResultException>(() => service.SaveAsync("platform-admin", new(
            new(first.Endpoint, first.BotId, first.ServiceName, "replacement-key"), first.Revision)));
        Assert.Equal("assistant_web_search_revision_conflict", conflict.BusinessMessage);
    }

    [Fact]
    public async Task Volcengine_adapter_posts_bounded_search_query_and_parses_sse_answer_and_references()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "data: {\"choices\":[{\"delta\":{\"content\":\"检索结果\"}}]}\n\n"
                + "data: {\"search_results\":[{\"title\":\"来源\",\"url\":\"https://example.test/page\",\"summary\":\"摘要\"}]}\n\n"
                + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n",
                Encoding.UTF8, "text/event-stream")
        });
        var client = new VolcengineAssistantWebSearchClient(new RecordingClientFactory(handler));

        var result = await client.SearchAsync(Settings("test-only-key"), " 展会开放时间 ");

        Assert.Equal("检索结果", result.Answer);
        var reference = Assert.Single(result.References);
        Assert.Equal("来源", reference.Title);
        Assert.Equal("https://example.test/page", reference.Url);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("test-only-key", handler.Authorization?.Parameter);
        Assert.Equal("ask_echo", handler.ServiceName);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(AssistantWebSearchConfigurationService.DefaultBotId,
            request.RootElement.GetProperty("bot_id").GetString());
        Assert.Equal("展会开放时间", request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.True(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.DoesNotContain("test-only-key", handler.RequestBody);
    }

    [Fact]
    public async Task Published_snapshot_freezes_only_config_revision_and_query_tool_schema()
    {
        var repository = new MemoryPolicyRepository();
        var db = DispatchProxy.Create<IDbContext, NoopDbContext>();
        var webSearch = new AssistantWebSearchConfigurationService(repository, db);
        const string testCredential = "test-only-volc-key";
        await webSearch.SaveAsync("platform-admin", new(Settings(testCredential), 0));

        var policy = DispatchProxy.Create<IAssistantPolicyService, StaticPolicyService>();
        ((StaticPolicyService)(object)policy).Policy = ResolvedPolicy(supportsTools: true);
        var knowledge = DispatchProxy.Create<IAssistantKnowledgeRepository, UnexpectedCallProxy>();
        var documents = DispatchProxy.Create<IAssistantKnowledgeDocumentRepository, UnexpectedCallProxy>();
        var embeddingProfiles = DispatchProxy.Create<IAssistantKnowledgeEmbeddingProfileRepository, UnexpectedCallProxy>();
        var snapshots = new AssistantExecutionSnapshotService(policy, knowledge, documents, embeddingProfiles, webSearch);
        var draft = AssistantDraft.Create("assistant-1", "tenant-1", "org-1", "客服", null,
            "{\"businessPolicyId\":\"policy-1\",\"capabilities\":{\"webSearch\":true}}",
            "admin-1", null, DateTime.UtcNow);

        var json = await snapshots.FreezeAsync(draft, CancellationToken.None);

        using var snapshot = JsonDocument.Parse(json);
        var runtime = snapshot.RootElement.GetProperty("runtime");
        Assert.Equal("volc-agent-chat@1", runtime.GetProperty("webSearch").GetProperty("configRef").GetString());
        var tool = Assert.Single(runtime.GetProperty("tools").EnumerateArray());
        Assert.Equal("web_search", tool.GetProperty("name").GetString());
        Assert.Equal("query", tool.GetProperty("inputSchema").GetProperty("required")[0].GetString());
        Assert.DoesNotContain(testCredential, json);

        ((StaticPolicyService)(object)policy).Policy = ResolvedPolicy(supportsTools: false);
        var unsupported = await Assert.ThrowsAsync<BizResultException>(
            () => snapshots.FreezeAsync(draft, CancellationToken.None));
        Assert.Equal("assistant_web_search_model_tools_required", unsupported.BusinessMessage);
        Assert.DoesNotContain(testCredential, unsupported.Message);
    }

    [Fact]
    public async Task Web_search_tool_handler_uses_frozen_config_and_returns_only_bounded_search_result()
    {
        var repository = new MemoryPolicyRepository();
        var db = DispatchProxy.Create<IDbContext, NoopDbContext>();
        var configuration = new AssistantWebSearchConfigurationService(repository, db);
        const string testCredential = "test-only-volc-key";
        await configuration.SaveAsync("platform-admin", new(Settings(testCredential), 0));
        var client = new StaticWebSearchClient();
        var handler = new AssistantVolcengineWebSearchToolHandler(configuration, client);
        using var input = JsonDocument.Parse("{\"query\":\"展会开放时间\"}");
        var scope = new AssistantScopeSnapshot(
            "assistant-1", "tenant-1", "org-1", "published-1", AssistantExecutionMode.Public,
            "volc-agent-chat@1");

        var output = await handler.ExecuteAsync(input.RootElement, scope);

        Assert.Equal("展会开放时间", client.Query);
        Assert.Equal(testCredential, client.Settings?.ApiKey);
        Assert.Equal("web_search_result", output.GetProperty("type").GetString());
        Assert.Equal("已找到展会开放时间。", output.GetProperty("answer").GetString());
        Assert.Equal("https://example.test/source", output.GetProperty("references")[0].GetProperty("url").GetString());
        Assert.DoesNotContain(testCredential, output.GetRawText());
    }

    [Fact]
    public async Task Volcengine_adapter_sanitizes_provider_errors_and_rejects_incomplete_streams()
    {
        const string testCredential = "test-only-key";
        var failedHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent($"provider echoed {testCredential}")
        });
        var failedClient = new VolcengineAssistantWebSearchClient(new RecordingClientFactory(failedHandler));
        var providerFailure = await Assert.ThrowsAsync<BizResultException>(
            () => failedClient.SearchAsync(Settings(testCredential), "query"));
        Assert.Equal("assistant_web_search_provider_failed", providerFailure.BusinessMessage);
        Assert.DoesNotContain(testCredential, providerFailure.Message);
        Assert.DoesNotContain("provider echoed", providerFailure.Message);

        var incompleteHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n",
                Encoding.UTF8, "text/event-stream")
        });
        var incompleteClient = new VolcengineAssistantWebSearchClient(new RecordingClientFactory(incompleteHandler));
        var incomplete = await Assert.ThrowsAsync<BizResultException>(
            () => incompleteClient.SearchAsync(Settings(testCredential), "query"));
        Assert.Equal("assistant_web_search_response_invalid", incomplete.BusinessMessage);
        Assert.DoesNotContain(testCredential, incomplete.Message);
    }

    [Fact]
    public async Task Volcengine_adapter_maps_http_200_json_authentication_error_to_sanitized_provider_failure()
    {
        const string testCredential = "test-only-key";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"error\":{\"code\":\"invalid_api_key\",\"message\":\"invalid api key\"}}",
                Encoding.UTF8, "application/json")
        });
        var client = new VolcengineAssistantWebSearchClient(new RecordingClientFactory(handler));

        var failure = await Assert.ThrowsAsync<BizResultException>(
            () => client.SearchAsync(Settings(testCredential), "query"));

        Assert.Equal("assistant_web_search_provider_failed", failure.BusinessMessage);
        Assert.DoesNotContain(testCredential, failure.Message);
        Assert.DoesNotContain("invalid_api_key", failure.Message);
    }

    private static AssistantWebSearchSettings Settings(string key)
        => new(AssistantWebSearchConfigurationService.DefaultEndpoint,
            AssistantWebSearchConfigurationService.DefaultBotId,
            AssistantWebSearchConfigurationService.DefaultServiceName, key, 5);

    private static AssistantResolvedPolicy ResolvedPolicy(bool supportsTools)
        => new("policy-1", 1, "customer_service", "provider-1@1", "test provider", "test-model",
            "Answer in Chinese.", 0.2, 1024, KnowledgeEnabled: false, RetrievalLimit: 5, RequireEvidence: true,
            NoAnswerMessage: "No answer.", ModelSupportsTools: supportsTools);

    private static Task<T> InvokeInternalAsync<T>(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing internal method: {methodName}");
        return (Task<T>)(method.Invoke(target, arguments)
            ?? throw new InvalidOperationException($"Internal method returned null: {methodName}"));
    }

    private sealed class MemoryPolicyRepository : IAssistantPolicyRepository
    {
        private AssistantPolicyRecord? _current;
        public AssistantPolicyRecord? Current => _current is null ? null : Clone(_current);
        public List<AssistantPolicyRevisionRecord> Revisions { get; } = new();

        public Task<IReadOnlyList<AssistantPolicyRecord>> ListAsync(string tenantId, string orgId, string kind, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantPolicyRecord>>(Array.Empty<AssistantPolicyRecord>());

        public Task<AssistantPolicyRecord?> GetAsync(string tenantId, string orgId, string kind, string id, CancellationToken ct)
            => Task.FromResult(_current is not null && _current.TenantId == tenantId && _current.OrgId == orgId
                && _current.Kind == kind && _current.Id == id ? Clone(_current) : null);

        public Task<AssistantPolicyRecord?> GetProviderAsync(string id, CancellationToken ct)
            => Task.FromResult<AssistantPolicyRecord?>(null);

        public Task<AssistantPolicyRevisionRecord?> GetRevisionAsync(
            string tenantId, string orgId, string kind, string id, long revision, CancellationToken ct)
        {
            var frozen = Revisions.FirstOrDefault(item => item.RecordId == id && item.TenantId == tenantId
                && item.OrgId == orgId && item.Kind == kind && item.Revision == revision);
            return Task.FromResult(frozen is null || _current?.Enabled != true || !frozen.Enabled
                ? null
                : new AssistantPolicyRevisionRecord
                {
                    Id = frozen.Id, RecordId = frozen.RecordId, TenantId = frozen.TenantId, OrgId = frozen.OrgId,
                    Kind = frozen.Kind, Revision = frozen.Revision, Enabled = frozen.Enabled,
                    SettingsJson = frozen.SettingsJson
                });
        }

        public Task SaveAsync(AssistantPolicyRecord record, long expectedRevision, CancellationToken ct)
        {
            if ((_current?.Revision ?? 0) != expectedRevision)
                throw new BizResultException(BusinessCodes.Conflict, "assistant_policy_revision_conflict");
            _current = Clone(record);
            Revisions.Add(new AssistantPolicyRevisionRecord
            {
                Id = Guid.NewGuid().ToString("N"), RecordId = record.Id, TenantId = record.TenantId,
                OrgId = record.OrgId, Kind = record.Kind, Revision = record.Revision,
                Enabled = record.Enabled, SettingsJson = record.SettingsJson
            });
            return Task.CompletedTask;
        }

        private static AssistantPolicyRecord Clone(AssistantPolicyRecord value)
            => new()
            {
                Id = value.Id, TenantId = value.TenantId, OrgId = value.OrgId, Kind = value.Kind,
                Name = value.Name, BusinessCode = value.BusinessCode, Revision = value.Revision,
                Enabled = value.Enabled, SettingsJson = value.SettingsJson, CreatedAt = value.CreatedAt,
                CreatedBy = value.CreatedBy, UpdatedAt = value.UpdatedAt, UpdatedBy = value.UpdatedBy,
                IsDeleted = value.IsDeleted, DeletedAt = value.DeletedAt, DeletedBy = value.DeletedBy
            };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? ServiceName { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            ServiceName = request.Headers.GetValues("ServiceName").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class RecordingClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StaticWebSearchClient : IAssistantWebSearchClient
    {
        public AssistantWebSearchSettings? Settings { get; private set; }
        public string? Query { get; private set; }

        public Task<AssistantWebSearchResult> SearchAsync(
            AssistantWebSearchSettings settings, string query, CancellationToken ct = default)
        {
            Settings = settings;
            Query = query;
            return Task.FromResult(new AssistantWebSearchResult("已找到展会开放时间。",
                new[] { new AssistantWebSearchReference("活动日程", "https://example.test/source", "开放安排") }));
        }
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

    public class StaticPolicyService : DispatchProxy
    {
        public AssistantResolvedPolicy Policy { get; set; } = ResolvedPolicy(supportsTools: true);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.Name switch
            {
                "ResolveAsync" => Task.FromResult(Policy),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
    }

    public class UnexpectedCallProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException($"Unexpected repository call: {targetMethod!.Name}");
    }
}
