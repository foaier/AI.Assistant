#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SmartAssistant.Modules.Domain.Entities;

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 管理联网搜索配置、启停状态和供执行快照引用的配置修订。
/// </summary>
public sealed class AssistantWebSearchConfigurationService
{
    /// <summary>
    /// 定义助手网络检索配置服务使用的配置标识配置值。
    /// </summary>
    public const string ConfigId = "volc-agent-chat";
    /// <summary>
    /// 定义助手网络检索配置服务使用的配置种类配置值。
    /// </summary>
    public const string ConfigKind = "websearch";
    /// <summary>
    /// 定义助手网络检索配置服务使用的默认值服务地址配置值。
    /// </summary>
    public const string DefaultEndpoint = "https://open.feedcoopapi.com/agent_api/agent/chat/completion";
    /// <summary>
    /// 定义助手网络检索配置服务使用的默认值服务名称配置值。
    /// </summary>
    public const string DefaultServiceName = "ask_echo";
    /// <summary>
    /// 定义助手网络检索配置服务使用的默认值机器人标识配置值。
    /// </summary>
    public const string DefaultBotId = "7602814290642355738";
    /// <summary>
    /// 定义助手网络检索配置服务使用的凭据配置值。
    /// </summary>
    private const string MaskedCredential = "••••••••••••";
    /// <summary>
    /// 保存用于助手网络检索配置服务事件编解码的序列化器。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = AssistantPolicyService.JsonOptions;
    /// <summary>
    /// 保存助手策略仓储依赖，供助手网络检索配置服务执行相关操作。
    /// </summary>
    private readonly IAssistantPolicyRepository _repository;
    /// <summary>
    /// 保存数据库上下文依赖，供助手网络检索配置服务执行相关操作。
    /// </summary>
    private readonly IDbContext _db;

    /// <summary>
    /// 接收并保存助手网络检索配置服务执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantWebSearchConfigurationService(IAssistantPolicyRepository repository, IDbContext db)
        => (_repository, _db) = (repository, db);

    /// <summary>
    /// 返回管理端可见的联网搜索设置，并以脱敏信息表示凭据状态。
    /// </summary>
    public async Task<AssistantWebSearchConfigDto> GetForAdminAsync(CancellationToken ct = default)
    {
        var record = await GetRecordAsync(ct);
        return record is null ? EmptyDto() : Map(record, AssistantPolicyService.Read<AssistantWebSearchSettings>(record.SettingsJson));
    }

    /// <summary>
    /// 返回联网搜索是否已配置和启用，供业务能力选择使用。
    /// </summary>
    public async Task<AssistantWebSearchAvailabilityDto> GetAvailabilityAsync(CancellationToken ct = default)
    {
        var record = await GetRecordAsync(ct);
        if (record is null) return new(false, false, null);
        var settings = AssistantPolicyService.Read<AssistantWebSearchSettings>(record.SettingsJson);
        var configured = !string.IsNullOrWhiteSpace(settings.ApiKey);
        return new(configured, record.Enabled && configured, record.Revision);
    }

    /// <summary>
    /// 校验配置与修订号后保护凭据并保存联网搜索配置。
    /// </summary>
    public async Task<AssistantWebSearchConfigDto> SaveAsync(
        string actor, SaveAssistantWebSearchConfigCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required");
        if (command.ExpectedRevision < 0 || command.ExpectedRevision == long.MaxValue)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_web_search_revision_conflict");

        var record = await GetRecordAsync(ct);
        if ((record?.Revision ?? 0) != command.ExpectedRevision)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_web_search_revision_conflict");

        var previous = record is null ? null : AssistantPolicyService.Read<AssistantWebSearchSettings>(record.SettingsJson);
        var submitted = command.Settings ?? throw Invalid("assistant_web_search_settings_required");
        var apiKey = string.IsNullOrWhiteSpace(submitted.ApiKey) ? previous?.ApiKey : submitted.ApiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) throw Invalid("assistant_web_search_credential_required");

