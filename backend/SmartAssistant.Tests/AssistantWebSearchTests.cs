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
using SmartAssistant.Services.Application;
using SmartAssistant.Services.Infrastructure;
using SmartAssistant.Services.Infrastructure.AI;
using SmartAssistant.Services.Contracts;
using SmartAssistant.Services.Domain;
using SmartAssistant.Platform;
using Xunit;
using SmartAssistant.Services.Domain.Entities;
using SmartAssistant.Models.AI;
using SmartAssistant.Models.Policies;
using SmartAssistant.Models.Runtime;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证助手网络检索行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantWebSearchTests
{
    /// <summary>
    /// 验证平台配置脱敏凭据，空白更新保留已有值，并遵守启用状态和修订约束。
    /// </summary>
    [Fact]
    public async Task Platform_config_masks_credential_preserves_blank_updates_and_honors_disable_and_revision()
    {
        var repository = new MemoryPolicyRepository();
        var db = DispatchProxy.Create<IDbContext, NoopDbContext>();
        var service = new AssistantWebSearchConfigurationService(repository, db);
        const string testCredential = "test-only-volc-key";

        var first = await service.SaveAsync(new(
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

        var second = await service.SaveAsync(new(
            new(first.Endpoint, "rotated-bot", first.ServiceName, "", first.TimeoutSeconds), first.Revision));
        Assert.Equal(2, second.Revision);
        Assert.DoesNotContain(testCredential, JsonSerializer.Serialize(second));
        Assert.Contains(testCredential, repository.Current!.SettingsJson);

        var disabled = await service.SetStatusAsync(new(false, second.Revision));
        Assert.False(disabled.Enabled);
        var unavailable = await service.GetAvailabilityAsync();
        Assert.True(unavailable.Configured);
        Assert.False(unavailable.Enabled);
        await Assert.ThrowsAsync<BizResultException>(() => InvokeInternalAsync<(AssistantWebSearchSettings Settings, long Revision)>(
            service, "ResolveForRunAsync", "volc-agent-chat@1", CancellationToken.None));

        await service.SetStatusAsync(new(true, disabled.Revision));
        var frozen = await InvokeInternalAsync<(AssistantWebSearchSettings Settings, long Revision)>(
            service, "ResolveForRunAsync", "volc-agent-chat@1", CancellationToken.None);
        Assert.Equal(AssistantWebSearchConfigurationService.DefaultBotId, frozen.Settings.BotId);
        Assert.Equal(testCredential, frozen.Settings.ApiKey);
        Assert.DoesNotContain(testCredential, JsonSerializer.Serialize(
            new AssistantWebSearchExecutionProfile("volc-agent-chat@1")));

        var conflict = await Assert.ThrowsAsync<BizResultException>(() => service.SaveAsync(new(
            new(first.Endpoint, first.BotId, first.ServiceName, "replacement-key"), first.Revision)));
        Assert.Equal("assistant_web_search_revision_conflict", conflict.BusinessMessage);
    }

    /// <summary>
    /// 验证火山引擎搜索适配器限制查询内容，并解析 SSE 回答及引用。
    /// </summary>
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

    /// <summary>
    /// 验证发布快照冻结配置修订和查询工具结构。
    /// </summary>
    [Fact]
    public async Task Published_snapshot_freezes_only_config_revision_and_query_tool_schema()
    {
        var repository = new MemoryPolicyRepository();
        var db = DispatchProxy.Create<IDbContext, NoopDbContext>();
        var webSearch = new AssistantWebSearchConfigurationService(repository, db);
        const string testCredential = "test-only-volc-key";
        await webSearch.SaveAsync(new(Settings(testCredential), 0));

        var policy = DispatchProxy.Create<IAssistantPolicyService, StaticPolicyService>();
        ((StaticPolicyService)(object)policy).Policy = ResolvedPolicy(supportsTools: true);
        var knowledge = DispatchProxy.Create<IAssistantKnowledgeRepository, UnexpectedCallProxy>();
        var documents = DispatchProxy.Create<IAssistantKnowledgeDocumentRepository, UnexpectedCallProxy>();
        var embeddingProfiles = DispatchProxy.Create<IAssistantKnowledgeEmbeddingProfileRepository, UnexpectedCallProxy>();
        var snapshots = new AssistantExecutionSnapshotService(policy, knowledge, documents, embeddingProfiles, webSearch);
        var draft = AssistantDraft.Create("assistant-1", "客服", null,
            "{\"businessPolicyId\":\"policy-1\",\"capabilities\":{\"webSearch\":true}}",
            null);

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

    /// <summary>
    /// 验证搜索工具使用冻结配置，且返回结果受长度限制。
    /// </summary>
    [Fact]
    public async Task Web_search_tool_handler_uses_frozen_config_and_returns_only_bounded_search_result()
    {
        var repository = new MemoryPolicyRepository();
        var db = DispatchProxy.Create<IDbContext, NoopDbContext>();
        var configuration = new AssistantWebSearchConfigurationService(repository, db);
        const string testCredential = "test-only-volc-key";
        await configuration.SaveAsync(new(Settings(testCredential), 0));
        var client = new StaticWebSearchClient();
        var handler = new AssistantVolcengineWebSearchToolHandler(configuration, client);
        using var input = JsonDocument.Parse("{\"query\":\"展会开放时间\"}");
        var scope = new AssistantScopeSnapshot(
            "assistant-1", "published-1", AssistantExecutionMode.Public,
            "volc-agent-chat@1");

        var output = await handler.ExecuteAsync(input.RootElement, scope);

        Assert.Equal("展会开放时间", client.Query);
        Assert.Equal(testCredential, client.Settings?.ApiKey);
        Assert.Equal("web_search_result", output.GetProperty("type").GetString());
        Assert.Equal("已找到展会开放时间。", output.GetProperty("answer").GetString());
        Assert.Equal("https://example.test/source", output.GetProperty("references")[0].GetProperty("url").GetString());
        Assert.DoesNotContain(testCredential, output.GetRawText());
    }

    /// <summary>
    /// 验证火山引擎适配器脱敏供应商错误，并拒绝不完整的响应流。
    /// </summary>
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

    /// <summary>
    /// 验证 HTTP 200 中的 JSON 鉴权错误会映射为脱敏供应商错误。
    /// </summary>
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

    /// <summary>
    /// 验证设置相关行为、预期结果及错误边界。
    /// </summary>
    private static AssistantWebSearchSettings Settings(string key)
        => new(AssistantWebSearchConfigurationService.DefaultEndpoint,
            AssistantWebSearchConfigurationService.DefaultBotId,
            AssistantWebSearchConfigurationService.DefaultServiceName, key, 5);

    /// <summary>
    /// 验证策略相关行为、预期结果及错误边界。
    /// </summary>
    private static AssistantResolvedPolicy ResolvedPolicy(bool supportsTools)
        => new("policy-1", 1, "customer_service", "provider-1@1", "test provider", "test-model",
            "Answer in Chinese.", 0.2, 1024, KnowledgeEnabled: false, RetrievalLimit: 5, RequireEvidence: true,
            NoAnswerMessage: "No answer.", ModelSupportsTools: supportsTools);

    /// <summary>
    /// 验证助手网络检索测试相关行为、预期结果及错误边界。
    /// </summary>
    private static Task<T> InvokeInternalAsync<T>(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing internal method: {methodName}");
        return (Task<T>)(method.Invoke(target, arguments)
            ?? throw new InvalidOperationException($"Internal method returned null: {methodName}"));
    }

    /// <summary>
    /// 测试用仓储替身，按预设数据实现所需查询并避免连接外部数据库。
    /// </summary>
    private sealed class MemoryPolicyRepository : IAssistantPolicyRepository
    {
        /// <summary>
        /// 保存助手策略记录依赖，供策略仓储执行相关操作。
        /// </summary>
        private AssistantPolicyEntity? _current;
        /// <summary>
        /// 表示策略仓储的当前信息。
        /// </summary>
        public AssistantPolicyEntity? Current => _current is null ? null : Clone(_current);
        /// <summary>
        /// 表示策略仓储的修订信息。
        /// </summary>
        public List<AssistantPolicyRevisionEntity> Revisions { get; } = new();

        /// <summary>
        /// 验证列出相关行为、预期结果及错误边界。
        /// </summary>
        public Task<IReadOnlyList<AssistantPolicyEntity>> ListAsync(string kind, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AssistantPolicyEntity>>(Array.Empty<AssistantPolicyEntity>());

        /// <summary>
        /// 验证获取相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPolicyEntity?> GetAsync(string kind, string id, CancellationToken ct)
            => Task.FromResult(_current is not null
                && _current.Kind == kind && _current.Id == id ? Clone(_current) : null);

        /// <summary>
        /// 验证获取模型供应商相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPolicyEntity?> GetProviderAsync(string id, CancellationToken ct)
            => Task.FromResult<AssistantPolicyEntity?>(null);

        /// <summary>
        /// 验证获取修订相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantPolicyRevisionEntity?> GetRevisionAsync(
            string kind, string id, long revision, CancellationToken ct)
        {
            var frozen = Revisions.FirstOrDefault(item => item.RecordId == id && item.Kind == kind && item.Revision == revision);
            return Task.FromResult(frozen is null || _current?.Enabled != true || !frozen.Enabled
                ? null
                : new AssistantPolicyRevisionEntity
                {
                    Id = frozen.Id,
                    RecordId = frozen.RecordId,
                    Kind = frozen.Kind,
                    Revision = frozen.Revision,
                    Enabled = frozen.Enabled,
                    SettingsJson = frozen.SettingsJson
                });
        }

        /// <summary>
        /// 验证保存相关行为、预期结果及错误边界。
        /// </summary>
        public Task SaveAsync(AssistantPolicyEntity record, long expectedRevision, CancellationToken ct)
        {
            if ((_current?.Revision ?? 0) != expectedRevision)
                throw new BizResultException(BusinessCodes.Conflict, "assistant_policy_revision_conflict");
            _current = Clone(record);
            Revisions.Add(new AssistantPolicyRevisionEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                RecordId = record.Id,
                Kind = record.Kind,
                Revision = record.Revision,
                Enabled = record.Enabled,
                SettingsJson = record.SettingsJson
            });
            return Task.CompletedTask;
        }

        /// <summary>
        /// 验证策略仓储相关行为、预期结果及错误边界。
        /// </summary>
        private static AssistantPolicyEntity Clone(AssistantPolicyEntity value)
            => new()
            {
                Id = value.Id,
                Kind = value.Kind,
                Name = value.Name,
                BusinessCode = value.BusinessCode,
                Revision = value.Revision,
                Enabled = value.Enabled,
                SettingsJson = value.SettingsJson
            };
    }

    /// <summary>
    /// 测试用请求处理器，用于验证 HTTP 请求和响应边界。
    /// </summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>
        /// 表示处理器的授权信息。
        /// </summary>
        public AuthenticationHeaderValue? Authorization { get; private set; }
        /// <summary>
        /// 保存搜索服务使用的服务名称。
        /// </summary>
        public string? ServiceName { get; private set; }
        /// <summary>
        /// 表示处理器的请求正文信息。
        /// </summary>
        public string? RequestBody { get; private set; }

        /// <summary>
        /// 验证发送相关行为、预期结果及错误边界。
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            ServiceName = request.Headers.GetValues("ServiceName").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    /// <summary>
    /// 仅供助手网络检索测试使用的测试辅助类型。
    /// </summary>
    private sealed class RecordingClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        /// <summary>
        /// 验证创建客户端相关行为、预期结果及错误边界。
        /// </summary>
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// 测试用客户端替身，用于控制下游结果并核对调用参数。
    /// </summary>
    private sealed class StaticWebSearchClient : IAssistantWebSearchClient
    {
        /// <summary>
        /// 表示网络检索客户端的设置信息。
        /// </summary>
        public AssistantWebSearchSettings? Settings { get; private set; }
        /// <summary>
        /// 保存发出的检索查询文本。
        /// </summary>
        public string? Query { get; private set; }

        /// <summary>
        /// 验证检索相关行为、预期结果及错误边界。
        /// </summary>
        public Task<AssistantWebSearchResult> SearchAsync(
            AssistantWebSearchSettings settings, string query, CancellationToken ct = default)
        {
            Settings = settings;
            Query = query;
            return Task.FromResult(new AssistantWebSearchResult("已找到展会开放时间。",
                new[] { new AssistantWebSearchReference("活动日程", "https://example.test/source", "开放安排") }));
        }
    }

    /// <summary>
    /// 仅供助手网络检索测试使用的测试辅助类型。
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

    /// <summary>
    /// 仅供助手网络检索测试使用的测试辅助类型。
    /// </summary>
    public class StaticPolicyService : DispatchProxy
    {
        /// <summary>
        /// 表示策略服务的策略信息。
        /// </summary>
        public AssistantResolvedPolicy Policy { get; set; } = ResolvedPolicy(supportsTools: true);

        /// <summary>
        /// 验证策略服务相关行为、预期结果及错误边界。
        /// </summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.Name switch
            {
                "ResolveAsync" => Task.FromResult(Policy),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
    }

    /// <summary>
    /// 在测试中替代服务依赖，用来记录调用或模拟特定响应。
    /// </summary>
    public class UnexpectedCallProxy : DispatchProxy
    {
        /// <summary>
        /// 验证调用相关行为、预期结果及错误边界。
        /// </summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException($"Unexpected repository call: {targetMethod!.Name}");
    }
}
