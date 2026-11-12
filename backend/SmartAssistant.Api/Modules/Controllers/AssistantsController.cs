#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Authentication;
using SmartAssistant.Modules.Admin.Public.Contracts;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Tenant;

using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Modules.Controllers;

/// <summary>
/// 提供助手草稿管理、发布和撤回接口，并校验组织权限。
/// </summary>
[ApiController]

[Route("api/admin/assistants")]
public sealed class AssistantsController : ControllerBase
{
    private readonly IAssistantDraftService _service;
    private readonly IAssistantPublicationService _publicationService;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminPermissionService _permissions;

    public AssistantsController(
        IAssistantDraftService service,
        IAssistantPublicationService publicationService,
        ITenantContext tenantContext,
        IAdminPermissionService permissions)
    {
        _service = service;
        _publicationService = publicationService;
        _tenantContext = tenantContext;
        _permissions = permissions;
    }

    /// <summary>
    /// 读取当前租户和组织范围内的助手列表。
    /// </summary>
    [HttpGet]
    public async Task<BizResult<IReadOnlyList<AssistantListItemDto>>> ListAsync(
        CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadPermissionAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(
            await _service.ListAsync(actor, scope.TenantId, scope.OrgId, cancellationToken));
    }

    /// <summary>
    /// 校验配置与幂等键后创建助手草稿，重复的相同请求复用已有结果。
    /// </summary>
    [HttpPost]
    public async Task<BizResult<AssistantDto>> CreateAsync(
        [FromBody] CreateAssistantRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(
            await _service.CreateAssistantAsync(
                actor,
                scope.TenantId,
                scope.OrgId,
                new CreateAssistantDraftCommand(
                    request.Name,
                    request.Description,
                    ConfigurationJson(request.Configuration),
                    idempotencyKey),
                cancellationToken));
    }

    /// <summary>
    /// 读取指定助手草稿；资源不存在时返回相应业务错误。
    /// </summary>
    [HttpGet("{assistantId}")]
    public async Task<BizResult<AssistantDto>> GetAsync(
        string assistantId,
        CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await RequireReadPermissionAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(
            await _service.GetAssistantAsync(actor, scope.TenantId, scope.OrgId, assistantId, cancellationToken));
    }

    /// <summary>
    /// 校验组织写权限后保存助手草稿，并返回最新修订。
    /// </summary>
    [HttpPut("{assistantId}/draft")]
    public async Task<BizResult<AssistantDto>> SaveDraftAsync(
        string assistantId,
        [FromBody] SaveAssistantDraftRequest request,
        CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(
            await _service.SaveAssistantDraftAsync(
                actor,
                scope.TenantId,
                scope.OrgId,
                assistantId,
                new SaveAssistantDraftCommand(
                    request.Name,
                    request.Description,
                    ConfigurationJson(request.Configuration),
                    request.ExpectedDraftRevision),
                cancellationToken));
    }

    /// <summary>
    /// 校验组织写权限后请求删除助手。
    /// </summary>
    [HttpDelete("{assistantId}")]
    public async Task<BizResult<object>> DeleteAsync(
        string assistantId,
        CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        await _service.DeleteAssistantAsync(actor, scope.TenantId, scope.OrgId, assistantId, cancellationToken);
        return BizResult.BusinessSuccess<object>(new { deleted = true });
    }

    /// <summary>
    /// 校验组织读取权限后查询当前发布状态。
    /// </summary>
    [HttpGet("{assistantId}/publication")]
    public async Task<BizResult<AssistantPublicationState>> PublicationAsync(string assistantId, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        await RequireReadPermissionAsync(User.GetUserId(), scope.OrgId);
        return BizResult.BusinessSuccess(await _publicationService.GetStateAsync(scope.TenantId, scope.OrgId, assistantId, cancellationToken));
    }

    /// <summary>
    /// 校验组织写权限并按请求中的草稿和指针修订执行发布。
    /// </summary>
    [HttpPost("{assistantId}/publish")]
    public async Task<BizResult<AssistantPublicationResult>> PublishAsync(
        string assistantId,
        [FromBody] PublishAssistantRequest request,
        CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(await _publicationService.PublishAsync(
            actor,
            scope.TenantId,
            scope.OrgId,
            assistantId,
            new PublishAssistantCommand(request.ExpectedDraftRevision, request.ExpectedPointerRevision),
            cancellationToken));
    }

    /// <summary>
    /// 校验组织写权限并按预期指针修订撤回发布。
    /// </summary>
    [HttpPost("{assistantId}/revoke")]
    public async Task<BizResult<AssistantPublicationResult>> RevokeAsync(
        string assistantId,
        [FromBody] RevokeAssistantRequest request,
        CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        var actor = User.GetUserId();
        await _permissions.RequireOrgPermissionAsync(actor, scope.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return BizResult.BusinessSuccess(await _publicationService.RevokeAsync(
            actor,
            scope.TenantId,
            scope.OrgId,
            assistantId,
            new RevokeAssistantPublicationCommand(request.ExpectedPointerRevision),
            cancellationToken));
    }

    private (string TenantId, string OrgId) RequireScope()
    {
        var tenantId = _tenantContext.TenantId;
        var orgId = _tenantContext.OrgId;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(orgId))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required");
        return (tenantId, orgId);
    }

    private async Task RequireReadPermissionAsync(string actor, string orgId)
    {
        var canRead = await _permissions.HasOrgPermissionAsync(
            actor, orgId, AdminPermissionConstants.OrgRead);
        var canConfigure = await _permissions.HasOrgPermissionAsync(
            actor, orgId, AdminPermissionConstants.OrgSettingsWrite);
        if (!canRead && !canConfigure)
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_read_permission_required");
    }

    private static string ConfigurationJson(JsonElement configuration)
        => configuration.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "{}"
            : configuration.GetRawText();
}

/// <summary>
/// 创建助手的 HTTP 请求体，包含展示信息、配置和幂等键。
/// </summary>
public sealed class CreateAssistantRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonElement Configuration { get; set; }
}

/// <summary>
/// 保存草稿的 HTTP 请求体，携带当前预期修订号。
/// </summary>
public sealed class SaveAssistantDraftRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonElement Configuration { get; set; }
    public long ExpectedDraftRevision { get; set; }
}

/// <summary>
/// 发布助手时提交的草稿与发布指针修订号。
/// </summary>
public sealed class PublishAssistantRequest
{
    public long ExpectedDraftRevision { get; set; }
    public long? ExpectedPointerRevision { get; set; }
}

/// <summary>
/// 撤回助手时提交的预期发布指针修订号。
/// </summary>
public sealed class RevokeAssistantRequest
{
    public long ExpectedPointerRevision { get; set; }
}
