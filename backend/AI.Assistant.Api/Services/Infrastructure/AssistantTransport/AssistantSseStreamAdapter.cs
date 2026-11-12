#nullable enable
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Services.Contracts;

namespace AI.Assistant.Services.Infrastructure.AssistantTransport;

/// <summary>
/// 将事件代理的订阅结果输出为 SSE，并处理恢复订阅和连接生命周期。
/// 轮次创建、模型调用和工具执行由应用层负责。
/// </summary>
public sealed class AssistantSseStreamAdapter
{
    /// <summary>
    /// 定义助手SSE流式输出适配器使用的执行终态配置值。
    /// </summary>
    private static readonly TimeSpan RunTerminalGracePeriod = TimeSpan.FromMilliseconds(500);
    /// <summary>
    /// 定义助手SSE流式输出适配器使用的帧配置值。
    /// </summary>
    private static readonly byte[] DoneFrame = Encoding.UTF8.GetBytes("data: [DONE]\n\n");

    /// <summary>
    /// 保存用于助手SSE流式输出适配器事件编解码的序列化器。
    /// </summary>
    private readonly AssistantRunEventTransportSerializer _serializer;
    /// <summary>
    /// 保存相关内容依赖，供助手SSE流式输出适配器执行相关操作。
    /// </summary>
    private readonly TimeSpan _heartbeatInterval;

    /// <summary>
    /// 接收并保存助手SSE流式输出适配器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantSseStreamAdapter(AssistantRunEventTransportSerializer serializer, TimeSpan? heartbeatInterval = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(15);
        if (_heartbeatInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(heartbeatInterval));
    }

    /// <summary>
    /// 根据已校验的恢复请求订阅运行事件，并通过当前连接输出协议对应的数据帧。
    /// </summary>
    public async Task StreamAsync(
        HttpResponse response,
        IRunEventBroker broker,
        AssistantRunResumeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(broker);
        if (request.Transport != AssistantTransportKind.Sse)
            throw new InvalidOperationException("SSE adapter requires the SSE transport kind.");

        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-store";
        response.Headers.Append("X-Accel-Buffering", "no");
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(": connected\n\n"), cancellationToken);
        await response.Body.FlushAsync(cancellationToken);

        using var subscription = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var events = broker.SubscribeAsync(request, subscription.Token).GetAsyncEnumerator(subscription.Token);
        var nextEvent = events.MoveNextAsync().AsTask();
        long? messageCompletedAt = null;
        AssistantRunEventEnvelope? messageCompletedEvent = null;
        try
        {
            while (true)
            {
                bool available;
                var waitInterval = GetWaitInterval(messageCompletedAt);
                try { available = await nextEvent.WaitAsync(waitInterval, cancellationToken); }
                catch (TimeoutException) when (!nextEvent.IsFaulted)
                {
                    /*
                     * 事件完成与心跳超时可能同时发生，先核对已完成的事件，
                     * 避免将正常的数据流误判为超时。
                     */
                    if (nextEvent.IsCompleted)
                    {
                        available = await nextEvent;
                    }
                    else
                    {
                        if (messageCompletedEvent is not null && HasTerminalGraceElapsed(messageCompletedAt))
                        {
                            await WriteRunCompletedFallbackAsync(response, messageCompletedEvent, cancellationToken);
                            await WriteDoneAsync(response, cancellationToken);
                            break;
                        }

                        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(": keep-alive\n\n"), cancellationToken);
                        await response.Body.FlushAsync(cancellationToken);
                        continue;
                    }
                }
                if (!available) break;
                var bytes = Encoding.UTF8.GetBytes(_serializer.SerializeSse(events.Current));
                await response.Body.WriteAsync(bytes, cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
                if (IsRunTerminal(events.Current.Type))
                {
                    await WriteDoneAsync(response, cancellationToken);
                    break;
                }

                if (string.Equals(events.Current.Type, AssistantRunEventTypes.MessageCompleted, StringComparison.Ordinal))
                {
                    messageCompletedAt = Stopwatch.GetTimestamp();
                    messageCompletedEvent = events.Current;
                }
                nextEvent = events.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            /*
             * 客户端可能在写入心跳时断开；先等待未完成的 MoveNext 结束，
             * 再释放异步迭代器，避免并发释放。
             */
            await subscription.CancelAsync();
            try { await nextEvent; } catch (OperationCanceledException) when (subscription.IsCancellationRequested) { }
        }
    }

    /// <summary>
    /// 根据运行终态时间计算等待间隔，确保终态宽限期结束后及时关闭输出。
    /// </summary>
    private TimeSpan GetWaitInterval(long? messageCompletedAt)
    {
        if (messageCompletedAt is null)
            return _heartbeatInterval;

        var graceRemaining = RunTerminalGracePeriod - Stopwatch.GetElapsedTime(messageCompletedAt.Value);
        return graceRemaining <= TimeSpan.Zero || graceRemaining < _heartbeatInterval
            ? graceRemaining > TimeSpan.Zero ? graceRemaining : TimeSpan.FromMilliseconds(1)
            : _heartbeatInterval;
    }

    /// <summary>
    /// 判断运行终态后的等待宽限期是否已经结束。
    /// </summary>
    private static bool HasTerminalGraceElapsed(long? messageCompletedAt)
        => messageCompletedAt is not null
            && Stopwatch.GetElapsedTime(messageCompletedAt.Value) >= RunTerminalGracePeriod;

    /// <summary>
    /// 判断是否满足执行终态。
    /// </summary>
    private static bool IsRunTerminal(string type)
        => type is AssistantRunEventTypes.RunCompleted
            or AssistantRunEventTypes.RunFailed
            or AssistantRunEventTypes.RunCancelled
            or AssistantRunEventTypes.RunExpired;

    /// <summary>
    /// 向 SSE 响应写入结束帧并刷新缓冲区。
    /// </summary>
    private static async Task WriteDoneAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        await response.Body.WriteAsync(DoneFrame, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 在需要时写入运行完成的回退事件，使客户端能够识别流已结束。
    /// </summary>
    private async Task WriteRunCompletedFallbackAsync(HttpResponse response, AssistantRunEventEnvelope messageCompletedEvent, CancellationToken cancellationToken)
    {
        var sequence = checked(messageCompletedEvent.Sequence + 1);
        var runCompleted = AssistantRunEventEnvelope.Create(
            $"{messageCompletedEvent.RunId}:{sequence}:{AssistantRunEventTypes.RunCompleted}",
            messageCompletedEvent.RunId,
            messageCompletedEvent.TurnId,
            sequence,
            AssistantRunEventTypes.RunCompleted,
            DateTime.Now,
            new { runId = messageCompletedEvent.RunId, turnId = messageCompletedEvent.TurnId });
        var bytes = Encoding.UTF8.GetBytes(_serializer.SerializeSse(runCompleted));
        await response.Body.WriteAsync(bytes, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
