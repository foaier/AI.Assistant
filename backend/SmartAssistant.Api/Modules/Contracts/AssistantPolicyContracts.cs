#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartAssistant.Modules.Contracts;

/// <summary>
/// 模型供应商暴露的模型能力，包括用途、工具调用支持和嵌入维度。
/// </summary>
public sealed record AssistantModelOption(string Id, string Purpose, bool SupportsTools = false, int? Dimension = null);
/// <summary>
/// 模型供应商配置，包含服务地址、凭据引用和可用模型列表。
/// </summary>
public sealed record AssistantModelServiceSettings(
    string Name, string BaseUrl, string SecretReference, string SecretVersion,
    IReadOnlyList<AssistantModelOption> Models, int TimeoutSeconds = 90);
/// <summary>
/// 模型供应商配置的管理视图，包含修订和启用状态。
/// </summary>
public sealed record AssistantModelServiceDto(
    string Id, string Name, long Revision, bool Enabled, IReadOnlyList<AssistantModelOption> Models,
    AssistantModelServiceSettings? Settings);
/// <summary>
/// 业务策略绑定的工具标识与契约版本。
/// </summary>
public sealed record AssistantToolBinding(string ToolId, string ContractVersion = "v1");
/// <summary>
/// 业务模型策略，约束供应商、模型、提示词、知识能力和可用工具。
/// </summary>
public sealed record AssistantBusinessPolicySettings(
    string Name, string BusinessCode, string ProviderId, long ProviderRevision, string Model,
    string Instructions, double Temperature = 0.2, int MaxOutputTokens = 2048,
    bool KnowledgeEnabled = true, int RetrievalLimit = 5, bool RequireEvidence = true,
    string NoAnswerMessage = "暂时没有找到可靠的信息，请联系工作人员。",
    bool WebSearchEnabled = false, bool HumanHandoffEnabled = false,
    IReadOnlyList<AssistantToolBinding>? Tools = null,
    bool ModelSupportsTools = false);
/// <summary>
/// 业务策略的管理视图，包含当前设置、修订和启用状态。
/// </summary>
public sealed record AssistantBusinessPolicyDto(
    string Id, long Revision, bool Enabled, AssistantBusinessPolicySettings Settings);
/// <summary>
/// 保存模型供应商设置并校验预期修订号的命令。
/// </summary>
public sealed record SaveAssistantModelServiceCommand(AssistantModelServiceSettings Settings, long ExpectedRevision);
/// <summary>
/// 保存业务策略并校验预期修订号的命令。
/// </summary>
public sealed record SaveAssistantBusinessPolicyCommand(AssistantBusinessPolicySettings Settings, long ExpectedRevision);
/// <summary>
/// 按预期修订号切换模型供应商或策略的启用状态。
/// </summary>
public sealed record AssistantPolicyStatusCommand(bool Enabled, long ExpectedRevision);
/// <summary>
/// 解析后的可执行业务策略，绑定确定的模型供应商修订和模型能力。
/// </summary>
public sealed record AssistantResolvedPolicy(
    string PolicyId, long PolicyRevision, string BusinessCode, string ProviderProfileRef,
    string ProviderName, string Model, string Instructions, double Temperature, int MaxOutputTokens,
    bool KnowledgeEnabled, int RetrievalLimit, bool RequireEvidence, string NoAnswerMessage,
    IReadOnlyList<AssistantToolBinding>? Tools = null,
    bool ModelSupportsTools = false);
/// <summary>
/// 模型供应商连接检查结果及诊断信息。
/// </summary>
public sealed record AssistantConnectionCheck(bool Success, string Message, DateTime CheckedAt);
/// <summary>
/// 联网搜索服务配置，包含服务接入与凭据设置。
/// </summary>
public sealed record AssistantWebSearchSettings(string Endpoint, string BotId, string ServiceName, string ApiKey, int TimeoutSeconds = 60);
/// <summary>
/// 供系统管理端查看的联网搜索配置及修订状态。
/// </summary>
public sealed record AssistantWebSearchConfigDto(
    string Id,
    long Revision,
    bool Enabled,
    string Endpoint,
    string BotId,
    string ServiceName,
    bool CredentialConfigured,
    string CredentialMasked,
    int TimeoutSeconds);
