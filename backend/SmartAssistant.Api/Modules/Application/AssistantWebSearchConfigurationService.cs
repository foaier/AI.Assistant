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

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 管理联网搜索配置、启停状态和供执行快照引用的配置修订。
/// </summary>
public sealed class AssistantWebSearchConfigurationService
{
    public const string ConfigId = "volc-agent-chat";
    public const string ConfigKind = "websearch";
    public const string DefaultEndpoint = "https://open.feedcoopapi.com/agent_api/agent/chat/completion";
    public const string DefaultServiceName = "ask_echo";
    public const string DefaultBotId = "7602814290642355738";
    private const string MaskedCredential = "••••••••••••";
    private static readonly string PlatformScope = AssistantPolicyService.PlatformScope;
    private static readonly JsonSerializerOptions JsonOptions = AssistantPolicyService.JsonOptions;
    private readonly IAssistantPolicyRepository _repository;
    private readonly IDbContext _db;

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
        var now = DateTime.UtcNow;
        record ??= new AssistantPolicyRecord
        {
            Id = ConfigId,
            TenantId = PlatformScope,
            OrgId = PlatformScope,
            Kind = ConfigKind,
            Name = "Volcengine Web Search Agent",
            CreatedAt = now,
            CreatedBy = actor
        };
        record.Name = "Volcengine Web Search Agent";
        record.Revision = command.ExpectedRevision + 1;
        record.Enabled = record.Revision == 1 || record.Enabled;
        record.SettingsJson = JsonSerializer.Serialize(settings, JsonOptions);
        record.UpdatedAt = now;
        record.UpdatedBy = actor;
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
        record.UpdatedAt = DateTime.UtcNow;
        record.UpdatedBy = actor;
        await _repository.SaveAsync(record, command.ExpectedRevision, ct);
        await _db.CommitAsync(ct);
        return Map(record, settings);
    }

    internal async Task<(AssistantWebSearchSettings Settings, long Revision)> ResolveForRunAsync(
        string? configRef, CancellationToken ct = default)
    {
        var parts = configRef?.Split('@');
        if (parts is not { Length: 2 } || !string.Equals(parts[0], ConfigId, StringComparison.Ordinal)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            throw Unavailable();
        var current = await GetRecordAsync(ct);
        if (current is null || !current.Enabled) throw Unavailable();
        var frozen = await _repository.GetRevisionAsync(PlatformScope, PlatformScope, ConfigKind, ConfigId, revision, ct);
        if (frozen is null || !frozen.Enabled) throw Unavailable();
        var settings = Validate(AssistantPolicyService.Read<AssistantWebSearchSettings>(frozen.SettingsJson));
        return (settings, revision);
    }

    internal async Task<string> GetCurrentExecutionRefAsync(CancellationToken ct = default)
    {
        var value = await GetForAdminAsync(ct);
        if (!value.Enabled || !value.CredentialConfigured)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_external_capability_not_ready");
        return $"{ConfigId}@{value.Revision.ToString(CultureInfo.InvariantCulture)}";
    }

    private Task<AssistantPolicyRecord?> GetRecordAsync(CancellationToken ct)
        => _repository.GetAsync(PlatformScope, PlatformScope, ConfigKind, ConfigId, ct);

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

    private static AssistantWebSearchConfigDto Map(AssistantPolicyRecord record, AssistantWebSearchSettings settings)
    {
        var configured = !string.IsNullOrWhiteSpace(settings.ApiKey);
        return new(record.Id, record.Revision, record.Enabled, settings.Endpoint, settings.BotId,
            settings.ServiceName, configured, configured ? MaskedCredential : string.Empty,
            settings.TimeoutSeconds, record.UpdatedAt);
    }

    private static AssistantWebSearchConfigDto EmptyDto()
        => new(ConfigId, 0, false, DefaultEndpoint, DefaultBotId, DefaultServiceName, false, string.Empty, 60, DateTime.UnixEpoch);

    private static BizResultException Invalid(string code)
        => new(BusinessCodes.BusinessException, code);

    private static BizResultException Unavailable()
        => new(BusinessCodes.Forbidden, "assistant_web_search_config_unavailable");
}
