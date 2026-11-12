#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartAssistant.Services.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Services.Domain;

/// <summary>
/// 冻结的助手发布快照，保留版本号、内容哈希和发布、撤回审计信息。
/// </summary>
public sealed class AssistantPublishedVersion
{
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AssistantPublishedVersion() { }

    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; private set; } = string.Empty;
    /// <summary>
    /// 表示助手发布版本的助手密钥信息。
    /// </summary>
    public string AssistantKey { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    public string AssistantId { get; private set; } = string.Empty;
    /// <summary>
    /// 记录发布版本或文档版本的递增编号。
    /// </summary>
    public long VersionNo { get; private set; }
    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    public string Name { get; private set; } = string.Empty;
    /// <summary>
    /// 记录此资源的可选说明。
    /// </summary>
    public string? Description { get; private set; }
    /// <summary>
    /// 发布时冻结的配置与执行依赖，后续草稿修改不会覆盖此快照。
    /// </summary>
    public string SnapshotJson { get; private set; } = "{}";
    /// <summary>
    /// 发布快照的内容哈希，用于标识被冻结的配置内容。
    /// </summary>
    public string SnapshotHash { get; private set; } = string.Empty;
    /// <summary>
    /// 标记发布版本或访问授权是否已撤回。
    /// </summary>
    public bool IsRevoked { get; private set; }
    /// <summary>
    /// 记录授权或版本撤回的时间。
    /// </summary>
    public DateTime? RevokedAt { get; private set; }
    /// <summary>
    /// 记录版本发布的时间。
    /// </summary>
    public DateTime PublishedAt { get; private set; }

    /// <summary>
    /// 根据校验后的输入创建助手发布版本领域对象，并初始化其状态和时间字段。
    /// </summary>
    public static AssistantPublishedVersion Create(
        AssistantDraft draft,
        long versionNo,
        DateTime now,
        string executionSnapshotJson)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        if (versionNo < 1) throw new BizResultException(BusinessCodes.BusinessException, "assistant_publication_version_invalid");

        var snapshotJson = NormalizeJson(executionSnapshotJson);
        return new AssistantPublishedVersion
        {
            Id = Guid.NewGuid().ToString("N"),
            AssistantKey = draft.Id,
            AssistantId = draft.Id,
            VersionNo = versionNo,
            Name = draft.Name,
            Description = draft.Description,
            SnapshotJson = snapshotJson,
            SnapshotHash = Hash(snapshotJson),
            PublishedAt = now
        };
    }

    /// <summary>
    /// 从持久化数据还原助手发布版本的标识、访问范围和生命周期状态。
    /// </summary>
    public static AssistantPublishedVersion Rehydrate(
        string id,
        string assistantKey,
        string assistantId,
        long versionNo,
        string name,
        string? description,
        string snapshotJson,
        string snapshotHash,
        bool isRevoked,
        DateTime? revokedAt,
        DateTime publishedAt)
        => new()
        {
            Id = id,
            AssistantKey = assistantKey,
            AssistantId = assistantId,
            VersionNo = versionNo,
            Name = name,
            Description = description,
            SnapshotJson = snapshotJson,
            SnapshotHash = snapshotHash,
            IsRevoked = isRevoked,
            RevokedAt = revokedAt,
            PublishedAt = publishedAt
        };

    /// <summary>
    /// 记录发布版本的撤回状态及撤回时间。
    /// </summary>
    public void Revoke(DateTime now)
    {
        if (IsRevoked)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_already_revoked");
        IsRevoked = true;
        RevokedAt = now;
    }

    /// <summary>
    /// 将配置文本解析并重新序列化为规范 JSON；无效 JSON 按业务校验错误拒绝。
    /// </summary>
    private static string NormalizeJson(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        try
        {
            using var document = JsonDocument.Parse(normalized);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_publication_snapshot_invalid_json");
        }
    }

    /// <summary>
    /// 计算配置文本的 SHA-256 摘要，用于检测内容变化。
    /// </summary>
    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

}
