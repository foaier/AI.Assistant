using System.Text.Json;
using AI.Assistant.Models.Assets;

namespace AI.Assistant.Platform.Assets;

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
    public async Task<AssetUploadResult> UploadAsync(IFormFile file, string? eventId, string category, string visibility = "public", CancellationToken cancellationToken = default)
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
    /// 为服务内部的文档处理读取资产内容，公开下载使用单独的可见性校验入口。
    /// </summary>
    public async Task<AssetContentResult> OpenInternalContentAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var metadata = await ReadMetadata(assetId, cancellationToken);
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
