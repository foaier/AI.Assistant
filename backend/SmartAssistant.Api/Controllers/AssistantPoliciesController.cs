#nullable enable
using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Models.Common;
using SmartAssistant.Models.Policies;
using SmartAssistant.Services.Application;
using SmartAssistant.Services.Contracts;

namespace SmartAssistant.Controllers;

/// <summary>
/// 提供业务策略及可用模型和联网搜索能力的管理接口。
/// </summary>
[ApiController, Route("api/admin/assistant-models")]
public sealed class AssistantPoliciesController : ControllerBase
{
    /// <summary>
    /// 保存助手策略服务依赖，供助手策略控制器执行相关操作。
    /// </summary>
    private readonly IAssistantPolicyService _service;
    /// <summary>
    /// 保存助手网络检索配置服务依赖，供助手策略控制器执行相关操作。
    /// </summary>
    private readonly AssistantWebSearchConfigurationService _webSearch;
    /// <summary>
    /// 接收并保存助手策略控制器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantPoliciesController(IAssistantPolicyService service, AssistantWebSearchConfigurationService webSearch)
        => (_service, _webSearch) = (service, webSearch);

    /// <summary>
    /// 返回业务端可见的联网搜索配置可用性。
    /// </summary>
    [HttpGet("web-search/availability")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<BizResult<AssistantWebSearchAvailabilityDto>> WebSearchAvailability(CancellationToken ct)
    {
        NoStore();
        return BizResult.BusinessSuccess(await _webSearch.GetAvailabilityAsync(ct));
    }

    /// <summary>
    /// 返回业务策略可引用的模型供应商信息。
    /// </summary>
    [HttpGet("providers")]
    public async Task<BizResult<IReadOnlyList<AssistantModelServiceDto>>> Providers(CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.ListProvidersAsync(false, ct));
    }
    /// <summary>
    /// 返回当前可用于聊天的模型供应商列表。
    /// </summary>
    [HttpGet("providers/available")]
    public async Task<BizResult<IReadOnlyList<AssistantModelServiceDto>>> AvailableProviders(CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.ListAvailableChatProvidersAsync(ct));
    }
    /// <summary>
    /// 返回业务模型策略。
    /// </summary>
    [HttpGet("policies")]
    public async Task<BizResult<IReadOnlyList<AssistantBusinessPolicyDto>>> Policies(CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.ListPoliciesAsync(ct));
    }
    /// <summary>
    /// 创建业务策略。
    /// </summary>
    [HttpPost("policies")]
    public Task<BizResult<AssistantBusinessPolicyDto>> CreatePolicy([FromBody] SaveAssistantBusinessPolicyCommand command, CancellationToken ct) => SavePolicy(null, command, ct);
    /// <summary>
    /// 按预期修订更新业务策略。
    /// </summary>
    [HttpPut("policies/{id}")]
    public Task<BizResult<AssistantBusinessPolicyDto>> UpdatePolicy(string id, [FromBody] SaveAssistantBusinessPolicyCommand command, CancellationToken ct) => SavePolicy(id, command, ct);
    /// <summary>
    /// 校验并保存业务策略配置及其修订信息。
    /// </summary>
    private async Task<BizResult<AssistantBusinessPolicyDto>> SavePolicy(string? id, SaveAssistantBusinessPolicyCommand command, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.SavePolicyAsync(id, command, ct));
    }
    /// <summary>
    /// 按预期修订切换策略启用状态。
    /// </summary>
    [HttpPut("policies/{id}/status")]
    public async Task<BizResult<AssistantBusinessPolicyDto>> PolicyStatus(string id, [FromBody] AssistantPolicyStatusCommand command, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.SetPolicyStatusAsync(id, command, ct));
    }

    /// <summary>
    /// 设置禁止缓存的响应标头，避免浏览器或代理保存接口返回的数据。
    /// </summary>
    private void NoStore()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
    }
}
