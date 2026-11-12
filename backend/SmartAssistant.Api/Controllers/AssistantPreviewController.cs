#nullable enable
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Authentication;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Controllers;

/// <summary>
/// 提供基于草稿快照的预览会话与消息接口。
/// </summary>
[ApiController, Route("api/admin/assistants/{assistantId}/previews")]
public sealed class AssistantPreviewController(AssistantPreviewService service) : ControllerBase
{
    /// <summary>
    /// 按预期草稿修订创建预览。
    /// </summary>
    [HttpPost]
    public async Task<BizResult<AssistantPreviewCreated>> Create(string assistantId, CreateAssistantPreviewRequest request, CancellationToken ct)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await service.CreateAsync(actor, assistantId, request.ExpectedDraftRevision, ct));
    }
    /// <summary>
    /// 向指定助手的预览会话提交用户消息。
    /// </summary>
    [HttpPost("{previewId}/turns")]
    public async Task<BizResult<AssistantTurnAccepted>> Submit(string assistantId, string previewId, SubmitAssistantPreviewRequest request, CancellationToken ct)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await service.SubmitAsync(actor, assistantId, previewId, request, ct));
    }
    /// <summary>
    /// 读取指定预览的消息与运行状态。
    /// </summary>
    [HttpGet("{previewId}/messages")]
    public async Task<BizResult<AssistantPreviewMessages>> Messages(string assistantId, string previewId, CancellationToken ct)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await service.MessagesAsync(actor, assistantId, previewId, ct));
    }
}