        var settings = Validate(submitted with { ApiKey = apiKey });
        record ??= new AssistantPolicyEntity
        {
            Id = ConfigId,
            Kind = ConfigKind,
            Name = "Volcengine Web Search Agent"
        };
        record.Name = "Volcengine Web Search Agent";
        record.Revision = command.ExpectedRevision + 1;
        record.Enabled = record.Revision == 1 || record.Enabled;
        record.SettingsJson = JsonSerializer.Serialize(settings, JsonOptions);
        await _repository.SaveAsync(record, command.ExpectedRevision, ct);
        await _db.CommitAsync(ct);
        return Map(record, settings);
    }

    /// <summary>
    /// 按预期修订号更新联网搜索的启用状态。
    /// </summary>
    public async Task<AssistantWebSearchConfigDto> SetStatusAsync(
        string actor, AssistantPolicyStatusCommand command, CancellationToken ct = default)
    {
        var record = await GetRecordAsync(ct)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_web_search_config_not_found");
        if (record.Revision != command.ExpectedRevision || command.ExpectedRevision == long.MaxValue)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_web_search_revision_conflict");
        var settings = AssistantPolicyService.Read<AssistantWebSearchSettings>(record.SettingsJson);
        if (command.Enabled && string.IsNullOrWhiteSpace(settings.ApiKey))
            throw Invalid("assistant_web_search_credential_required");

        record.Revision++;
        record.Enabled = command.Enabled;
        await _repository.SaveAsync(record, command.ExpectedRevision, ct);
        await _db.CommitAsync(ct);
        return Map(record, settings);
    }

    /// <summary>
    /// 解析本次运行应使用的搜索配置；未配置或不可用时返回受控结果。
    /// </summary>
    internal async Task<(AssistantWebSearchSettings Settings, long Revision)> ResolveForRunAsync(
        string? configRef, CancellationToken ct = default)
    {
        var parts = configRef?.Split('@');
        if (parts is not { Length: 2 } || !string.Equals(parts[0], ConfigId, StringComparison.Ordinal)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            throw Unavailable();
        var current = await GetRecordAsync(ct);
        if (current is null || !current.Enabled) throw Unavailable();
        var frozen = await _repository.GetRevisionAsync(ConfigKind, ConfigId, revision, ct);
        if (frozen is null || !frozen.Enabled) throw Unavailable();
        var settings = Validate(AssistantPolicyService.Read<AssistantWebSearchSettings>(frozen.SettingsJson));
        return (settings, revision);
    }

    /// <summary>
    /// 读取当前启用搜索配置的版本引用，供运行快照冻结使用。
    /// </summary>
    internal async Task<string> GetCurrentExecutionRefAsync(CancellationToken ct = default)
    {
        var value = await GetForAdminAsync(ct);
        if (!value.Enabled || !value.CredentialConfigured)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_external_capability_not_ready");
        return $"{ConfigId}@{value.Revision.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// 读取当前平台搜索服务的配置记录。
    /// </summary>
    private Task<AssistantPolicyEntity?> GetRecordAsync(CancellationToken ct)
        => _repository.GetAsync(ConfigKind, ConfigId, ct);

    /// <summary>
    /// 检查设置中的必填字段、格式和取值范围，不符合约束时拒绝保存或执行。
    /// </summary>
    private static AssistantWebSearchSettings Validate(AssistantWebSearchSettings value)
    {
        if (!Uri.TryCreate(value.Endpoint?.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "open.feedcoopapi.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.Equals(uri.AbsolutePath, "/agent_api/agent/chat/completion", StringComparison.Ordinal)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw Invalid("assistant_web_search_endpoint_invalid");
        if (string.IsNullOrWhiteSpace(value.BotId) || value.BotId.Trim().Length > 128
            || !Regex.IsMatch(value.BotId.Trim(), "^[A-Za-z0-9_-]{1,128}$"))
            throw Invalid("assistant_web_search_bot_id_invalid");
        if (string.IsNullOrWhiteSpace(value.ServiceName) || value.ServiceName.Trim().Length > 64
            || !Regex.IsMatch(value.ServiceName.Trim(), "^[A-Za-z0-9_-]{1,64}$"))
            throw Invalid("assistant_web_search_service_name_invalid");
        if (string.IsNullOrWhiteSpace(value.ApiKey) || value.ApiKey.Trim().Length > 4096
            || value.ApiKey.Any(char.IsControl) || value.ApiKey.Any(char.IsWhiteSpace)
            || value.TimeoutSeconds is < 5 or > 120)
            throw Invalid(string.IsNullOrWhiteSpace(value.ApiKey)
                ? "assistant_web_search_credential_required"
                : "assistant_web_search_settings_invalid");
        return value with
        {
            Endpoint = uri!.ToString().TrimEnd('/'),
            BotId = value.BotId.Trim(),
            ServiceName = value.ServiceName.Trim(),
            ApiKey = value.ApiKey.Trim()
        };
    }

    /// <summary>
    /// 将领域对象转换为应用层返回的数据传输对象。
    /// </summary>
    private static AssistantWebSearchConfigDto Map(AssistantPolicyEntity record, AssistantWebSearchSettings settings)
    {
        var configured = !string.IsNullOrWhiteSpace(settings.ApiKey);
        return new(record.Id, record.Revision, record.Enabled, settings.Endpoint, settings.BotId,
            settings.ServiceName, configured, configured ? MaskedCredential : string.Empty,
            settings.TimeoutSeconds);
    }

    /// <summary>
    /// 生成不含可用配置的助手网络检索配置空数据传输对象。
    /// </summary>
    private static AssistantWebSearchConfigDto EmptyDto()
        => new(ConfigId, 0, false, DefaultEndpoint, DefaultBotId, DefaultServiceName, false, string.Empty, 60);

    /// <summary>
    /// 构造包含稳定业务错误码和消息的参数校验异常。
    /// </summary>
    private static BizResultException Invalid(string code)
        => new(BusinessCodes.BusinessException, code);

    /// <summary>
    /// 构造助手网络检索配置当前不可用时返回的稳定状态或错误结果。
    /// </summary>
    private static BizResultException Unavailable()
        => new(BusinessCodes.Forbidden, "assistant_web_search_config_unavailable");
}
