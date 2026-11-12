using System.Text.Json;
using SmartAssistant.Platform;

namespace SmartAssistant.Platform.Assets;

/// <summary>
/// 文件访问授权上下文，包含身份、操作人及关联活动信息。
/// </summary>
public sealed record AssetAclContext(bool Authenticated, string ActorUserId, string? EventId);
/// <summary>
/// 定义文件资产的可见性标识，供上传与下载授权判断使用。
/// </summary>
public static class AssetCategoryRegistry
{
    /// <summary>
    /// 定义资源注册表使用的可见性私有配置值。
    /// </summary>
    public const string VisibilityPrivate = "private";
}
/// <summary>
/// 文件上传后的资产标识、访问地址、文件类型和大小等元数据。
/// </summary>
public sealed record AssetUploadResult(string AssetId, string Url, string PublicUrl, string FileName,
    string ContentType, long SizeBytes, string Category, string? EventId);
/// <summary>
/// 文件下载结果，包含内容流及响应所需的媒体类型。
/// </summary>
public sealed record AssetContentResult(Stream Content, string ContentType);
/// <summary>
/// 保存在本地的文件元数据，记录可见性和上传结果。
/// </summary>
internal sealed record AssetMetadata(string Visibility, AssetUploadResult Asset);

/// <summary>
/// 管理独立部署的本地文件存储与访问控制。
/// 知识文档按私有资产保存，公开下载接口只允许公开可见的资产。
/// </summary>
public sealed class AssetApplicationService(IConfiguration configuration)
{
    /// <summary>
    /// 保存资源服务在处理目录时所需的数据或状态。
    /// </summary>
    private readonly string _directory = Path.Combine(configuration["Assistant:DataDirectory"]!, "assets");
    /// <summary>
    /// 校验文件大小、类型和可见性后保存内容与元数据，返回资产访问描述。
    /// </summary>
    public async Task<AssetUploadResult> UploadAsync(IFormFile file, string? eventId, string category, string actorUserId, string visibility = "public", CancellationToken cancellationToken = default)
    {
        if (file.Length is <= 0 or > 25 * 1024 * 1024)
            throw new BizResultException(1001, "asset_size_invalid");
        if (visibility != "private" && file.ContentType is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp"))
            throw new BizResultException(1001, "asset_image_type_required");
        Directory.CreateDirectory(_directory);
        var id = Guid.NewGuid().ToString("N");
        var url = $"/api/assets/{id}/content";
        var result = new AssetUploadResult(id, url, url, Path.GetFileName(file.FileName), file.ContentType, file.Length, category, eventId);
        await using (var stream = File.Create(Path.Combine(_directory, id + ".bin")))
            await file.CopyToAsync(stream, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_directory, id + ".json"),
            JsonSerializer.Serialize(new AssetMetadata(visibility, result)), cancellationToken);
        return result;
    }
    /// <summary>
    /// 按访问上下文校验资产权限，再返回文件内容流。
    /// </summary>
    public async Task<AssetContentResult> GetContentAsync(string assetId, AssetAclContext acl, CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadata(assetId, cancellationToken);
        if (metadata.Visibility == "private" && !acl.Authenticated)
            throw new BizResultException(1003, "asset_access_denied");
        return new(File.OpenRead(Path.Combine(_directory, assetId + ".bin")), metadata.Asset.ContentType);
    }
    /// <summary>
    /// 读取公开可见资产；私有资产不能通过此入口下载。
    /// </summary>
    public async Task<AssetContentResult> GetPublicContentAsync(string id, CancellationToken cancellationToken)
    {
        var metadata = await ReadMetadata(id, cancellationToken);
        if (metadata.Visibility == "private") throw new BizResultException(1003, "asset_private");
        return new(File.OpenRead(Path.Combine(_directory, id + ".bin")), metadata.Asset.ContentType);
    }
    /// <summary>
    /// 读取并校验资产元数据文件；无效标识或缺失文件按未找到处理。
    /// </summary>
    private async Task<AssetMetadata> ReadMetadata(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _) || !File.Exists(Path.Combine(_directory, id + ".json")))
            throw new BizResultException(1002, "asset_not_found");
        return JsonSerializer.Deserialize<AssetMetadata>(await File.ReadAllTextAsync(Path.Combine(_directory, id + ".json"), ct))!;
    }
}
