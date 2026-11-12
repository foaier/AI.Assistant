#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 验证固定问答匹配的问题请求体。
/// </summary>
public sealed class TestQnaRequest
{
    /// <summary>
    /// 保存请求中的问题字段。
    /// </summary>
    public string Question { get; set; } = string.Empty;
}
