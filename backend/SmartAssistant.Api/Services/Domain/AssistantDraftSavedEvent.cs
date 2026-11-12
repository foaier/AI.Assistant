#nullable enable
using SmartAssistant.Platform.Events;

namespace SmartAssistant.Services.Domain;

/// <summary>
/// 草稿保存后产生的领域事件，以确定的草稿修订标识关联 Outbox 记录。
/// </summary>
public sealed class AssistantDraftSavedEvent : DomainEvent, IDeterministicOutboxIdEvent
{
    /// <summary>
    /// 接收并保存助手草稿保存事件执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantDraftSavedEvent(string assistantId, long revision)
    {
        Id = $"assistant-draft-saved:{assistantId}:{revision}";
        AssistantId = assistantId;
        Revision = revision;
    }

    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    public string AssistantId { get; }
    /// <summary>
    /// 记录当前配置修订号，用于乐观并发控制。
    /// </summary>
    public long Revision { get; }
}
