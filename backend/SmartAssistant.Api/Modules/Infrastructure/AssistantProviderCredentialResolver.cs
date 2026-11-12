#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Infrastructure.AI;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Security;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 从数据库冻结修订解析供应商配置，并在读取凭据前检查当前紧急停用状态。
/// </summary>
public sealed class AssistantProviderCredentialResolver : IAssistantProviderCredentialResolver
{
    private readonly IAssistantPolicyRepository _repository;
    private readonly ICredentialMaterialResolver _credentialMaterialResolver;
    private readonly AssistantProviderSecretProtector _secretProtector;

    public AssistantProviderCredentialResolver(
        IAssistantPolicyRepository repository,
        ICredentialMaterialResolver credentialMaterialResolver,
        AssistantProviderSecretProtector secretProtector)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _credentialMaterialResolver = credentialMaterialResolver
            ?? throw new ArgumentNullException(nameof(credentialMaterialResolver));
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
    }

    /// <summary>
    /// 校验供应商可用状态和模型用途后，解析冻结修订中的地址及凭据。
    /// </summary>
    public async ValueTask<OpenAiCompatibleProviderCredential> ResolveAsync(
        AssistantProviderAccess access,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(access.TenantId) || string.IsNullOrWhiteSpace(access.OrgId))
            throw Unavailable();
        var parts = access.ProviderProfileRef.Split('@');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out _)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            throw Unavailable();
        var current = await _repository.GetAsync(AssistantPolicyService.PlatformScope,
            AssistantPolicyService.PlatformScope, "provider", parts[0], cancellationToken);
        if (current is null || !current.Enabled) throw Unavailable();
        var version = await _repository.GetRevisionAsync(AssistantPolicyService.PlatformScope,
            AssistantPolicyService.PlatformScope, "provider", parts[0], revision, cancellationToken);
        if (version is null || !version.Enabled) throw Unavailable();
        var settings = AssistantPolicyValidation.Provider(
            AssistantPolicyService.Read<AssistantModelServiceSettings>(version.SettingsJson));
        var selectedModel = settings.Models.FirstOrDefault(m => m.Id == access.Model && m.Purpose == access.Purpose);
        if (selectedModel is null || access.Purpose == "embedding"
            && (access.Dimension is null or < 1 || selectedModel.Dimension != access.Dimension))
            throw Unavailable();
        var apiKey = await AssistantModelSecret.ResolveAsync(
            _credentialMaterialResolver, _secretProtector, settings.SecretReference, parts[0],
            settings.SecretVersion,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_provider_credential_missing");

        return new OpenAiCompatibleProviderCredential(
            new Uri(settings.BaseUrl),
            apiKey.Trim(),
            Timeout: TimeSpan.FromSeconds(settings.TimeoutSeconds));
    }

    private static BizResultException Unavailable()
        => new(BusinessCodes.Forbidden, "assistant_provider_profile_unavailable");
}
