#nullable enable
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Authentication;
using SmartAssistant.Modules.Admin.Public.Contracts;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Tenant;

using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Modules.Controllers;

/// <summary>
/// 提供基于草稿快照的预览会话与消息接口。
/// </summary>
[ApiController, Route("api/admin/assistants/{assistantId}/previews")]
public sealed class AssistantPreviewController(AssistantPreviewService service, ITenantContext tenant,
    IAdminPermissionService permissions) : ControllerBase
{
    /// <summary>
    /// 校验组织权限后按预期草稿修订创建预览。
    /// </summary>
    [HttpPost]
    public async Task<BizResult<AssistantPreviewCreated>> Create(string assistantId, CreateAssistantPreviewRequest request, CancellationToken ct)
    {
        var scope = await RequireAsync();
        return BizResult.BusinessSuccess(await service.CreateAsync(scope.Actor, scope.Tenant, scope.Org,
            assistantId, request.ExpectedDraftRevision, ct));
    }
    /// <summary>
    /// 在组织授权范围内向预览提交用户消息。
    /// </summary>
    [HttpPost("{previewId}/turns")]
    public async Task<BizResult<AssistantTurnAccepted>> Submit(string assistantId, string previewId, SubmitAssistantPreviewRequest request, CancellationToken ct)
    {
        var scope = await RequireAsync();
        return BizResult.BusinessSuccess(await service.SubmitAsync(scope.Actor, scope.Tenant, scope.Org,
            assistantId, previewId, request, ct));
    }
    /// <summary>
    /// 读取指定预览的消息与运行状态。
    /// </summary>
    [HttpGet("{previewId}/messages")]
    public async Task<BizResult<AssistantPreviewMessages>> Messages(string assistantId, string previewId, CancellationToken ct)
    {
        var scope = await RequireAsync();
        return BizResult.BusinessSuccess(await service.MessagesAsync(scope.Actor, scope.Tenant, scope.Org,
            assistantId, previewId, ct));
    }
    private async Task<(string Actor, string Tenant, string Org)> RequireAsync()
    {
        if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.OrgId))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required");
        var actor = User.GetUserId();
        await permissions.RequireOrgPermissionAsync(actor, tenant.OrgId, AdminPermissionConstants.OrgSettingsWrite);
        return (actor, tenant.TenantId, tenant.OrgId);
    }
}
