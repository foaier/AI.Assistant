#nullable enable
using Microsoft.AspNetCore.Mvc;
using SmartAssistant.Models.Common;
using SmartAssistant.Models.Preview;
using SmartAssistant.Models.Runtime;
using SmartAssistant.Services.Application;

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
        return BizResult.BusinessSuccess(await service.CreateAsync(assistantId, request.ExpectedDraftRevision, ct));
    }
    /// <summary>
    /// 向指定助手的预览会话提交用户消息。
    /// </summary>
    [HttpPost("{previewId}/turns")]
    public async Task<BizResult<AssistantTurnAccepted>> Submit(string assistantId, string previewId, SubmitAssistantPreviewRequest request, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await service.SubmitAsync(assistantId, previewId, request, ct));
    }
    /// <summary>
    /// 读取指定预览的消息与运行状态。
    /// </summary>
    [HttpGet("{previewId}/messages")]
    public async Task<BizResult<AssistantPreviewMessages>> Messages(string assistantId, string previewId, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await service.MessagesAsync(assistantId, previewId, ct));
    }
}
