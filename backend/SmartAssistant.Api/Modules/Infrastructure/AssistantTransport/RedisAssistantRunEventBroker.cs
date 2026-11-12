#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Infrastructure.AssistantTransport;

/// <summary>
/// 使用 Redis 保存运行事件与订阅通知，支持跨进程的有序事件回放。
/// </summary>
public sealed class RedisAssistantRunEventBroker : IRunEventBroker
{
    /*
     * Redis 4.0 不支持 Streams，因此使用独立的 v2 列表键空间。
     * 旧的 Stream 键可自然过期，不会混入当前事件回放。
     */
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
    private const string ReplayScript = """
        return {redis.call('LRANGE', KEYS[1], 0, -1),
                redis.call('HMGET', KEYS[2], 'turnId', 'sequence', 'status', 'terminal')}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDatabase _database;
    private readonly int _replayWindowSize;
    private readonly TimeSpan _eventTtl;
    private readonly TimeSpan _pollInterval;

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

    private async Task<ReplaySnapshot> ReadReplayAsync(
        RedisKeys keys,
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
                return new ReplaySnapshot(Array.Empty<AssistantRunEventEnvelope>(), meta.TurnId, meta.Terminal, gap);
            }

            return new ReplaySnapshot(Array.Empty<AssistantRunEventEnvelope>(), meta.TurnId, meta.Terminal, null);
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
            return new ReplaySnapshot(Array.Empty<AssistantRunEventEnvelope>(), first.TurnId, meta.Terminal, gap);
        }

        return new ReplaySnapshot(
            events.Where(item => item.Sequence > afterSequence).ToArray(),
            first.TurnId,
            meta.Terminal || events.Any(item => IsTerminal(item.Type)),
            null);
    }

    private static MetaSnapshot ReadMeta(RedisValue[] values)
    {
        var sequence = long.TryParse(values[1].ToString(), out var parsedSequence) ? parsedSequence : -1;
        var status = int.TryParse(values[2].ToString(), out var parsedStatus)
            && Enum.IsDefined(typeof(AssistantRunStatus), parsedStatus)
            ? (AssistantRunStatus)parsedStatus
            : AssistantRunStatus.Accepted;
        return new MetaSnapshot(
            values[0].HasValue ? values[0].ToString() : string.Empty,
            sequence,
            status,
            values[3] == "1");
    }

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

    private static RedisKeys Keys(string runId, string eventId)
    {
        var keySegment = Uri.EscapeDataString(runId);
        var eventSegment = Uri.EscapeDataString(eventId);
        return new RedisKeys(
            runId,
            $"assistant:run:replay-v2:{{{keySegment}}}:events",
            $"assistant:run:replay-v2:{{{keySegment}}}:meta",
            $"assistant:run:replay-v2:{{{keySegment}}}:event-id:{eventSegment}");
    }

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

    private static bool IsTerminal(string type)
        => type is AssistantRunEventTypes.RunCompleted
            or AssistantRunEventTypes.RunFailed
            or AssistantRunEventTypes.RunCancelled
            or AssistantRunEventTypes.RunExpired;

    private sealed record RedisKeys(string RunId, RedisKey List, RedisKey Meta, RedisKey EventId);

    private sealed record MetaSnapshot(string TurnId, long Sequence, AssistantRunStatus Status, bool Terminal);

    private sealed record ReplaySnapshot(
        IReadOnlyList<AssistantRunEventEnvelope> Events,
        string TurnId,
        bool Terminal,
        AssistantReplayGap? Gap);
}
