#nullable enable
using System.Text.Json.Serialization;

namespace AI.Assistant.Models.AI;

/// <summary>
/// 封装打开工具调用相关的内部状态或数据，供所属组件完成相应处理。
/// </summary>
internal sealed class OpenAiToolCall
{
    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 表示打开工具调用的类型信息。
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    /// <summary>
    /// 保存供应商协议中的函数定义及其参数约束。
    /// </summary>
    [JsonPropertyName("function")]
    public OpenAiToolCallFunction Function { get; init; } = new();
}
