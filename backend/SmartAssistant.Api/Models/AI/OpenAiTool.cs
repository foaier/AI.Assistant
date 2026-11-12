#nullable enable
using System.Text.Json.Serialization;

namespace SmartAssistant.Models.AI;

/// <summary>
/// 封装打开工具所需字段，供模型供应商协议序列化和反序列化使用。
/// </summary>
internal sealed class OpenAiTool
{
    /// <summary>
    /// 表示打开工具的类型信息。
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    /// <summary>
    /// 保存供应商协议中的函数定义及其参数约束。
    /// </summary>
    [JsonPropertyName("function")]
    public OpenAiFunction Function { get; init; } = new();
}