/// <summary>
/// 供业务端判断是否可以启用联网搜索的可用性信息。
/// </summary>
public sealed record AssistantWebSearchAvailabilityDto(bool Configured, bool Enabled, long? Revision);
/// <summary>
/// 保存联网搜索设置并检测并发修改的命令。
/// </summary>
public sealed record SaveAssistantWebSearchConfigCommand(AssistantWebSearchSettings Settings, long ExpectedRevision);

/// <summary>
/// 定义模型供应商和业务策略的配置、启停、解析及连接检查操作。
/// </summary>
public interface IAssistantPolicyService
{
    /// <summary>
    /// 验证冻结的嵌入供应商修订、模型和维度仍可使用。
    /// </summary>
    Task EnsureEmbeddingAvailableAsync(AssistantKnowledgeEmbeddingExecutionProfile profile, CancellationToken ct = default);
    /// <summary>
    /// 列出模型供应商，可按调用场景决定是否包含设置详情。
    /// </summary>
    Task<IReadOnlyList<AssistantModelServiceDto>> ListProvidersAsync(bool includeSettings, CancellationToken ct = default);
    /// <summary>
    /// 筛选已启用且可供聊天策略使用的模型供应商。
    /// </summary>
    Task<IReadOnlyList<AssistantModelServiceDto>> ListAvailableChatProvidersAsync(CancellationToken ct = default);
    /// <summary>
    /// 读取供应商编辑数据，并按凭据展示规则处理敏感字段。
    /// </summary>
    Task<AssistantModelServiceDto> GetProviderForEditAsync(string id, CancellationToken ct = default);
    /// <summary>
    /// 校验供应商设置并保护凭据，按预期修订保存当前配置与历史版本。
    /// </summary>
    Task<AssistantModelServiceDto> SaveProviderAsync(string actor, string? id, SaveAssistantModelServiceCommand command, CancellationToken ct = default);
    /// <summary>
    /// 校验预期修订号后修改供应商启用状态。
    /// </summary>
    Task<AssistantModelServiceDto> SetProviderStatusAsync(string actor, string id, AssistantPolicyStatusCommand command, CancellationToken ct = default);
    /// <summary>
    /// 读取业务模型策略。
    /// </summary>
    Task<IReadOnlyList<AssistantBusinessPolicyDto>> ListPoliciesAsync(CancellationToken ct = default);
    /// <summary>
    /// 校验模型及工具绑定后按预期修订保存业务策略。
    /// </summary>
    Task<AssistantBusinessPolicyDto> SavePolicyAsync(string actor, string? id, SaveAssistantBusinessPolicyCommand command, CancellationToken ct = default);
    /// <summary>
    /// 按预期修订修改策略启用状态。
    /// </summary>
    Task<AssistantBusinessPolicyDto> SetPolicyStatusAsync(string actor, string id, AssistantPolicyStatusCommand command, CancellationToken ct = default);
    /// <summary>
    /// 解析有效业务策略及其绑定的供应商修订，生成可冻结的执行配置。
    /// </summary>
    Task<AssistantResolvedPolicy> ResolveAsync(string policyId, CancellationToken ct = default);
    /// <summary>
    /// 在运行前复核冻结策略的当前可用性，使紧急停用对已有快照生效。
    /// </summary>
    Task EnsureAvailableAsync(AssistantResolvedPolicy snapshot, CancellationToken ct = default);
    /// <summary>
    /// 检查指定供应商的连接配置并返回成功或失败诊断。
    /// </summary>
    Task<AssistantConnectionCheck> CheckProviderAsync(string id, CancellationToken ct = default);
}
