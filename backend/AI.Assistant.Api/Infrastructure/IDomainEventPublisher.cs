using System.Threading.Tasks;

namespace AI.Assistant.Infrastructure.Events;

/// <summary>
/// 领域事件发布器接口。
/// </summary>
public interface IDomainEventPublisher
{
    /// <summary>
    /// 发布一个领域事件。实现类通常应将其持久化到 Outbox 中。
    /// </summary>
    Task PublishAsync<T>(T @event) where T : DomainEvent;
}
