#nullable enable
namespace SmartAssistant.Models.Assets;

/// <summary>
/// 文件下载结果，包含内容流及响应所需的媒体类型。
/// </summary>
public sealed record AssetContentResult(
    Stream Content,
    string ContentType);
