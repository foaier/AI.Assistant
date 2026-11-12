#nullable enable
using StackExchange.Redis;

namespace AI.Assistant.Models.Transport;

/// <summary>
/// 封装Redis密钥相关的内部状态或数据，供所属组件完成相应处理。
/// </summary>
internal sealed record AssistantRedisKeys(
    string RunId,
    RedisKey List,
    RedisKey Meta,
    RedisKey EventId);
