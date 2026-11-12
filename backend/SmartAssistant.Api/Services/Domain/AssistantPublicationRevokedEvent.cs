#nullable enable
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SmartAssistant.Platform.Events;

namespace SmartAssistant.Services.Domain;

/// <summary>
/// 发布撤回后的领域事件，记录助手、发布版本和指针修订号。
/// </summary>
public sealed class AssistantPublicationRevokedEvent : DomainEvent, IDeterministicOutboxIdEvent
{
    /// <summary>
    /// 接收并保存助手发布已撤回事件执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantPublicationRevokedEvent(
        string assistantId,
        string publishedVersionId,
        long pointerRevision)
    {
        var identity = string.Concat(
            assistantId, "\0", publishedVersionId, "\0",
            pointerRevision.ToString(CultureInfo.InvariantCulture));
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16)).ToLowerInvariant();
        Id = $"assistant-publication-revoked:{fingerprint}";
        AssistantId = assistantId;
        PublishedVersionId = publishedVersionId;
        PointerRevision = pointerRevision;
    }

    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    public string AssistantId { get; }
    /// <summary>
    /// 绑定创建此会话或运行时使用的固定发布版本。
    /// </summary>
    public string PublishedVersionId { get; }
    /// <summary>
    /// 记录发布指针修订号，用于检测并发发布或撤回。
    /// </summary>
    public long PointerRevision { get; }
}
