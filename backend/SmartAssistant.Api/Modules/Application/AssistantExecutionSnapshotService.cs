#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 冻结到执行快照中的固定问答，保留标准化问题和推荐问题标记。
/// </summary>
public sealed record AssistantFrozenQuestion(string Id, string Question, string NormalizedQuestion, string? Answer, bool ShowAsSuggestion = false);
/// <summary>
/// 冻结到执行快照中的知识文档版本，避免运行期间读取未发布的新版本。
/// </summary>
public sealed record AssistantFrozenDocument(string Id, string VersionId, int VersionNo, string Name);
/// <summary>
/// 发布时冻结的工具定义，包含契约版本和参数结构。
/// </summary>
public sealed record AssistantFrozenTool(
    string ToolId,
    string ContractVersion,
    string Name,
    string Description,
    JsonElement InputSchema);
/// <summary>
/// 执行快照使用的联网搜索配置版本引用。
/// </summary>
public sealed record AssistantWebSearchExecutionProfile(string ConfigRef);
/// <summary>
/// 发布或预览时冻结的模型策略、知识版本、工具和联网搜索配置。
/// </summary>
public sealed record AssistantExecutionSnapshot(
    int SchemaVersion, AssistantResolvedPolicy Policy, string? KnowledgeBaseId,
    IReadOnlyList<AssistantFrozenQuestion> Questions, IReadOnlyList<AssistantFrozenDocument> Documents,
    IReadOnlyList<AssistantFrozenTool>? Tools = null,
    AssistantKnowledgeEmbeddingExecutionProfile? KnowledgeEmbedding = null,
    AssistantWebSearchExecutionProfile? WebSearch = null);

/// <summary>
/// 为发布与预览生成服务端执行快照。
/// 客户端提交的运行配置会被服务端重新构建，实际运行只使用已校验的绑定和版本。
/// </summary>
public sealed class AssistantExecutionSnapshotService
{
    private readonly IAssistantPolicyService _policies;
    private readonly IAssistantKnowledgeRepository _knowledge;
    private readonly IAssistantKnowledgeDocumentRepository _documents;
    private readonly IAssistantKnowledgeEmbeddingProfileRepository _embeddingProfiles;
    private readonly AssistantWebSearchConfigurationService? _webSearch;
    public AssistantExecutionSnapshotService(IAssistantPolicyService policies,
        IAssistantKnowledgeRepository knowledge, IAssistantKnowledgeDocumentRepository documents,
        IAssistantKnowledgeEmbeddingProfileRepository embeddingProfiles,
        AssistantWebSearchConfigurationService? webSearch = null)
        => (_policies, _knowledge, _documents, _embeddingProfiles, _webSearch) =
            (policies, knowledge, documents, embeddingProfiles, webSearch);

    /// <summary>
    /// 校验展示配置及策略、知识库和外部能力绑定，拒绝尚未接入或不可用的能力。
    /// </summary>
    public async Task ValidateBindingsAsync(string tenantId, string orgId, string json, CancellationToken ct)
    {
        var value = Parse(json);
        using var presentation = JsonDocument.Parse(json);
        AssistantPresentation.Validate(presentation.RootElement);
        var policyId = String(value, "businessPolicyId");
        if (policyId is not null) await _policies.ResolveAsync(tenantId, orgId, policyId, ct);
        var knowledgeId = String(value, "knowledgeBaseId");
        if (knowledgeId is not null && await _knowledge.GetBaseAsync(tenantId, orgId, knowledgeId, ct) is null)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_knowledge_base_not_found");
        if (Enabled(value, "humanHandoff"))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_external_capability_not_ready");
        if (Enabled(value, "webSearch"))
        {
            var availability = _webSearch is null ? null : await _webSearch.GetAvailabilityAsync(ct);
            if (availability is not { Configured: true, Enabled: true })
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_external_capability_not_ready");
        }
    }

