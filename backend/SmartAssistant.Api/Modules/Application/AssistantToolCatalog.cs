#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 维护运行时允许使用的工具目录。
/// 发布快照只能冻结目录内的工具，草稿或模型输出不能引入未注册的工具。
/// </summary>
public static class AssistantToolCatalog
{
    /// <summary>
    /// 定义助手工具目录使用的事件概览结构配置值。
    /// </summary>
    private const string EventOverviewSchema = """
        { "type":"object", "properties": {
          "eventId": { "type":"string", "minLength":1 },
          "locale": { "type":"string", "enum":["zh-CN","en-US"] }
        }, "required":["eventId"], "additionalProperties":false }
        """;

    /// <summary>
    /// 定义助手工具目录使用的事件日程结构配置值。
    /// </summary>
    private const string EventScheduleSchema = """
        { "type":"object", "properties": {
          "eventId": { "type":"string", "minLength":1 },
          "date": { "type":"string", "description":"Optional ISO date (yyyy-MM-dd)." },
          "keyword": { "type":"string" },
          "limit": { "type":"integer", "minimum":1, "maximum":50 }
        }, "required":["eventId"], "additionalProperties":false }
        """;

    /// <summary>
    /// 定义助手工具目录使用的网络检索结构配置值。
    /// </summary>
    private const string WebSearchSchema = """
        { "type":"object", "properties": {
          "query": { "type":"string", "minLength":1, "maxLength":500,
            "description":"A concise public web search query derived from the user's question." }
        }, "required":["query"], "additionalProperties":false }
        """;

    /// <summary>
    /// 定义助手工具目录使用的定义配置值。
    /// </summary>
    private static readonly IReadOnlyDictionary<(string Id, string Version), (string Name, string Description, string Schema)> Definitions =
        new Dictionary<(string, string), (string, string, string)>
        {
            /*
             * 原活动和日程工具需要额外业务集成。
             * 独立应用不将其加入允许目录，在发布阶段即拒绝不可用的绑定。
             */
            [("assistant.web_search", "v1")] = (
                "web_search",
                "Search current public web information for the user's question. Use only when fresh external facts are needed. 使用火山联网问答 Agent 搜索公开网页。",
                WebSearchSchema)
        };

    /// <summary>
    /// 按工具标识和契约版本查找运行时允许使用的工具定义。
    /// </summary>
    public static AssistantToolDefinition? Resolve(string toolId, string contractVersion)
    {
        if (!Definitions.TryGetValue((toolId, contractVersion), out var value))
            return null;
        using var document = JsonDocument.Parse(value.Schema);
        return new AssistantToolDefinition(toolId, contractVersion, value.Name, value.Description, document.RootElement.Clone());
    }

    /// <summary>
    /// 返回当前工具目录中的工具定义。
    /// </summary>
    public static IReadOnlyList<AssistantToolDefinition> List()
    {
        var result = new List<AssistantToolDefinition>(Definitions.Count);
        foreach (var entry in Definitions)
        {
            var definition = Resolve(entry.Key.Id, entry.Key.Version);
            if (definition is not null)
                result.Add(definition);
        }
        return result;
    }
}

/// <summary>
/// 按冻结的工具契约解析定义和处理器，将模型工具请求限制在已接入的实现内。
/// </summary>
public sealed class AssistantToolRegistry : IAssistantToolRegistry
{
    /// <summary>
    /// 保存服务模型供应商依赖，供助手工具注册表执行相关操作。
    /// </summary>
    private readonly IServiceProvider _services;
    /// <summary>
    /// 接收并保存助手工具注册表执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantToolRegistry(IServiceProvider services) => _services = services;

    /// <summary>
    /// 根据工具标识与契约版本读取允许使用的工具定义。
    /// </summary>
    public Task<AssistantToolDefinition?> ResolveAsync(
        string toolId,
        string contractVersion,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AssistantToolCatalog.Resolve(toolId, contractVersion));
    }

    /// <summary>
    /// 在运行范围内解析指定工具的处理器，拒绝未注册的工具。
    /// </summary>
    public Task<IAssistantToolHandler?> ResolveHandlerAsync(
        string toolId,
        string contractVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handler = _services.GetServices<IAssistantToolHandler>()
            .FirstOrDefault(value => string.Equals(value.ToolId, toolId, StringComparison.Ordinal)
                && string.Equals(value.ContractVersion, contractVersion, StringComparison.Ordinal));
        return Task.FromResult(handler);
    }
}
