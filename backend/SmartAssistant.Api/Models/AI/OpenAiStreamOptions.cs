#nullable enable
using System.Text.Json.Serialization;

namespace SmartAssistant.Models.AI;

/// <summary>
/// 封装打开流式输出选项所需字段，供模型供应商协议序列化和反序列化使用。
/// </summary>
internal sealed class OpenAiStreamOptions
{
    /// <summary>
    /// 控制模型流响应是否包含令牌用量信息。
    /// </summary>
    [JsonPropertyName("include_usage")]
    public bool IncludeUsage { get; init; }
}
