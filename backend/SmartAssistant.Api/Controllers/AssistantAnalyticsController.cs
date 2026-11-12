#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Authentication;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Controllers;

/// <summary>
/// 提供管理端会话列表、消息明细和使用统计接口。
/// </summary>
[ApiController]

[Route("api/admin/assistants/{assistantId}/analytics")]
public sealed class AssistantAnalyticsController : ControllerBase
{
    /// <summary>
    /// 保存助手分析服务依赖，供助手分析控制器执行相关操作。
    /// </summary>
    private readonly IAssistantAnalyticsService _service;
    /// <summary>
    /// 接收并保存助手分析控制器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantAnalyticsController(IAssistantAnalyticsService service)
    {
        _service = service;
    }

    /// <summary>
    /// 分页返回指定助手的会话列表。
    /// </summary>
    [HttpGet("conversations")]
    public async Task<BizResult<AssistantConversationsPage>> ConversationsAsync(string assistantId, [FromQuery] int limit = 50,
        [FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(
            await _service.ListConversationsAsync(actor, assistantId, limit, page, cancellationToken));
    }

    /// <summary>
    /// 返回指定助手的使用统计。
    /// </summary>
    [HttpGet("summary")]
    public async Task<BizResult<AssistantAnalyticsSummaryDto>> SummaryAsync(string assistantId, CancellationToken cancellationToken = default)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _service.GetSummaryAsync(actor, assistantId, cancellationToken));
    }

    /// <summary>
    /// 返回指定助手会话的消息。
    /// </summary>
    [HttpGet("conversations/{conversationId}/messages")]
    public async Task<BizResult<AssistantMessagesPage>> MessagesAsync(string assistantId, string conversationId,
        [FromQuery] int limit = 50, [FromQuery] string? cursor = null, CancellationToken cancellationToken = default)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(
            await _service.GetMessagesAsync(actor, assistantId, conversationId, limit, cursor, cancellationToken));
    }
}
