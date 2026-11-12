#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Authentication;
using SmartAssistant.Modules.Admin.Public.Contracts;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Contracts;

using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Modules.Controllers;

/// <summary>
/// 提供系统级模型供应商和联网搜索配置接口。
/// </summary>
[ApiController, Route("api/admin/system/assistant-models")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class SystemAssistantModelsController : ControllerBase
{
    private readonly IAssistantPolicyService _service;
    private readonly AssistantWebSearchConfigurationService _webSearch;
    private readonly IAdminPermissionService _permissions;

    public SystemAssistantModelsController(IAssistantPolicyService service,
        AssistantWebSearchConfigurationService webSearch, IAdminPermissionService permissions)
        => (_service, _webSearch, _permissions) = (service, webSearch, permissions);

    private async Task<string> RequireSuperAdminAsync()
    {
        var actor = User.GetUserId();
        await _permissions.RequireSuperAdminAsync(actor);
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
        return actor;
    }

    /// <summary>
    /// 校验系统管理权限后列出模型供应商。
    /// </summary>
    [HttpGet("providers")]
    public async Task<BizResult<IReadOnlyList<AssistantModelServiceDto>>> Providers(CancellationToken ct)
    {
        await RequireSuperAdminAsync();
        return BizResult.BusinessSuccess(await _service.ListProvidersAsync(true, ct));
    }

    /// <summary>
    /// 校验系统管理权限后返回供应商编辑视图。
    /// </summary>
    [HttpGet("providers/{id}/edit")]
    public async Task<BizResult<AssistantModelServiceDto>> EditProvider(string id, CancellationToken ct)
    {
        await RequireSuperAdminAsync();
        return BizResult.BusinessSuccess(await _service.GetProviderForEditAsync(id, ct));
    }

    /// <summary>
    /// 校验系统管理权限后创建模型供应商配置。
    /// </summary>
    [HttpPost("providers")]
    public Task<BizResult<AssistantModelServiceDto>> CreateProvider([FromBody] SaveAssistantModelServiceCommand command, CancellationToken ct) => SaveProvider(null, command, ct);

    /// <summary>
    /// 校验系统管理权限后更新模型供应商配置。
    /// </summary>
    [HttpPut("providers/{id}")]
    public Task<BizResult<AssistantModelServiceDto>> UpdateProvider(string id, [FromBody] SaveAssistantModelServiceCommand command, CancellationToken ct) => SaveProvider(id, command, ct);

    private async Task<BizResult<AssistantModelServiceDto>> SaveProvider(string? id, SaveAssistantModelServiceCommand command, CancellationToken ct)
    {
        var actor = await RequireSuperAdminAsync();
        return BizResult.BusinessSuccess(await _service.SaveProviderAsync(actor, id, command, ct));
    }

    /// <summary>
    /// 校验系统管理权限后切换供应商启用状态。
    /// </summary>
    [HttpPut("providers/{id}/status")]
    public async Task<BizResult<AssistantModelServiceDto>> ProviderStatus(string id, [FromBody] AssistantPolicyStatusCommand command, CancellationToken ct)
    {
        var actor = await RequireSuperAdminAsync();
        return BizResult.BusinessSuccess(await _service.SetProviderStatusAsync(actor, id, command, ct));
    }

    /// <summary>
    /// 校验系统管理权限后检查模型供应商连接。
    /// </summary>
    [HttpPost("providers/{id}/check")]
    public async Task<BizResult<AssistantConnectionCheck>> Check(string id, CancellationToken ct)
    {
        await RequireSuperAdminAsync();
        return BizResult.BusinessSuccess(await _service.CheckProviderAsync(id, ct));
    }

    /// <summary>
    /// 校验系统管理权限后读取联网搜索管理配置。
    /// </summary>
    [HttpGet("web-search")]
    public async Task<BizResult<AssistantWebSearchConfigDto>> WebSearchConfig(CancellationToken ct)
    {
        await RequireSuperAdminAsync();
        return BizResult.BusinessSuccess(await _webSearch.GetForAdminAsync(ct));
    }

    /// <summary>
    /// 校验系统管理权限后保存联网搜索设置。
    /// </summary>
    [HttpPost("web-search"), HttpPut("web-search")]
    public async Task<BizResult<AssistantWebSearchConfigDto>> SaveWebSearchConfig([FromBody] SaveAssistantWebSearchConfigCommand command, CancellationToken ct)
    {
        var actor = await RequireSuperAdminAsync();
        return BizResult.BusinessSuccess(await _webSearch.SaveAsync(actor, command, ct));
    }

    /// <summary>
    /// 校验系统管理权限后切换联网搜索启用状态。
    /// </summary>
    [HttpPut("web-search/status")]
    public async Task<BizResult<AssistantWebSearchConfigDto>> WebSearchStatus([FromBody] AssistantPolicyStatusCommand command, CancellationToken ct)
    {
        var actor = await RequireSuperAdminAsync();
        return BizResult.BusinessSuccess(await _webSearch.SetStatusAsync(actor, command, ct));
    }
}
