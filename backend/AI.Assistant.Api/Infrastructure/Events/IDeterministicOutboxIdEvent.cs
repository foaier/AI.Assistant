#nullable enable

namespace AI.Assistant.Infrastructure.Events;

/// <summary>
/// 标记事件标识可作为确定性 Outbox 标识的领域事件。
/// 发布者可使用该标识关联重试中的同一业务事件，具体去重与事务行为由持久化实现负责。
/// </summary>
public interface IDeterministicOutboxIdEvent
{
}
