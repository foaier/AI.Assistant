#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartAssistant.Models.Runtime;
using SmartAssistant.Models.Transport;
using SmartAssistant.Platform;
using SmartAssistant.Services.Contracts;
using StackExchange.Redis;

namespace SmartAssistant.Services.Infrastructure.AssistantTransport;

/// <summary>
/// 使用 Redis 保存运行事件与订阅通知，支持跨进程的有序事件回放。
/// </summary>
public sealed class RedisAssistantRunEventBroker : IRunEventBroker
{
    /*
     * Redis 4.0 不支持 Streams，因此使用独立的 v2 列表键空间。
     * 旧的 Stream 键可自然过期，不会混入当前事件回放。
     */
    /// <summary>
    /// 定义Redis助手执行事件事件代理使用的发布配置值。
    /// </summary>
    private const string PublishScript = """
        local turn_id = redis.call('HGET', KEYS[2], 'turnId')
        local sequence = tonumber(redis.call('HGET', KEYS[2], 'sequence') or '0')
        local terminal = redis.call('HGET', KEYS[2], 'terminal') or '0'
        if turn_id and turn_id ~= ARGV[1] then return -1 end
        local duplicate = redis.call('GET', KEYS[3])
        if duplicate then
          if duplicate == ARGV[3] then return 1 else return -4 end
        end
        if terminal == '1' then return -2 end
        if tonumber(ARGV[2]) ~= sequence + 1 then return -3 end
        redis.call('RPUSH', KEYS[1], ARGV[3])
        redis.call('LTRIM', KEYS[1], -tonumber(ARGV[4]), -1)
        redis.call('SET', KEYS[3], ARGV[3], 'EX', ARGV[6])
        redis.call('HSET', KEYS[2], 'turnId', ARGV[1], 'sequence', ARGV[2], 'status', ARGV[7], 'terminal', ARGV[8])
        redis.call('EXPIRE', KEYS[1], ARGV[6])
        redis.call('EXPIRE', KEYS[2], ARGV[6])
        return 1
        """;

    /*
     * 事件列表与终态元数据必须一起读取。
     * 否则在 LRANGE 和 HMGET 之间完成的运行可能使订阅漏掉最后一个事件。
     */
    /// <summary>
    /// 定义Redis助手执行事件事件代理使用的回放配置值。
    /// </summary>
    private const string ReplayScript = """
        return {redis.call('LRANGE', KEYS[1], 0, -1),
                redis.call('HMGET', KEYS[2], 'turnId', 'sequence', 'status', 'terminal')}
        """;

    /// <summary>
    /// 保存用于Redis助手执行事件事件代理事件编解码的序列化器。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 保存数据库依赖，供Redis助手执行事件事件代理执行相关操作。
    /// </summary>
    private readonly IDatabase _database;
    /// <summary>
    /// 保存Redis助手执行事件事件代理在处理回放窗口大小时所需的数据或状态。
    /// </summary>
    private readonly int _replayWindowSize;
    /// <summary>
    /// 保存相关内容依赖，供Redis助手执行事件事件代理执行相关操作。
    /// </summary>
    private readonly TimeSpan _eventTtl;
    /// <summary>
    /// 保存相关内容依赖，供Redis助手执行事件事件代理执行相关操作。
    /// </summary>
    private readonly TimeSpan _pollInterval;

    /// <summary>
    /// 接收并保存Redis助手执行事件事件代理执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public RedisAssistantRunEventBroker(
        IConnectionMultiplexer connection,
        int replayWindowSize = 256,
        TimeSpan? eventTtl = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (replayWindowSize < 1)
            throw new ArgumentOutOfRangeException(nameof(replayWindowSize));

