#nullable enable
using AI.Assistant.Models.Knowledge;
using AI.Assistant.Models.Policies;

namespace AI.Assistant.Services.Contracts;

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
    Task<AssistantModelServiceDto> SaveProviderAsync(string? id, SaveAssistantModelServiceCommand command, CancellationToken ct = default);
    /// <summary>
    /// 校验预期修订号后修改供应商启用状态。
    /// </summary>
    Task<AssistantModelServiceDto> SetProviderStatusAsync(string id, AssistantPolicyStatusCommand command, CancellationToken ct = default);
    /// <summary>
    /// 读取业务模型策略。
    /// </summary>
    Task<IReadOnlyList<AssistantBusinessPolicyDto>> ListPoliciesAsync(CancellationToken ct = default);
    /// <summary>
    /// 校验模型及工具绑定后按预期修订保存业务策略。
    /// </summary>
    Task<AssistantBusinessPolicyDto> SavePolicyAsync(string? id, SaveAssistantBusinessPolicyCommand command, CancellationToken ct = default);
    /// <summary>
    /// 按预期修订修改策略启用状态。
    /// </summary>
    Task<AssistantBusinessPolicyDto> SetPolicyStatusAsync(string id, AssistantPolicyStatusCommand command, CancellationToken ct = default);
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
