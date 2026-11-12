#nullable enable
using AI.Assistant.Platform.Events;

namespace AI.Assistant.Services.Domain;

/// <summary>
/// 发布成功后的领域事件，记录新发布版本及操作信息。
/// </summary>
public sealed class AssistantPublishedEvent : DomainEvent, IDeterministicOutboxIdEvent
{
    /// <summary>
    /// 接收并保存助手发布事件执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantPublishedEvent(
        string assistantId,
        string publishedVersionId,
        long versionNo)
    {
        Id = $"assistant-published:{assistantId}:{versionNo}";
        AssistantId = assistantId;
        PublishedVersionId = publishedVersionId;
        VersionNo = versionNo;
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
    /// 记录发布版本或文档版本的递增编号。
    /// </summary>
    public long VersionNo { get; }
}
