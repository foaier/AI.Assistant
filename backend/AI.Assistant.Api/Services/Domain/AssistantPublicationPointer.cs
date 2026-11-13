#nullable enable
using System;
using AI.Assistant.Infrastructure;

namespace AI.Assistant.Services.Domain;

/// <summary>
/// 发布指针的有效或撤回状态。
/// </summary>
public enum AssistantPublicationPointerStatus
{
    /// <summary>
    /// 权限或发布状态已撤回。
    /// </summary>
    Revoked = 0,
    /// <summary>
    /// 当前处于可访问或生效状态。
    /// </summary>
    Active = 1
}

/// <summary>
/// 记录公开助手当前指向的发布版本，并用修订号保护并发切换。
/// </summary>
public sealed class AssistantPublicationPointer
{
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AssistantPublicationPointer() { }

    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; private set; } = string.Empty;
    /// <summary>
    /// 表示助手发布指针的助手密钥信息。
    /// </summary>
    public string AssistantKey { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    public string AssistantId { get; private set; } = string.Empty;
    /// <summary>
    /// 当前公开可用的发布版本标识；未发布或撤回后可为空。
    /// </summary>
    public string? CurrentPublishedVersionId { get; private set; }
    /// <summary>
    /// 记录当前发布版本号。
    /// </summary>
    public long CurrentVersionNo { get; private set; }
    /// <summary>
    /// 发布指针的乐观并发修订号，发布与撤回都需校验该值。
    /// </summary>
    public long PointerRevision { get; private set; }
    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    public AssistantPublicationPointerStatus Status { get; private set; }

    /// <summary>
    /// 根据校验后的输入创建助手发布指针领域对象，并初始化业务状态。
    /// </summary>
    public static AssistantPublicationPointer Create(
        AssistantPublishedVersion version)
    {
        if (version is null) throw new ArgumentNullException(nameof(version));
        return new AssistantPublicationPointer
        {
            Id = version.AssistantId,
            AssistantKey = version.AssistantKey,
            AssistantId = version.AssistantId,
            CurrentPublishedVersionId = version.Id,
            CurrentVersionNo = version.VersionNo,
            PointerRevision = 1,
            Status = AssistantPublicationPointerStatus.Active
        };
    }

    /// <summary>
    /// 从持久化数据还原助手发布指针的标识、访问范围和生命周期状态。
    /// </summary>
    public static AssistantPublicationPointer Rehydrate(
        string id,
        string assistantKey,
        string assistantId,
        string? currentPublishedVersionId,
        long currentVersionNo,
        long pointerRevision,
        AssistantPublicationPointerStatus status)
        => new()
        {
            Id = id,
            AssistantKey = assistantKey,
            AssistantId = assistantId,
            CurrentPublishedVersionId = currentPublishedVersionId,
            CurrentVersionNo = currentVersionNo,
            PointerRevision = pointerRevision,
            Status = status
        };

    /// <summary>
    /// 校验预期指针修订后切换当前发布版本，并递增指针修订号。
    /// </summary>
    public void Advance(AssistantPublishedVersion version, long expectedRevision)
    {
        if (version is null) throw new ArgumentNullException(nameof(version));
        if (expectedRevision != PointerRevision)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_pointer_revision_conflict");
        if (!string.Equals(version.AssistantId, AssistantId, StringComparison.Ordinal))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_publication_scope_mismatch");

        CurrentPublishedVersionId = version.Id;
        CurrentVersionNo = version.VersionNo;
        PointerRevision++;
        Status = AssistantPublicationPointerStatus.Active;
    }

    /// <summary>
    /// 在预期修订匹配时撤回当前发布指针。
    /// </summary>
    public string Revoke(long expectedRevision)
    {
        if (expectedRevision != PointerRevision)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_pointer_revision_conflict");
        if (CurrentPublishedVersionId is null || Status != AssistantPublicationPointerStatus.Active)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");

        var revokedVersionId = CurrentPublishedVersionId;
        CurrentPublishedVersionId = null;
        PointerRevision++;
        Status = AssistantPublicationPointerStatus.Revoked;
        return revokedVersionId;
    }

    /// <summary>
    /// 校验必填文本并返回去除首尾空白后的值；内容缺失时报告输入错误。
    /// </summary>
    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();
}
