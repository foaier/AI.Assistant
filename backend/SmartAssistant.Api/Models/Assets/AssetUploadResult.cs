#nullable enable
namespace SmartAssistant.Models.Assets;

/// <summary>
/// 文件上传后的资产标识、访问地址、文件类型和大小等元数据。
/// </summary>
public sealed record AssetUploadResult(
    string AssetId,
    string Url,
    string PublicUrl,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Category,
    string? EventId);