        _database = connection.GetDatabase();
        _replayWindowSize = replayWindowSize;
        _eventTtl = eventTtl ?? TimeSpan.FromMinutes(15);
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
        if (_eventTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(eventTtl));
        if (_pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    /// <summary>
    /// 按运行范围发布有序事件，并维护恢复订阅所需的事件进度。
    /// </summary>
    public async Task PublishAsync(
        AssistantRunEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);

        var keys = Keys(envelope.RunId, envelope.EventId);
        var values = new RedisValue[]
        {
            envelope.TurnId,
            envelope.Sequence,
            JsonSerializer.Serialize(envelope, JsonOptions),
            _replayWindowSize,
            envelope.EventId,
            checked((long)_eventTtl.TotalSeconds),
            (int)InferStatus(envelope.Type),
            IsTerminal(envelope.Type) ? 1 : 0
        };

        var result = await _database.ScriptEvaluateAsync(
            PublishScript,
            new RedisKey[] { keys.List, keys.Meta, keys.EventId },
            values,
            CommandFlags.DemandMaster);
        cancellationToken.ThrowIfCancellationRequested();

        switch ((long)result)
        {
            case 1:
                return;
            case -1:
                throw new BizResultException(BusinessCodes.Forbidden, "assistant_event_scope_mismatch");
            case -2:
                throw new BizResultException(BusinessCodes.Conflict, "assistant_run_terminal_event_already_set");
            case -3:
                throw new BizResultException(BusinessCodes.Conflict, "assistant_event_sequence_gap");
            case -4:
                throw new BizResultException(BusinessCodes.Conflict, "assistant_event_duplicate");
            default:
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_event_publish_failed");
        }
    }

    /// <summary>
    /// 先读取指定序号之后的保留事件，再接收后续事件；窗口缺口按统一契约返回。
    /// </summary>
    public async IAsyncEnumerable<AssistantRunEventEnvelope> SubscribeAsync(
        AssistantRunResumeRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_run_required");
        if (request.AfterSequence < 0)
            throw new BizResultException(BusinessCodes.BusinessException, "after_sequence_invalid");

        var keys = Keys(request.RunId, "__read__");
        var replay = await ReadReplayAsync(keys, request.AfterSequence, cancellationToken);
        if (replay.Gap is not null)
        {
            yield return CreateReplayGapEnvelope(request.RunId, replay.TurnId, replay.Gap);
            yield break;
        }

        foreach (var item in replay.Events)
            yield return item;
        if (replay.Terminal)
            yield break;

        var lastSequence = replay.Events.Select(item => item.Sequence).DefaultIfEmpty(request.AfterSequence).Max();
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, cancellationToken);
            var tail = await ReadReplayAsync(keys, lastSequence, cancellationToken);
            if (tail.Gap is not null)
            {
                yield return CreateReplayGapEnvelope(request.RunId, tail.TurnId, tail.Gap);
                yield break;
            }

