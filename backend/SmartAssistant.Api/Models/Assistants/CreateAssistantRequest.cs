#nullable enable
using System.Text.Json;

namespace SmartAssistant.Models.Assistants;

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
