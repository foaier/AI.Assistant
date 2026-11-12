#nullable enable
namespace SmartAssistant.Models.Assets;

/// <summary>
/// 保存在本地的文件元数据，记录可见性和上传结果。
/// </summary>
internal sealed record AssetMetadata(
    string Visibility,
    AssetUploadResult Asset);
