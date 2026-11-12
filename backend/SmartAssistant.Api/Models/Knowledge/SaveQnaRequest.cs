#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 保存固定问答内容和展示设置的 HTTP 请求体。
/// </summary>
public sealed class SaveQnaRequest
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// 保存请求中的问题字段。
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// 保存请求中的回答字段。
    /// </summary>
    public string? Answer { get; set; }

    /// <summary>
    /// 标记对应能力或配置是否启用。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 记录问答项在列表中的显示顺序。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 标记该问答是否可以作为建议问题展示。
    /// </summary>
    public bool ShowAsSuggestion { get; set; }
}
