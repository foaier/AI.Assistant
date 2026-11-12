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
    /// <summary>
    /// 保存助手策略仓储依赖，供助手模型供应商凭据解析器执行相关操作。
    /// </summary>
    private readonly IAssistantPolicyRepository _repository;
    /// <summary>
    /// 保存凭据材料解析器依赖，供助手模型供应商凭据解析器执行相关操作。
    /// </summary>
    private readonly ICredentialMaterialResolver _credentialMaterialResolver;
    /// <summary>
    /// 保存助手模型供应商密钥依赖，供助手模型供应商凭据解析器执行相关操作。
    /// </summary>
    private readonly AssistantProviderSecretProtector _secretProtector;

    /// <summary>
    /// 接收并保存助手模型供应商凭据解析器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
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
        var parts = access.ProviderProfileRef.Split('@');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out _)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            throw Unavailable();
        var current = await _repository.GetAsync("provider", parts[0], cancellationToken);
        if (current is null || !current.Enabled) throw Unavailable();
        var version = await _repository.GetRevisionAsync("provider", parts[0], revision, cancellationToken);
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

    /// <summary>
    /// 构造助手模型供应商凭据当前不可用时返回的稳定状态或错误结果。
    /// </summary>
    private static BizResultException Unavailable()
        => new(BusinessCodes.Forbidden, "assistant_provider_profile_unavailable");
}
