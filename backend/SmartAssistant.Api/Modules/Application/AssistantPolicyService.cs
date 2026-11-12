#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Security;
using SmartAssistant.Modules.Domain.Entities;

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 管理模型供应商与业务策略的修订、启停和可用性校验。
/// </summary>
public sealed class AssistantPolicyService : IAssistantPolicyService
{
    /*
     * 为平台级模型供应商保留独立的存储范围。
     */
    /// <summary>
    /// 保存用于助手策略服务事件编解码的序列化器。
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    /// <summary>
    /// 定义助手策略服务使用的已配置凭据配置值。
    /// </summary>
    public const string ConfiguredCredentialMarker = "[configured]";
    /// <summary>
    /// 保存助手策略仓储依赖，供助手策略服务执行相关操作。
    /// </summary>
    private readonly IAssistantPolicyRepository _repository;
    /// <summary>
    /// 保存数据库上下文依赖，供助手策略服务执行相关操作。
    /// </summary>
    private readonly IDbContext _db;
    /// <summary>
    /// 保存凭据材料解析器依赖，供助手策略服务执行相关操作。
    /// </summary>
    private readonly ICredentialMaterialResolver _secrets;
    /// <summary>
    /// 保存助手模型供应商密钥依赖，供助手策略服务执行相关操作。
    /// </summary>
    private readonly AssistantProviderSecretProtector _secretProtector;
    /// <summary>
    /// 保存复用的 HTTP 传输依赖，用于访问配置的模型或搜索服务。
    /// </summary>
    private readonly IHttpClientFactory _http;
    /// <summary>
    /// 接收并保存助手策略服务执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantPolicyService(IAssistantPolicyRepository repository, IDbContext db, ICredentialMaterialResolver secrets,
        IHttpClientFactory http, AssistantProviderSecretProtector secretProtector)
        => (_repository, _db, _secrets, _http, _secretProtector) = (repository, db, secrets, http, secretProtector);

    /// <summary>
    /// 列出模型供应商，可按调用场景决定是否包含设置详情。
    /// </summary>
    public async Task<IReadOnlyList<AssistantModelServiceDto>> ListProvidersAsync(bool includeSettings, CancellationToken ct = default)
        => (await _repository.ListAsync("provider", ct))
            .Select(x => ProviderDto(x, includeSettings)).ToArray();

    /// <summary>
    /// 筛选已启用且可供聊天策略使用的模型供应商。
    /// </summary>
    public async Task<IReadOnlyList<AssistantModelServiceDto>> ListAvailableChatProvidersAsync(CancellationToken ct = default)
        => (await ListProvidersAsync(false, ct))
            .Where(x => x.Enabled && x.Models.Any(model => model.Purpose == "chat")).ToArray();

    /// <summary>
    /// 读取供应商编辑数据，并按凭据展示规则处理敏感字段。
    /// </summary>
    public async Task<AssistantModelServiceDto> GetProviderForEditAsync(string id, CancellationToken ct = default)
    {
        var record = await _repository.GetAsync("provider", id, ct)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_provider_not_found");
        var settings = Read<AssistantModelServiceSettings>(record.SettingsJson);
        var credential = await AssistantModelSecret.ResolveAsync(_secrets, _secretProtector,
            settings.SecretReference, id, settings.SecretVersion, ct);
        if (string.IsNullOrWhiteSpace(credential))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_provider_credential_missing");
        return new(record.Id, record.Name, record.Revision, record.Enabled, settings.Models,
            settings with { SecretReference = credential });
    }

    /// <summary>
    /// 验证冻结的嵌入供应商修订、模型和维度仍可使用。
    /// </summary>
    public async Task EnsureEmbeddingAvailableAsync(
        AssistantKnowledgeEmbeddingExecutionProfile profile, CancellationToken ct = default)
    {
        var current = await _repository.GetAsync("provider", profile.ProviderId, ct);
        if (current is null || !current.Enabled)
            throw new BizResultException(BusinessCodes.Forbidden, "knowledge_embedding_model_unavailable");
        var revision = await _repository.GetRevisionAsync("provider",
            profile.ProviderId, profile.ProviderRevision, ct);
        if (revision is null || !revision.Enabled || profile.Dimension is < 1 or > 32768
            || !Read<AssistantModelServiceSettings>(revision.SettingsJson).Models.Any(model =>
                model.Id == profile.Model && model.Purpose == "embedding" && model.Dimension == profile.Dimension))
            throw new BizResultException(BusinessCodes.Forbidden, "knowledge_embedding_model_unavailable");
    }

    /// <summary>
    /// 校验供应商设置并保护凭据，按预期修订保存当前配置与历史版本。
    /// </summary>
    public async Task<AssistantModelServiceDto> SaveProviderAsync(string actor, string? id, SaveAssistantModelServiceCommand command, CancellationToken ct = default)
    {
        var incoming = command.Settings;
        if (id is not null)
        {
            var existing = await _repository.GetAsync("provider", id, ct)
                ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_provider_not_found");
            var existingSettings = Read<AssistantModelServiceSettings>(existing.SettingsJson);
            if (string.IsNullOrWhiteSpace(incoming.SecretReference)
                || string.Equals(incoming.SecretReference, ConfiguredCredentialMarker, StringComparison.Ordinal))
            {
                incoming = incoming with
                {
                    SecretReference = existingSettings.SecretReference,
                    SecretVersion = existingSettings.SecretVersion
                };
            }
            else
            {
                try
                {
                    var existingCredential = await AssistantModelSecret.ResolveAsync(_secrets, _secretProtector,
                        existingSettings.SecretReference, id, existingSettings.SecretVersion, ct);
                    if (string.Equals(incoming.SecretReference.Trim(), existingCredential, StringComparison.Ordinal))
                    {
                        incoming = incoming with
                        {
                            SecretReference = existingSettings.SecretReference,
                            SecretVersion = existingSettings.SecretVersion
                        };
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    /*
                     * 旧凭据引用无法读取时仍允许管理员替换，避免损坏配置阻塞修复。
                     */
                }
            }
        }
        var settings = AssistantPolicyValidation.Provider(incoming);
        var record = await PrepareAsync(actor, "provider", id, command.ExpectedRevision, ct);
        /*
         * 供应商凭据存储在数据库配置中，返回给 API 的供应商视图负责脱敏。
         */
        record.Name = settings.Name;
        record.SettingsJson = JsonSerializer.Serialize(settings, JsonOptions);
        await SaveAsync(record, command.ExpectedRevision, ct);
        return ProviderDto(record, true);
    }
    /// <summary>
    /// 校验预期修订号后修改供应商启用状态。
    /// </summary>
    public async Task<AssistantModelServiceDto> SetProviderStatusAsync(string actor, string id, AssistantPolicyStatusCommand command, CancellationToken ct = default)
    {
        var record = await PrepareAsync(actor, "provider", id, command.ExpectedRevision, ct);
        record.Enabled = command.Enabled;
        await SaveAsync(record, command.ExpectedRevision, ct);
        return ProviderDto(record, true);
    }
    /// <summary>
    /// 读取业务模型策略。
    /// </summary>
    public async Task<IReadOnlyList<AssistantBusinessPolicyDto>> ListPoliciesAsync(CancellationToken ct = default)
        => (await _repository.ListAsync("policy", ct)).Select(PolicyDto).ToArray();

    /// <summary>
    /// 校验模型及工具绑定后按预期修订保存业务策略。
    /// </summary>
    public async Task<AssistantBusinessPolicyDto> SavePolicyAsync(string actor, string? id, SaveAssistantBusinessPolicyCommand command, CancellationToken ct = default)
    {
        var settings = AssistantPolicyValidation.Policy(command.Settings);
        await RequireProviderVersionAsync(settings, ct, includeAvailabilityReason: true);
        var record = await PrepareAsync(actor, "policy", id, command.ExpectedRevision, ct);
        record.Name = settings.Name;
        record.BusinessCode = settings.BusinessCode;
        record.SettingsJson = JsonSerializer.Serialize(settings, JsonOptions);
        await SaveAsync(record, command.ExpectedRevision, ct);
        return PolicyDto(record);
    }
    /// <summary>
    /// 按预期修订修改策略启用状态。
    /// </summary>
    public async Task<AssistantBusinessPolicyDto> SetPolicyStatusAsync(string actor, string id, AssistantPolicyStatusCommand command, CancellationToken ct = default)
    {
        var record = await PrepareAsync(actor, "policy", id, command.ExpectedRevision, ct);
        if (command.Enabled) await RequireProviderVersionAsync(
            Read<AssistantBusinessPolicySettings>(record.SettingsJson), ct, includeAvailabilityReason: true);
        record.Enabled = command.Enabled;
        await SaveAsync(record, command.ExpectedRevision, ct);
        return PolicyDto(record);
    }
    /// <summary>
    /// 解析有效业务策略及其绑定的供应商修订，生成可冻结的执行配置。
    /// </summary>
    public async Task<AssistantResolvedPolicy> ResolveAsync(string policyId, CancellationToken ct = default)
    {
        var policy = await _repository.GetAsync("policy", policyId, ct);
        if (policy is null || !policy.Enabled) throw new BizResultException(BusinessCodes.NotFound, "assistant_policy_unavailable");
        var value = AssistantPolicyValidation.Policy(Read<AssistantBusinessPolicySettings>(policy.SettingsJson));
        var provider = await RequireProviderVersionAsync(value, ct);
        return new(policy.Id, policy.Revision, value.BusinessCode,
            value.ProviderId + "@" + value.ProviderRevision.ToString(CultureInfo.InvariantCulture), provider.Name,
            value.Model, value.Instructions, value.Temperature, value.MaxOutputTokens,
            value.KnowledgeEnabled, value.RetrievalLimit, value.RequireEvidence, value.NoAnswerMessage, value.Tools,
            provider.Models.Any(model => model.Id == value.Model && model.Purpose == "chat" && model.SupportsTools));
    }

    /// <summary>
    /// 检查指定供应商的连接配置并返回成功或失败诊断。
    /// </summary>
    public async Task<AssistantConnectionCheck> CheckProviderAsync(string id, CancellationToken ct = default)
    {
        var record = await _repository.GetAsync("provider", id, ct);
        if (record is null || !record.Enabled) throw new BizResultException(BusinessCodes.NotFound, "assistant_provider_unavailable");
        var settings = Read<AssistantModelServiceSettings>(record.SettingsJson);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var secret = await AssistantModelSecret.ResolveAsync(_secrets, _secretProtector,
                settings.SecretReference, id, settings.SecretVersion, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoint(settings.BaseUrl, "models"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var response = await _http.CreateClient("AssistantModelConnection").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                var configuredModels = settings.Models.Select(model => model.Id).Distinct(StringComparer.Ordinal).ToArray();
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                var availableModels = ReadModelIds(body);
                var missing = configuredModels
                    .Where(model => !availableModels.Contains(model))
                    .ToArray();
                if (missing.Length > 0)
                    return new(false,
                        $"连接成功，但服务未返回已配置模型：{string.Join("、", missing)}。请检查模型名称和服务地址。",
                        DateTime.UtcNow);
                return new(true, "连接成功，已确认模型服务和已配置模型。", DateTime.UtcNow);
            }

            var message = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    ? "模型服务拒绝了访问凭据，请检查密钥权限、服务区域和凭据版本。"
                    : response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "服务地址未提供 OpenAI 兼容的模型列表接口，请填写兼容地址（通常以 /v1 结尾）。"
                        : "模型服务未通过连接检查，请检查服务地址、访问凭据和服务状态。";
            return new(response.IsSuccessStatusCode, message, DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, "连接超时，请稍后重试。", DateTime.UtcNow); }
        catch (HttpRequestException) { return new(false, "无法连接模型服务，请检查服务地址和服务器网络。", DateTime.UtcNow); }
        catch (BizResultException ex)
        {
            var message = ex.Message switch
            {
                "assistant_provider_credential_missing" => "模型 Token 未配置，请填写有效的 API Token。",
                "assistant_provider_profile_unavailable" => "模型服务版本不可用，请确认服务及所选版本已启用。",
                _ when ex.Message.StartsWith("payment_credential_material_missing:", StringComparison.Ordinal)
                    => "服务端凭据未找到，请检查凭据引用、版本和本机环境变量配置。",
                _ => "模型服务配置不可用，请检查 API Token 和服务版本。"
            };
            return new(false, message, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// 组合并规范化供应商聊天接口地址，同时拒绝不受支持的 URI 形式。
    /// </summary>
    public static Uri ApiEndpoint(string baseUrl, string path)
    {
        var root = baseUrl.TrimEnd('/');
        return new Uri(root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? $"{root}/{path}" : $"{root}/v1/{path}");
    }

    /// <summary>
    /// 从供应商配置读取模型列表，并提取可选模型标识用于策略校验。
    /// </summary>
    private static HashSet<string> ReadModelIds(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
                return new HashSet<string>(StringComparer.Ordinal);

            return data.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("id").GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
    /// <summary>
    /// 按供应商标识与指定修订读取历史配置；缺失或不可用时返回业务错误。
    /// </summary>
    private async Task<AssistantModelServiceSettings> RequireProviderVersionAsync(
        AssistantBusinessPolicySettings settings, CancellationToken ct, bool includeAvailabilityReason = false)
    {
        var current = await _repository.GetAsync("provider", settings.ProviderId, ct);
        if (current is null) throw new BizResultException(BusinessCodes.NotFound,
            includeAvailabilityReason ? "assistant_provider_not_found" : "assistant_provider_unavailable");
        if (!current.Enabled) throw new BizResultException(BusinessCodes.NotFound,
            includeAvailabilityReason ? "assistant_provider_disabled" : "assistant_provider_unavailable");
        var version = await _repository.GetRevisionAsync("provider", settings.ProviderId, settings.ProviderRevision, ct);
        if (version is null || !version.Enabled) throw new BizResultException(BusinessCodes.NotFound,
            includeAvailabilityReason ? "assistant_provider_revision_unavailable" : "assistant_provider_unavailable");
        var provider = Read<AssistantModelServiceSettings>(version.SettingsJson);
        if (!provider.Models.Any(x => x.Id == settings.Model && x.Purpose == "chat"))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_policy_model_not_supported");
        return provider;
    }
    /// <summary>
    /// 在运行前复核冻结策略的当前可用性，使紧急停用对已有快照生效。
    /// </summary>
    public async Task EnsureAvailableAsync(AssistantResolvedPolicy snapshot, CancellationToken ct = default)
    {
        var current = await _repository.GetAsync("policy", snapshot.PolicyId, ct);
        if (current is null || !current.Enabled)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_policy_unavailable");
        var revision = await _repository.GetRevisionAsync("policy", snapshot.PolicyId, snapshot.PolicyRevision, ct);
        if (revision is null || !revision.Enabled)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_policy_unavailable");
        await RequireProviderVersionAsync(Read<AssistantBusinessPolicySettings>(revision.SettingsJson), ct);
    }
    /// <summary>
    /// 验证并规范化供应商或策略设置，为持久化和修订存档准备数据。
    /// </summary>
    private async Task<AssistantPolicyEntity> PrepareAsync(string actor, string kind, string? id, long expected, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required");
        if (expected < 0 || expected == long.MaxValue) throw new BizResultException(BusinessCodes.Conflict, "assistant_policy_revision_conflict");
        AssistantPolicyEntity record;
        if (id is null)
        {
            if (expected != 0) throw new BizResultException(BusinessCodes.Conflict, "assistant_policy_revision_conflict");
            record = new() { Id = Guid.NewGuid().ToString("N"), Kind = kind };
        }
        else
        {
            record = await _repository.GetAsync(kind, id, ct)
                ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_policy_not_found");
            if (record.Revision != expected) throw new BizResultException(BusinessCodes.Conflict, "assistant_policy_revision_conflict");
        }
        record.Revision = expected + 1;
        return record;
    }
    /// <summary>
    /// 保存助手策略及其关联状态，并响应调用方的取消请求。
    /// </summary>
    private async Task SaveAsync(AssistantPolicyEntity record, long expectedRevision, CancellationToken ct)
    {
        await _repository.SaveAsync(record, expectedRevision, ct);
        await _db.CommitAsync(ct);
    }
    /// <summary>
    /// 读取 JSON 配置并转换为指定设置类型；格式无效时报告业务校验错误。
    /// </summary>
    internal static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new BizResultException(BusinessCodes.BusinessException, "assistant_policy_configuration_invalid");
    /// <summary>
    /// 生成脱敏的供应商管理视图，不将凭据原文返回到 API。
    /// </summary>
    private static AssistantModelServiceDto ProviderDto(AssistantPolicyEntity value, bool includeSettings)
    {
        var settings = Read<AssistantModelServiceSettings>(value.SettingsJson);
        if (includeSettings)
        {
            settings = settings with
            {
                SecretReference = IsSafeReference(settings.SecretReference)
                    ? settings.SecretReference
                    : ConfiguredCredentialMarker
            };
        }
        return new(value.Id, value.Name, value.Revision, value.Enabled, settings.Models, includeSettings ? settings : null);
    }
    /// <summary>
    /// 判断凭据引用格式是否属于支持的本地配置或环境变量来源。
    /// </summary>
    private static bool IsSafeReference(string value)
        => value.StartsWith("env://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("config://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("apollo://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("db://", StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// 将策略记录映射为供管理界面读取的数据传输对象。
    /// </summary>
    private static AssistantBusinessPolicyDto PolicyDto(AssistantPolicyEntity value)
        => new(value.Id, value.Revision, value.Enabled, Read<AssistantBusinessPolicySettings>(value.SettingsJson));
}