            foreach (var item in tail.Events)
            {
                if (item.Sequence <= lastSequence)
                    continue;
                lastSequence = item.Sequence;
                yield return item;
                if (IsTerminal(item.Type))
                    yield break;
            }
        }
    }

    /// <summary>
    /// 通过单次 Redis 脚本读取事件窗口与终态元数据，避免漏掉末尾事件。
    /// </summary>
    private async Task<AssistantRedisReplaySnapshot> ReadReplayAsync(
        AssistantRedisKeys keys,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _database.ScriptEvaluateAsync(
            ReplayScript,
            new RedisKey[] { keys.List, keys.Meta },
            Array.Empty<RedisValue>(),
            CommandFlags.DemandMaster);
        cancellationToken.ThrowIfCancellationRequested();
        if ((RedisResult[]?)result is not { Length: 2 } snapshot
            || (RedisValue[]?)snapshot[0] is not { } entries
            || (RedisValue[]?)snapshot[1] is not { Length: 4 } metadata)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_event_replay_invalid");
        var meta = ReadMeta(metadata);

        if (entries.Length == 0)
        {
            if (meta.Sequence > afterSequence)
            {
                var gap = new AssistantReplayGap(
                    keys.RunId,
                    afterSequence,
                    meta.Sequence + 1,
                    meta.Sequence,
                    meta.Status);
                return new AssistantRedisReplaySnapshot(Array.Empty<AssistantRunEventEnvelope>(), meta.TurnId, meta.Terminal, gap);
            }

            return new AssistantRedisReplaySnapshot(Array.Empty<AssistantRunEventEnvelope>(), meta.TurnId, meta.Terminal, null);
        }

        var events = entries.Select(Deserialize).ToArray();
        var first = events[0];
        var current = Math.Max(meta.Sequence, events[^1].Sequence);
        if (afterSequence < first.Sequence - 1)
        {
            var gap = new AssistantReplayGap(
                keys.RunId,
                afterSequence,
                first.Sequence,
                current,
                meta.Status);
            return new AssistantRedisReplaySnapshot(Array.Empty<AssistantRunEventEnvelope>(), first.TurnId, meta.Terminal, gap);
        }

        return new AssistantRedisReplaySnapshot(
            events.Where(item => item.Sequence > afterSequence).ToArray(),
            first.TurnId,
            meta.Terminal || events.Any(item => IsTerminal(item.Type)),
            null);
    }

    /// <summary>
    /// 读取并校验 Redis 中运行事件窗口的元数据。
    /// </summary>
    private static AssistantRedisMetaSnapshot ReadMeta(RedisValue[] values)
    {
        var sequence = long.TryParse(values[1].ToString(), out var parsedSequence) ? parsedSequence : -1;
        var status = int.TryParse(values[2].ToString(), out var parsedStatus)
            && Enum.IsDefined(typeof(AssistantRunStatus), parsedStatus)
            ? (AssistantRunStatus)parsedStatus
            : AssistantRunStatus.Accepted;
        return new AssistantRedisMetaSnapshot(
            values[0].HasValue ? values[0].ToString() : string.Empty,
            sequence,
            status,
            values[3] == "1");
    }

    /// <summary>
    /// 将 Redis 中的事件负载解析为统一事件信封，格式错误时报告数据损坏。
    /// </summary>
    private static AssistantRunEventEnvelope Deserialize(RedisValue entry)
    {
        if (!entry.HasValue)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_event_payload_missing");

        try
        {
            return JsonSerializer.Deserialize<AssistantRunEventEnvelope>(entry.ToString(), JsonOptions)
                ?? throw new BizResultException(BusinessCodes.BusinessException, "assistant_event_payload_invalid");
        }
        catch (JsonException)
        {
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_event_payload_invalid");
        }
    }

    /// <summary>
    /// 创建包含当前回放边界和运行状态的序号缺口事件。
    /// </summary>
    private static AssistantRunEventEnvelope CreateReplayGapEnvelope(
        string runId,
        string turnId,
        AssistantReplayGap gap)
        => AssistantRunEventEnvelope.Create(
            $"{runId}:replay-gap:{gap.RequestedAfterSequence}:{gap.CurrentSequence}",
            runId,
            string.IsNullOrWhiteSpace(turnId) ? "replay-gap" : turnId,
            Math.Max(1, gap.CurrentSequence + 1),
            AssistantRunEventTypes.ReplayGap,
            DateTime.UtcNow,
            gap);

    /// <summary>
    /// 生成同一运行使用的 Redis 键集合，并保持键空间版本隔离。
    /// </summary>
    private static AssistantRedisKeys Keys(string runId, string eventId)
    {
        var keySegment = Uri.EscapeDataString(runId);
        var eventSegment = Uri.EscapeDataString(eventId);
        return new AssistantRedisKeys(
            runId,
            $"assistant:run:replay-v2:{{{keySegment}}}:events",
            $"assistant:run:replay-v2:{{{keySegment}}}:meta",
            $"assistant:run:replay-v2:{{{keySegment}}}:event-id:{eventSegment}");
    }

    /// <summary>
    /// 从已记录的终态事件推断运行的当前持久状态。
    /// </summary>
    private static AssistantRunStatus InferStatus(string type)
        => type switch
        {
            AssistantRunEventTypes.RunCompleted => AssistantRunStatus.Completed,
            AssistantRunEventTypes.RunFailed => AssistantRunStatus.Failed,
            AssistantRunEventTypes.RunCancelled => AssistantRunStatus.Cancelled,
            AssistantRunEventTypes.RunExpired => AssistantRunStatus.Expired,
            AssistantRunEventTypes.RunStarted => AssistantRunStatus.Running,
            _ => AssistantRunStatus.Accepted
        };

    /// <summary>
    /// 判断事件类型或运行状态是否已经进入不可逆终态。
    /// </summary>
    private static bool IsTerminal(string type)
        => type is AssistantRunEventTypes.RunCompleted
            or AssistantRunEventTypes.RunFailed
            or AssistantRunEventTypes.RunCancelled
            or AssistantRunEventTypes.RunExpired;
}
