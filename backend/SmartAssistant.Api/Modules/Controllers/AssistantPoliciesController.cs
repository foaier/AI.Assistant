#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Authentication;
using SmartAssistant.Modules.Admin.Public.Contracts;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Modules.Application;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Tenant;

using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Modules.Controllers;

/// <summary>
/// 提供业务策略及可用模型和联网搜索能力的管理接口。
/// </summary>
[ApiController, Route("api/admin/assistant-models")]
public sealed class AssistantPoliciesController : ControllerBase
{
    private readonly IAssistantPolicyService _service;
    private readonly AssistantWebSearchConfigurationService _webSearch;
    private readonly ITenantContext _tenant;
    private readonly IAdminPermissionService _permissions;
    public AssistantPoliciesController(IAssistantPolicyService service, AssistantWebSearchConfigurationService webSearch,
        ITenantContext tenant, IAdminPermissionService permissions)
        => (_service, _webSearch, _tenant, _permissions) = (service, webSearch, tenant, permissions);

    /// <summary>
    /// 返回业务端可见的联网搜索配置可用性。
    /// </summary>
    [HttpGet("web-search/availability")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<BizResult<AssistantWebSearchAvailabilityDto>> WebSearchAvailability(CancellationToken ct)
    {
        var (actor, _, org) = Scope();
        await ReadPermission(actor, org);
        NoStore();
        return BizResult.BusinessSuccess(await _webSearch.GetAvailabilityAsync(ct));
    }

    /// <summary>
    /// 返回业务策略可引用的模型供应商信息。
    /// </summary>
    [HttpGet("providers")]
    public async Task<BizResult<IReadOnlyList<AssistantModelServiceDto>>> Providers(CancellationToken ct)
    {
        var (actor, _, org) = Scope();
        await ReadPermission(actor, org);
        return BizResult.BusinessSuccess(await _service.ListProvidersAsync(false, ct));
    }
    /// <summary>
    /// 返回当前可用于聊天的模型供应商列表。
    /// </summary>
    [HttpGet("providers/available")]
    public async Task<BizResult<IReadOnlyList<AssistantModelServiceDto>>> AvailableProviders(CancellationToken ct)
    {
        var (actor, _, org) = Scope();
        await ReadPermission(actor, org);
        return BizResult.BusinessSuccess(await _service.ListAvailableChatProvidersAsync(ct));
    }
    /// <summary>
    /// 返回当前组织的业务模型策略。
    /// </summary>
    [HttpGet("policies")]
    public async Task<BizResult<IReadOnlyList<AssistantBusinessPolicyDto>>> Policies(CancellationToken ct)
    {
        var (actor, tenant, org) = Scope();
        await ReadPermission(actor, org);
        return BizResult.BusinessSuccess(await _service.ListPoliciesAsync(tenant, org, ct));
    }
    /// <summary>
    /// 校验组织写权限后创建业务策略。
    /// </summary>
    [HttpPost("policies")]
    public Task<BizResult<AssistantBusinessPolicyDto>> CreatePolicy([FromBody] SaveAssistantBusinessPolicyCommand command, CancellationToken ct) => SavePolicy(null, command, ct);
    /// <summary>
    /// 校验组织写权限后按预期修订更新业务策略。
    /// </summary>
    [HttpPut("policies/{id}")]
    public Task<BizResult<AssistantBusinessPolicyDto>> UpdatePolicy(string id, [FromBody] SaveAssistantBusinessPolicyCommand command, CancellationToken ct) => SavePolicy(id, command, ct);
    private async Task<BizResult<AssistantBusinessPolicyDto>> SavePolicy(string? id, SaveAssistantBusinessPolicyCommand command, CancellationToken ct)
    {
        var (actor, tenant, org) = Scope();
        await _permissions.RequireOrgPermissionAsync(actor, org, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(await _service.SavePolicyAsync(actor, tenant, org, id, command, ct));
    }
    /// <summary>
    /// 校验组织写权限后切换策略启用状态。
    /// </summary>
    [HttpPut("policies/{id}/status")]
    public async Task<BizResult<AssistantBusinessPolicyDto>> PolicyStatus(string id, [FromBody] AssistantPolicyStatusCommand command, CancellationToken ct)
    {
        var (actor, tenant, org) = Scope();
        await _permissions.RequireOrgPermissionAsync(actor, org, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(await _service.SetPolicyStatusAsync(actor, tenant, org, id, command, ct));
    }
    private (string Actor, string Tenant, string Org) Scope()
    {
        if (string.IsNullOrWhiteSpace(_tenant.TenantId) || string.IsNullOrWhiteSpace(_tenant.OrgId))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required");
        return (User.GetUserId(), _tenant.TenantId, _tenant.OrgId);
    }
    private async Task ReadPermission(string actor, string org)
    {
        if (!await _permissions.HasOrgPermissionAsync(actor, org, AdminPermissionConstants.OrgRead)
            && !await _permissions.HasOrgPermissionAsync(actor, org, AdminPermissionConstants.OrgSettingsWrite))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_read_permission_required");
    }

    private void NoStore()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
    }
}