    /// <summary>
    /// 读取已校验的服务端配置并冻结模型、知识文档版本和工具定义，生成执行快照 JSON。
    /// </summary>
    public async Task<string> FreezeAsync(AssistantDraft draft, CancellationToken ct)
    {
        var value = Parse(draft.DraftJson);
        var policyId = String(value, "businessPolicyId")
            ?? throw new BizResultException(BusinessCodes.BusinessException, "assistant_policy_required");
        var policy = await _policies.ResolveAsync(draft.TenantId, draft.OrgId, policyId, ct);
        await ValidateBindingsAsync(draft.TenantId, draft.OrgId, draft.DraftJson, ct);
        var knowledgeId = String(value, "knowledgeBaseId");
        var questions = new List<AssistantFrozenQuestion>();
        var documents = new List<AssistantFrozenDocument>();
        var tools = new List<AssistantFrozenTool>();
        AssistantKnowledgeEmbeddingExecutionProfile? embedding = null;
        AssistantWebSearchExecutionProfile? webSearch = null;
        foreach (var binding in policy.Tools ?? Array.Empty<AssistantToolBinding>())
        {
            var definition = AssistantToolCatalog.Resolve(binding.ToolId, binding.ContractVersion)
                ?? throw new BizResultException(BusinessCodes.BusinessException, "assistant_tool_not_available");
            tools.Add(new AssistantFrozenTool(
                definition.ToolId,
                definition.ContractVersion,
                definition.Name,
                definition.Description,
                definition.InputSchema));
        }
        if (Enabled(value, "webSearch"))
        {
            if (!policy.ModelSupportsTools)
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_web_search_model_tools_required");
            var configRef = await (_webSearch
                ?? throw new BizResultException(BusinessCodes.BusinessException, "assistant_external_capability_not_ready"))
                .GetCurrentExecutionRefAsync(ct);
            var definition = AssistantToolCatalog.Resolve("assistant.web_search", "v1")
                ?? throw new BizResultException(BusinessCodes.BusinessException, "assistant_tool_not_available");
            tools.Add(new AssistantFrozenTool(definition.ToolId, definition.ContractVersion,
                definition.Name, definition.Description, definition.InputSchema));
            webSearch = new AssistantWebSearchExecutionProfile(configRef);
        }
        if (knowledgeId is not null)
        {
            if (Enabled(value, "customQa"))
                questions.AddRange((await _knowledge.ListQnaAsync(draft.TenantId, draft.OrgId, knowledgeId, ct))
                    .Where(q => q.Enabled).OrderBy(q => q.SortOrder).ThenBy(q => q.Id, StringComparer.Ordinal)
                    .Select(q => new AssistantFrozenQuestion(q.Id, q.Question, q.NormalizedQuestion, q.Answer, q.ShowAsSuggestion)));
            if (Enabled(value, "knowledgeQa") && policy.KnowledgeEnabled)
            {
                var profile = await _embeddingProfiles.GetAsync(draft.TenantId, draft.OrgId, knowledgeId, ct);
                if (profile is null || profile.ActiveGeneration < 1 || profile.ActiveProviderId is null
                    || profile.ActiveProviderRevision is null || profile.ActiveModel is null || profile.ActiveDimension is null)
                    throw new BizResultException(BusinessCodes.Conflict, "assistant_knowledge_index_not_ready");
                embedding = new(profile.ActiveProviderId, profile.ActiveProviderRevision.Value, profile.ActiveModel,
                    profile.ActiveDimension.Value, profile.ActiveProfileRevision ?? profile.Revision, profile.ActiveGeneration);
                await _policies.EnsureEmbeddingAvailableAsync(embedding, ct);
                var jobs = await _documents.ListIndexJobsAsync(draft.TenantId, draft.OrgId, knowledgeId, ct);
                foreach (var doc in (await _documents.ListDocumentsAsync(draft.TenantId, draft.OrgId, knowledgeId, ct))
                    .OrderBy(d => d.Id, StringComparer.Ordinal))
                {
                    var job = jobs.Where(j => j.DocumentId == doc.Id && j.Status == "succeeded"
                        && j.Generation == embedding.Generation && j.EmbeddingProviderId == embedding.ProviderId
                        && j.EmbeddingProviderRevision == embedding.ProviderRevision && j.EmbeddingModel == embedding.Model
                        && j.EmbeddingDimension == embedding.Dimension).OrderByDescending(j => j.VersionNo).FirstOrDefault();
                    var version = job is null ? null : await _documents.GetVersionAsync(draft.TenantId, draft.OrgId, doc.Id, job.VersionNo, ct);
                    if (version is null || version.Id != job!.DocumentVersionId || version.Status != "ready" || version.IndexStatus != "ready")
                        throw new BizResultException(BusinessCodes.Conflict, "assistant_knowledge_index_not_ready");
                    documents.Add(new(doc.Id, version.Id, version.VersionNo, doc.FileName));
                }
            }
        }
        /*
         * 运行时路由由服务端快照决定，清除旧的客户端路由字段以避免混用。
         */
        value.Remove("providerProfileRef");
        value.Remove("model");
        value["runtime"] = JsonSerializer.SerializeToNode(
            new AssistantExecutionSnapshot(1, policy, knowledgeId, questions, documents, tools, embedding, webSearch), AssistantPolicyService.JsonOptions);
        value["displayName"] = draft.Name;
        value["description"] = draft.Description;
        return value.ToJsonString(AssistantPolicyService.JsonOptions);
    }

    /// <summary>
    /// 从助手配置中读取服务端冻结的执行快照，供运行时选择确定的策略与资源。
    /// </summary>
    public static AssistantExecutionSnapshot Read(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object || !configuration.TryGetProperty("runtime", out var runtime))
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_requires_republish");
        var snapshot = runtime.Deserialize<AssistantExecutionSnapshot>(AssistantPolicyService.JsonOptions);
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.Policy is null)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_snapshot_invalid");
        return snapshot;
    }

    /// <summary>
    /// 在冻结的固定问答中匹配用户问题，命中时返回可直接输出的答案。
    /// </summary>
    public async Task<string?> FindDirectAnswerAsync(AssistantPublicAuthority authority, string text, CancellationToken ct)
    {
        var snapshot = Read(authority.Configuration);
        if (snapshot.KnowledgeBaseId is null) return null;
        var question = snapshot.Questions.FirstOrDefault(q =>
            q.NormalizedQuestion == AssistantKnowledgeService.NormalizeQuestion(text) && !string.IsNullOrWhiteSpace(q.Answer));
        if (question is null) return null;
        /*
         * 发布时冻结的问答正文保持不变，但当前撤回操作必须立即阻止继续使用。
         */
        if (await _knowledge.GetBaseAsync(authority.Scope.TenantId, authority.Scope.OrgId, snapshot.KnowledgeBaseId, ct) is null)
            return null;
        var current = await _knowledge.GetQnaAsync(authority.Scope.TenantId, authority.Scope.OrgId,
            snapshot.KnowledgeBaseId, question.Id, ct);
        return current?.Enabled == true ? question.Answer : null;
    }

    private static JsonObject Parse(string json)
    {
        try { return JsonNode.Parse(json) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new BizResultException(BusinessCodes.BusinessException, "assistant_configuration_invalid"); }
    }
    private static string? String(JsonObject value, string key)
        => value[key] is JsonValue node && node.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    private static bool Enabled(JsonObject value, string key)
        => value["capabilities"] is JsonObject capabilities && capabilities[key] is JsonValue node
            && node.TryGetValue<bool>(out var enabled) && enabled;
}
