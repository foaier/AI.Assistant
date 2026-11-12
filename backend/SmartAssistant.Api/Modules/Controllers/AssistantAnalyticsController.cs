#nullable enable
using System.Collections.Generic;
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
/// 提供管理端会话列表、消息明细和使用统计接口。
/// </summary>
[ApiController]

[Route("api/admin/assistants/{assistantId}/analytics")]
public sealed class AssistantAnalyticsController : ControllerBase
{
    private readonly IAssistantAnalyticsService _service;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminPermissionService _permissions;
    public AssistantAnalyticsController(IAssistantAnalyticsService service, ITenantContext tenantContext, IAdminPermissionService permissions)
    { _service = service; _tenantContext = tenantContext; _permissions = permissions; }

    /// <summary>
    /// 校验组织读取权限后分页返回助手会话列表。
    /// </summary>
    [HttpGet("conversations")]
    public async Task<BizResult<AssistantConversationsPage>> ConversationsAsync(string assistantId, [FromQuery] int limit = 50,
        [FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        var scope = RequireScope(); var actor = User.GetUserId(); await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.ListConversationsAsync(actor, scope.TenantId, scope.OrgId, assistantId, limit, page, cancellationToken));
    }

    /// <summary>
    /// 校验组织读取权限后返回助手使用统计。
    /// </summary>
    [HttpGet("summary")]
    public async Task<BizResult<AssistantAnalyticsSummaryDto>> SummaryAsync(string assistantId, CancellationToken cancellationToken = default)
    {
        var scope = RequireScope(); var actor = User.GetUserId(); await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.GetSummaryAsync(actor, scope.TenantId, scope.OrgId, assistantId, cancellationToken));
    }

    /// <summary>
    /// 校验组织读取权限后返回指定会话的消息。
    /// </summary>
    [HttpGet("conversations/{conversationId}/messages")]
    public async Task<BizResult<AssistantMessagesPage>> MessagesAsync(string assistantId, string conversationId,
        [FromQuery] int limit = 50, [FromQuery] string? cursor = null, CancellationToken cancellationToken = default)
    {
        var scope = RequireScope(); var actor = User.GetUserId(); await RequireReadAsync(actor, scope.OrgId);
        return BizResult.BusinessSuccess(await _service.GetMessagesAsync(actor, scope.TenantId, scope.OrgId,
            assistantId, conversationId, limit, cursor, cancellationToken));
    }

    private (string TenantId, string OrgId) RequireScope()
    { if (string.IsNullOrWhiteSpace(_tenantContext.TenantId) || string.IsNullOrWhiteSpace(_tenantContext.OrgId)) throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required"); return (_tenantContext.TenantId, _tenantContext.OrgId); }
    private async Task RequireReadAsync(string actor, string orgId)
    { if (!await _permissions.HasOrgPermissionAsync(actor, orgId, AdminPermissionConstants.OrgRead) && !await _permissions.HasOrgPermissionAsync(actor, orgId, AdminPermissionConstants.OrgSettingsWrite)) throw new BizResultException(BusinessCodes.Forbidden, "assistant_analytics_read_permission_required"); }
}
