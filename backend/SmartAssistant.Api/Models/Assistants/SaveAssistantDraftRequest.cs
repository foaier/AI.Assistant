#nullable enable
using System.Text.Json;

namespace SmartAssistant.Models.Assistants;

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
