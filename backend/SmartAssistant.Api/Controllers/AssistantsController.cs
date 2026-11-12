#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Authentication;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

using Microsoft.AspNetCore.Mvc;

namespace SmartAssistant.Controllers;

/// <summary>
/// 提供助手草稿管理、发布和撤回接口。
/// </summary>
[ApiController]

[Route("api/admin/assistants")]
public sealed class AssistantsController : ControllerBase
{
    /// <summary>
    /// 保存助手草稿服务依赖，供助手控制器执行相关操作。
    /// </summary>
    private readonly IAssistantDraftService _service;
    /// <summary>
    /// 保存助手发布服务依赖，供助手控制器执行相关操作。
    /// </summary>
    private readonly IAssistantPublicationService _publicationService;

    /// <summary>
    /// 接收并保存助手控制器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantsController(
        IAssistantDraftService service,
        IAssistantPublicationService publicationService)
    {
        _service = service;
        _publicationService = publicationService;
    }

    /// <summary>
    /// 读取助手列表。
    /// </summary>
    [HttpGet]
    public async Task<BizResult<IReadOnlyList<AssistantListItemDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(
            await _service.ListAsync(actor, cancellationToken));
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
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(
            await _service.CreateAssistantAsync(
                actor,
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
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(
            await _service.GetAssistantAsync(actor, assistantId, cancellationToken));
    }

    /// <summary>
    /// 保存助手草稿，并返回最新修订。
    /// </summary>
    [HttpPut("{assistantId}/draft")]
    public async Task<BizResult<AssistantDto>> SaveDraftAsync(string assistantId,
        [FromBody] SaveAssistantDraftRequest request,
        CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(
            await _service.SaveAssistantDraftAsync(
                actor,
                assistantId,
                new SaveAssistantDraftCommand(
                    request.Name,
                    request.Description,
                    ConfigurationJson(request.Configuration),
                    request.ExpectedDraftRevision),
                cancellationToken));
    }

    /// <summary>
    /// 请求删除助手及其关联数据。
    /// </summary>
    [HttpDelete("{assistantId}")]
    public async Task<BizResult<object>> DeleteAsync(string assistantId, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        await _service.DeleteAssistantAsync(actor, assistantId, cancellationToken);
        return BizResult.BusinessSuccess<object>(new { deleted = true });
    }

    /// <summary>
    /// 查询助手当前的发布状态。
    /// </summary>
    [HttpGet("{assistantId}/publication")]
    public async Task<BizResult<AssistantPublicationState>> PublicationAsync(string assistantId, CancellationToken cancellationToken)
    {
        return BizResult.BusinessSuccess(await _publicationService.GetStateAsync(assistantId, cancellationToken));
    }

    /// <summary>
    /// 按请求中的草稿和指针修订执行发布。
    /// </summary>
    [HttpPost("{assistantId}/publish")]
    public async Task<BizResult<AssistantPublicationResult>> PublishAsync(string assistantId, [FromBody] PublishAssistantRequest request,
        CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _publicationService.PublishAsync(
            actor,
            assistantId,
            new PublishAssistantCommand(request.ExpectedDraftRevision, request.ExpectedPointerRevision),
            cancellationToken));
    }

    /// <summary>
    /// 按预期指针修订撤回发布。
    /// </summary>
    [HttpPost("{assistantId}/revoke")]
    public async Task<BizResult<AssistantPublicationResult>> RevokeAsync(string assistantId, [FromBody] RevokeAssistantRequest request, CancellationToken cancellationToken)
    {
        var actor = User.GetUserId();
        return BizResult.BusinessSuccess(await _publicationService.RevokeAsync(
            actor,
            assistantId,
            new RevokeAssistantPublicationCommand(request.ExpectedPointerRevision),
            cancellationToken));
    }

    /// <summary>
    /// 从 JSON 配置中读取并规范化序列化内容。
    /// </summary>
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
    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 记录此资源的可选说明。
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// 保存请求中的配置字段。
    /// </summary>
    public JsonElement Configuration { get; set; }
}

/// <summary>
/// 保存草稿的 HTTP 请求体，携带当前预期修订号。
/// </summary>
public sealed class SaveAssistantDraftRequest
{
    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 记录此资源的可选说明。
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// 保存请求中的配置字段。
    /// </summary>
    public JsonElement Configuration { get; set; }
    /// <summary>
    /// 记录调用方期望的草稿修订，用于拒绝并发覆盖。
    /// </summary>
    public long ExpectedDraftRevision { get; set; }
}

/// <summary>
/// 发布助手时提交的草稿与发布指针修订号。
/// </summary>
public sealed class PublishAssistantRequest
{
    /// <summary>
    /// 记录调用方期望的草稿修订，用于拒绝并发覆盖。
    /// </summary>
    public long ExpectedDraftRevision { get; set; }
    /// <summary>
    /// 记录调用方期望的发布指针修订，用于拒绝并发发布冲突。
    /// </summary>
    public long? ExpectedPointerRevision { get; set; }
}

/// <summary>
/// 撤回助手时提交的预期发布指针修订号。
/// </summary>
public sealed class RevokeAssistantRequest
{
    /// <summary>
    /// 记录调用方期望的发布指针修订，用于拒绝并发发布冲突。
    /// </summary>
    public long ExpectedPointerRevision { get; set; }
}
