// Spec: REQ-20260926-005 W-04; S2-B-05; TAPD 1130533319001146220.
#nullable enable
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Application;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Infrastructure.AssistantTransport;
using SmartAssistant.Modules.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantRunCancellationTests
{
    [Fact]
    public async Task Cancellation_after_sequence_reservation_preserves_delta_and_terminal_delivery()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        fixture.AfterReserve = sequence =>
        {
            if (sequence == 2) cancellation.Cancel();
            return Task.CompletedTask;
        };

        await fixture.ExecuteAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));

        var events = await fixture.ReadEventsAsync();
        Assert.Equal(new[] { "run.started", "message.delta", "run.cancelled" }, events.Select(e => e.Type));
        Assert.Equal(new long[] { 1, 2, 3 }, events.Select(e => e.Sequence));
        Assert.Equal(AssistantRunStatus.Cancelled, fixture.Run.Status);
        Assert.Contains("partial answer", fixture.Message.ContentJson);
    }

    [Fact]
    public async Task Stop_during_delta_publication_delivers_one_terminal_event_after_the_delta()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        var reserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.AfterReserve = async sequence =>
        {
            if (sequence != 2) return;
            reserved.SetResult();
            await release.Task;
        };
        var executing = fixture.ExecuteAsync(cancellation.Token);
        try
        {
            await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopping = fixture.Orchestrator.CancelRunAsync("key", fixture.Run.Id, "session");
            release.SetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult();
            cancellation.Cancel();
            await executing.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var events = await fixture.ReadEventsAsync();
        Assert.Equal(new[] { "run.started", "message.delta", "run.cancelled" }, events.Select(e => e.Type));
        Assert.Equal(AssistantRunStatus.Cancelled, fixture.Run.Status);
        Assert.Equal(AssistantTurnStatus.Cancelled, fixture.Turn.Status);
    }

    [Fact]
    public async Task Stop_retries_when_the_worker_starts_between_read_and_terminal_commit()
    {
        var fixture = new Fixture { StartDuringFirstCancel = true };
        await fixture.Orchestrator.CancelRunAsync("key", fixture.Run.Id, "session");
        Assert.Equal(AssistantRunStatus.Cancelled, fixture.Run.Status);
        Assert.Equal(AssistantTurnStatus.Cancelled, fixture.Turn.Status);
        Assert.Equal("run.cancelled", Assert.Single(await fixture.ReadEventsAsync()).Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_rechecks_a_terminal_turn_read_after_an_active_run(bool completed)
    {
        var fixture = new Fixture();
        fixture.Run.Start(DateTime.UtcNow);
        fixture.Turn.Start(DateTime.UtcNow);
        fixture.BeforeReadTurn = () =>
        {
            fixture.BeforeReadTurn = null;
            if (completed)
            {
                fixture.Run.Complete(DateTime.UtcNow);
                fixture.Turn.Complete(DateTime.UtcNow);
            }
            else
            {
                fixture.Run.Cancel(DateTime.UtcNow);
                fixture.Turn.Cancel(DateTime.UtcNow);
            }
        };

        await fixture.Orchestrator.CancelRunAsync("key", fixture.Run.Id, "session");

        Assert.Equal(completed ? AssistantRunStatus.Completed : AssistantRunStatus.Cancelled, fixture.Run.Status);
        Assert.Equal(completed ? AssistantTurnStatus.Completed : AssistantTurnStatus.Cancelled, fixture.Turn.Status);
        Assert.Equal(0, fixture.Run.SequenceHead);
    }

    [Fact]
    public async Task Stop_cancels_the_submitted_provider_stream_and_repeated_stop_is_idempotent()
    {
        var fixture = new Fixture();
        await fixture.SubmitAsync();
        Assert.Equal(AssistantRunStatus.Running, fixture.Run.Status);
        Assert.Contains("partial answer", fixture.Message.ContentJson);

        await fixture.Orchestrator.CancelRunAsync("key", fixture.Run.Id, "session");
        await fixture.ProviderStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Orchestrator.CancelRunAsync("key", fixture.Run.Id, "session");

        var events = await fixture.ReadEventsAsync();
        Assert.Equal(new[] { "run.accepted", "run.started", "message.delta", "run.cancelled" }, events.Select(e => e.Type));
        Assert.Equal(AssistantRunStatus.Cancelled, fixture.Run.Status);
        Assert.Equal(AssistantTurnStatus.Cancelled, fixture.Turn.Status);
        Assert.Equal(AssistantMessageStatus.Failed, fixture.Message.Status);
        Assert.Contains("partial answer", fixture.Message.ContentJson);
    }

    [Fact]
    public async Task Persistent_terminal_conflicts_are_reported_instead_of_acknowledging_an_active_run()
    {
        var fixture = new Fixture { TerminalConflictsRemaining = 3 };
        await Assert.ThrowsAsync<AssistantRuntimeConcurrencyException>(() => fixture.Orchestrator.CancelRunAsync("key", fixture.Run.Id, "session"));
        Assert.Equal(AssistantRunStatus.Accepted, fixture.Run.Status);
        Assert.Equal(0, fixture.TerminalConflictsRemaining);
    }

    [Fact]
    public async Task Stop_cancels_provider_without_waiting_for_terminal_event_delivery()
    {
        var fixture = new Fixture();
        await fixture.SubmitAsync();
        var reserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.AfterReserve = async sequence =>
        {
            if (sequence != 4) return;
            reserved.SetResult();
            await release.Task;
        };
        var stopping = fixture.Orchestrator.CancelRunAsync("key", fixture.Run.Id, "session");
        try
        {
            await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.ProviderStopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(stopping.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal("run.cancelled", (await fixture.ReadEventsAsync())[^1].Type);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Terminal_message_SQL_preserves_latest_content_on_cancel_and_writes_final_content_on_completion(bool cancelled)
    {
        // Exercise production repository translation; substitute only the DB
        // execution boundary. This is SQL proof, not a MySQL round-trip.
        var writes = new List<string>();
        var commands = new List<Func<ISqlSugarClient, Task<int>>>();
        using var sugar = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.MySql,
            ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=offline;Password=unused",
            IsAutoCloseConnection = true,
            SqlMiddle = new SqlMiddle
            {
                IsSqlMiddle = true,
                GetDataReaderAsync = (_, _) =>
                {
                    var table = new DataTable();
                    table.Columns.Add("conversation_id", typeof(string));
                    table.Columns.Add("session_id", typeof(string));
                    table.Rows.Add("conversation", "session");
                    return Task.FromResult<IDataReader>(table.CreateDataReader());
                },
                GetScalarAsync = (_, _) => Task.FromResult<object>(1),
                ExecuteCommandAsync = (sql, _) =>
                {
                    writes.Add(sql);
                    return Task.FromResult(1);
                }
            }
        });
        var context = Stub<IDbContext>((name, args) =>
        {
            if (name == "GetSugarScope") return sugar;
            if (name != "AddCommand") throw new NotSupportedException(name);
            commands.Add((Func<ISqlSugarClient, Task<int>>)args[0]!);
            return null;
        });
        var repository = new AssistantRuntimeRepository(context);
        var fixture = new Fixture();
        var now = DateTime.UtcNow;
        fixture.Run.Start(now);
        fixture.Turn.Start(now);
        fixture.Message.AppendAssistantText("earlier snapshot");
        if (cancelled)
        {
            fixture.Run.Cancel(now);
            fixture.Turn.Cancel(now);
            fixture.Message.Fail(now);
        }
        else
        {
            fixture.Run.Complete(now);
            fixture.Turn.Complete(now);
            fixture.Message.Complete("{\"text\":\"final answer\"}", now);
        }

        Assert.True(await repository.TryPersistTerminalBundleAsync(fixture.Scope, fixture.Run, AssistantRunStatus.Running,
            fixture.Turn, AssistantTurnStatus.Running, fixture.Message, AssistantMessageStatus.Streaming, CancellationToken.None));
        await Assert.Single(commands)(sugar);

        var messageUpdate = Assert.Single(writes, sql => sql.Contains("UPDATE `assistant_message`"));
        var assignments = messageUpdate[..messageUpdate.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase)];
        if (cancelled) Assert.DoesNotContain("`content_json`", assignments);
        else Assert.Contains("`content_json`", assignments);
        Assert.Contains("`status`", assignments);
        Assert.Contains("`completed_at`", assignments);
        var guard = messageUpdate[assignments.Length..];
        Assert.Contains("`status`", guard);
        Assert.Contains("`is_deleted`", guard);
    }

    private sealed class Fixture
    {
        public readonly AssistantScopeSnapshot Scope = new("assistant", "tenant", "org", "published", AssistantExecutionMode.Public);
        public AssistantTurn Turn;
        public AssistantRun Run;
        public AssistantMessage Message;
        public readonly AssistantRunOrchestrator Orchestrator;
        private readonly InMemoryAssistantRunEventBroker _broker = new();
        public Func<long, Task>? AfterReserve;
        public Action? BeforeReadTurn;
        public bool StartDuringFirstCancel;
        public int TerminalConflictsRemaining;
        public readonly TaskCompletionSource ProviderStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AssistantPublicAuthority _authority;
        private Action? _pendingCommit;
        private readonly AssistantResolvedPolicy _policy = new("policy", 1, "customer_service", "provider", "Provider", "model", "", 0.2, 100, false, 5, false, "");

        public Fixture()
        {
            var now = DateTime.UtcNow;
            Turn = AssistantTurn.Create(Guid.NewGuid().ToString("N"), "conversation", "session", "assistant", "tenant", "org",
                "published", "request", "text", "question", "zh-cn", 1, AssistantExecutionMode.Public, now);
            Run = AssistantRun.Create(Guid.NewGuid().ToString("N"), Turn, "provider", "model", now);
            Message = AssistantMessage.CreateAssistant("message", "conversation", Turn.Id, Run.Id, now);
            var session = AnonymousAssistantSession.Create("session", "assistant", "tenant", "org", "published", "subject", "zh-cn", AssistantExecutionMode.Public, now, now.AddHours(1));
            var conversation = AssistantConversation.Create("conversation", "assistant", "session", "tenant", "org", "published", "zh-cn", AssistantExecutionMode.Public, now);
            _authority = new("key", Scope, "Assistant", null,
                JsonSerializer.SerializeToElement(new { runtime = new AssistantExecutionSnapshot(1, _policy, null, [], []) }), 1, "hash");
            var runtime = Stub<IAssistantRuntimeRepository>((name, args) => name switch
            {
                "GetSessionAsync" => Task.FromResult<AnonymousAssistantSession?>(session),
                "GetConversationAsync" => Task.FromResult<AssistantConversation?>(conversation),
                "GetTurnByClientMessageIdAsync" => Task.FromResult<AssistantTurn?>(null),
                "TryCreateTurnBundleAsync" => CreateBundle(args),
                "GetRunAsync" => Task.FromResult<AssistantRun?>(Clone(Run)),
                "GetTurnAsync" => ReadTurn(),
                "ListMessagesByTurnAsync" => Task.FromResult<IReadOnlyList<AssistantMessage>>(new[] { Clone(Message) }),
                "TryStartRunBundleAsync" => Start((AssistantRun)args[1]!, (AssistantTurn)args[2]!),
                "SaveMessageAsync" => Save((AssistantMessage)args[0]!),
                "ReserveRunSequenceAsync" => ReserveAsync((long)args[2]!, (CancellationToken)args[3]!),
                "TryPersistTerminalBundleAsync" => Terminal(args),
                _ => throw new NotSupportedException(name)
            });
            var authority = Stub<IAssistantPublicAuthorityResolver>((_, _) => Task.FromResult<AssistantPublicAuthority?>(_authority));
            var db = Stub<IDbContext>((name, _) => name == "CommitAsync" ? Commit() : throw new NotSupportedException(name));
            var policy = Stub<IAssistantPolicyService>((_, _) => Task.CompletedTask);
            var tickets = Stub<IStreamTicketService>((_, args) => Task.FromResult(new AssistantStreamTicket("ticket", "assistant", "published",
                "session", "conversation", (string)args[3]!, (string)args[4]!, AssistantExecutionMode.Public,
                (IReadOnlySet<AssistantTransportKind>)args[5]!, (DateTime)args[6]!)));
            Orchestrator = new(authority, runtime, db, new WaitingProvider(ProviderStopped), _broker, tickets,
                new AssistantExecutionSnapshotService(policy, null!, null!, null!), policy, null!, null!,
                NullLogger<AssistantRunOrchestrator>.Instance);
        }

        public Task<AssistantTurnAccepted> SubmitAsync() => Orchestrator.SubmitAuthorizedTurnAsync(
            new("key", "conversation", "request", "text", "question", "zh-cn"), "session", _authority);

        private Task<bool> CreateBundle(object?[] args)
        {
            Turn = Clone((AssistantTurn)args[4]!);
            Run = Clone((AssistantRun)args[5]!);
            Message = Clone((AssistantMessage)args[7]!);
            return Task.FromResult(true);
        }

        public Task ExecuteAsync(CancellationToken token) => Orchestrator.ExecuteRunAsync(Scope, "session", "conversation", Turn.Id, Run.Id,
            "zh-cn", "question", null, _policy, new(1, _policy, null, [], []), token);

        private Task<AssistantTurn?> ReadTurn()
        {
            BeforeReadTurn?.Invoke();
            return Task.FromResult<AssistantTurn?>(Clone(Turn));
        }

        private Task<bool> Start(AssistantRun run, AssistantTurn turn)
        {
            Run = Clone(run); Turn = Clone(turn);
            return Task.FromResult(true);
        }

        private Task<int> Save(AssistantMessage message)
        {
            if (Message.Status != message.Status) return Task.FromResult(0);
            Message = Clone(message);
            return Task.FromResult(1);
        }

        private async Task<long?> ReserveAsync(long expected, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Run.SequenceHead != expected) return null;
            var next = expected + 1;
            Run = AssistantRun.Rehydrate(Run.Id, Run.TurnId, Run.ConversationId, Run.SessionId, Run.AssistantId, Run.TenantId,
                Run.OrgId, Run.PublishedVersionId, Run.ExecutionMode, Run.ProviderProfileRef, Run.Model, Run.Status, next,
                Run.CreatedAt, Run.StartedAt, Run.CompletedAt, Run.FailureCode, Run.PromptTokens, Run.CompletionTokens, Run.TotalTokens);
            if (AfterReserve is not null) await AfterReserve(next);
            return next;
        }

        private Task<bool> Terminal(object?[] args)
        {
            _pendingCommit = () => ApplyTerminal(args);
            return Task.FromResult(true);
        }

        private Task<int> Commit()
        {
            var command = _pendingCommit;
            _pendingCommit = null;
            command?.Invoke();
            return Task.FromResult(0);
        }

        private void ApplyTerminal(object?[] args)
        {
            if (TerminalConflictsRemaining > 0)
            {
                TerminalConflictsRemaining--;
                throw new AssistantRuntimeConcurrencyException();
            }
            if (StartDuringFirstCancel)
            {
                StartDuringFirstCancel = false;
                Run.Start(DateTime.UtcNow); Turn.Start(DateTime.UtcNow);
                throw new AssistantRuntimeConcurrencyException();
            }
            if (Run.Status != (AssistantRunStatus)args[2]! || Turn.Status != (AssistantTurnStatus)args[4]!
                || args[6] is AssistantMessageStatus expected && Message.Status != expected)
                throw new AssistantRuntimeConcurrencyException();
            // Sequence belongs to the event writer, independently of terminal state writes.
            var sequence = Run.SequenceHead;
            Run = Clone((AssistantRun)args[1]!);
            Run = AssistantRun.Rehydrate(Run.Id, Run.TurnId, Run.ConversationId, Run.SessionId, Run.AssistantId, Run.TenantId,
                Run.OrgId, Run.PublishedVersionId, Run.ExecutionMode, Run.ProviderProfileRef, Run.Model, Run.Status, sequence,
                Run.CreatedAt, Run.StartedAt, Run.CompletedAt, Run.FailureCode, Run.PromptTokens, Run.CompletionTokens, Run.TotalTokens);
            Turn = Clone((AssistantTurn)args[3]!);
            if (args[5] is AssistantMessage message)
                Message = AssistantMessage.Rehydrate(message.Id, message.ConversationId, message.TurnId, message.RunId,
                    message.Role, message.Status, Run.Status == AssistantRunStatus.Cancelled ? Message.ContentJson : message.ContentJson,
                    message.CreatedAt, message.CompletedAt);
        }

        public async Task<List<AssistantRunEventEnvelope>> ReadEventsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var events = new List<AssistantRunEventEnvelope>();
            await foreach (var envelope in _broker.SubscribeAsync(new(Run.Id, 0, AssistantTransportKind.Sse), timeout.Token)) events.Add(envelope);
            return events;
        }

        private static AssistantRun Clone(AssistantRun r) => AssistantRun.Rehydrate(r.Id, r.TurnId, r.ConversationId, r.SessionId,
            r.AssistantId, r.TenantId, r.OrgId, r.PublishedVersionId, r.ExecutionMode, r.ProviderProfileRef, r.Model, r.Status,
            r.SequenceHead, r.CreatedAt, r.StartedAt, r.CompletedAt, r.FailureCode, r.PromptTokens, r.CompletionTokens, r.TotalTokens);
        private static AssistantTurn Clone(AssistantTurn t) => AssistantTurn.Rehydrate(t.Id, t.ConversationId, t.SessionId, t.AssistantId,
            t.TenantId, t.OrgId, t.PublishedVersionId, t.ClientMessageId, t.RequestHash, t.TurnNo, t.ExecutionMode, t.Status, t.CreatedAt, t.CompletedAt, t.FailureCode);
        private static AssistantMessage Clone(AssistantMessage m) => AssistantMessage.Rehydrate(m.Id, m.ConversationId, m.TurnId, m.RunId, m.Role, m.Status, m.ContentJson, m.CreatedAt, m.CompletedAt);
    }

    private sealed class WaitingProvider(TaskCompletionSource stopped) : IChatModelProvider
    {
        public async IAsyncEnumerable<AssistantProviderChunk> StreamAsync(AssistantProviderRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                yield return new(AssistantProviderChunkKind.TextDelta, TextDelta: "partial answer");
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally { stopped.TrySetResult(); }
        }
    }

    private static T Stub<T>(Func<string, object?[], object?> invoke) where T : class
    {
        var instance = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)instance).Handler = invoke;
        return instance;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Handler = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!.Name, args ?? []);
    }
}
