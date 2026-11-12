#nullable enable
using Microsoft.AspNetCore.Mvc;
using AI.Assistant.Models.Common;
using AI.Assistant.Models.Policies;
using AI.Assistant.Services.Application;
using AI.Assistant.Services.Contracts;

namespace AI.Assistant.Controllers;

/// <summary>
/// 提供系统级模型供应商和联网搜索配置接口。
/// </summary>
[ApiController, Route("api/admin/system/assistant-models")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class SystemAssistantModelsController : ControllerBase
{
    /// <summary>
    /// 保存助手策略服务依赖，供助手模型控制器执行相关操作。
    /// </summary>
    private readonly IAssistantPolicyService _service;
    /// <summary>
    /// 保存助手网络检索配置服务依赖，供助手模型控制器执行相关操作。
    /// </summary>
    private readonly AssistantWebSearchConfigurationService _webSearch;
    /// <summary>
    /// 接收并保存助手模型控制器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public SystemAssistantModelsController(IAssistantPolicyService service,
        AssistantWebSearchConfigurationService webSearch)
        => (_service, _webSearch) = (service, webSearch);

    /// <summary>
    /// 列出模型供应商。
    /// </summary>
    [HttpGet("providers")]
    public async Task<BizResult<IReadOnlyList<AssistantModelServiceDto>>> Providers(CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.ListProvidersAsync(true, ct));
    }

    /// <summary>
    /// 返回供应商编辑视图。
    /// </summary>
    [HttpGet("providers/{id}/edit")]
    public async Task<BizResult<AssistantModelServiceDto>> EditProvider(string id, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.GetProviderForEditAsync(id, ct));
    }

    /// <summary>
    /// 创建模型供应商配置。
    /// </summary>
    [HttpPost("providers")]
    public Task<BizResult<AssistantModelServiceDto>> CreateProvider([FromBody] SaveAssistantModelServiceCommand command, CancellationToken ct) => SaveProvider(null, command, ct);

    /// <summary>
    /// 更新模型供应商配置。
    /// </summary>
    [HttpPut("providers/{id}")]
    public Task<BizResult<AssistantModelServiceDto>> UpdateProvider(string id, [FromBody] SaveAssistantModelServiceCommand command, CancellationToken ct) => SaveProvider(id, command, ct);

    /// <summary>
    /// 校验并保存模型供应商配置及其修订信息。
    /// </summary>
    private async Task<BizResult<AssistantModelServiceDto>> SaveProvider(string? id, SaveAssistantModelServiceCommand command, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.SaveProviderAsync(id, command, ct));
    }

    /// <summary>
    /// 切换供应商启用状态。
    /// </summary>
    [HttpPut("providers/{id}/status")]
    public async Task<BizResult<AssistantModelServiceDto>> ProviderStatus(string id, [FromBody] AssistantPolicyStatusCommand command, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.SetProviderStatusAsync(id, command, ct));
    }

    /// <summary>
    /// 检查模型供应商连接。
    /// </summary>
    [HttpPost("providers/{id}/check")]
    public async Task<BizResult<AssistantConnectionCheck>> Check(string id, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _service.CheckProviderAsync(id, ct));
    }

    /// <summary>
    /// 读取联网搜索管理配置。
    /// </summary>
    [HttpGet("web-search")]
    public async Task<BizResult<AssistantWebSearchConfigDto>> WebSearchConfig(CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _webSearch.GetForAdminAsync(ct));
    }

    /// <summary>
    /// 保存联网搜索设置。
    /// </summary>
    [HttpPost("web-search"), HttpPut("web-search")]
    public async Task<BizResult<AssistantWebSearchConfigDto>> SaveWebSearchConfig([FromBody] SaveAssistantWebSearchConfigCommand command, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _webSearch.SaveAsync(command, ct));
    }

    /// <summary>
    /// 切换联网搜索启用状态。
    /// </summary>
    [HttpPut("web-search/status")]
    public async Task<BizResult<AssistantWebSearchConfigDto>> WebSearchStatus([FromBody] AssistantPolicyStatusCommand command, CancellationToken ct)
    {
        return BizResult.BusinessSuccess(await _webSearch.SetStatusAsync(command, ct));
    }
}
