using System.Text.Json;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using Xunit;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证独立项目工具行为和边界条件的自动化测试集合。
/// </summary>
public sealed class StandaloneToolBoundaryTests
{
    /// <summary>
    /// 验证独立项目发布时拒绝依赖原应用的工具。
    /// </summary>
    [Theory]
    [InlineData("public.event.overview.read")]
    [InlineData("public.event.schedule.read")]
    public async Task Publication_rejects_tools_that_require_the_original_application(string toolId)
    {
        Assert.Null(AssistantToolCatalog.Resolve(toolId, "v1"));
        var policy = new AssistantResolvedPolicy("policy", 1, "service", "provider@1", "provider", "chat", "Answer.", 0.2, 1024,
            false, 5, true, "No answer.", [new(toolId)], true);
        var policies = System.Reflection.DispatchProxy.Create<IAssistantPolicyService, PolicyProxy>();
        ((PolicyProxy)policies).Policy = policy;
        var snapshots = new AssistantExecutionSnapshotService(policies, null!, null!, null!);
        var draft = AssistantDraft.Create("draft", "独立助手", null,
            """{"businessPolicyId":"policy"}""", "actor", null);

        var error = await Assert.ThrowsAsync<BizResultException>(() => snapshots.FreezeAsync(draft, CancellationToken.None));
        Assert.Equal("assistant_tool_not_available", error.BusinessMessage);
        Assert.NotNull(AssistantToolCatalog.Resolve("assistant.web_search", "v1"));
    }

    /// <summary>
    /// 在测试中替代服务依赖，用来记录调用或模拟特定响应。
    /// </summary>
    public class PolicyProxy : System.Reflection.DispatchProxy
    {
        /// <summary>
        /// 表示策略的策略信息。
        /// </summary>
        public AssistantResolvedPolicy Policy { get; set; } = null!;
        /// <summary>
        /// 验证策略相关行为、预期结果及错误边界。
        /// </summary>
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == nameof(IAssistantPolicyService.ResolveAsync)
                ? Task.FromResult(Policy)
                : throw new InvalidOperationException("Unexpected policy call.");
    }
}
