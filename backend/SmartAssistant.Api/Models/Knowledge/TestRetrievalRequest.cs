#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 验证知识检索效果的问题请求体。
/// </summary>
public sealed class TestRetrievalRequest
{
    /// <summary>
    /// 保存请求中的问题字段。
    /// </summary>
    public string Question { get; set; } = string.Empty;
}
