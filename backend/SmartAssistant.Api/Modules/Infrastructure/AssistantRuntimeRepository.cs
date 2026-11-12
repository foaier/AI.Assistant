#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Contracts;
using SqlSugar;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 定义公开助手运行数据的持久化边界。
/// 领域与应用层只使用聚合和契约，数据库行映射封装在仓储实现内。
/// </summary>
public interface IAssistantRuntimeRepository
{
    Task<AnonymousAssistantSession?> GetSessionAsync(AssistantScopeSnapshot scope, string sessionId, CancellationToken cancellationToken);
    Task AddSessionAsync(AnonymousAssistantSession session, CancellationToken cancellationToken);
    Task<int> SaveSessionAsync(AnonymousAssistantSession session, CancellationToken cancellationToken);
    Task<int> TombstoneSessionAsync(AssistantScopeSnapshot scope, string sessionId, string actorId, DateTime deletedAt, CancellationToken cancellationToken);

    Task<AssistantConversation?> GetConversationAsync(AssistantScopeSnapshot scope, string conversationId, CancellationToken cancellationToken);
    Task AddConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken);
    Task<int> SaveConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken);
    /// <summary>
    /// 在预期对话轮次仍有效时排队写入新轮次、运行及首批消息，提交时保持这些记录一致。
    /// </summary>
    Task<bool> TryCreateTurnBundleAsync(AssistantScopeSnapshot scope, string conversationId, long expectedLastTurnNo, DateTime activityAt,
        AssistantTurn turn, AssistantRun run, AssistantMessage userMessage, AssistantMessage assistantMessage, CancellationToken cancellationToken);
    /// <summary>
    /// 按预期轮次号申请新的对话轮次，并同步更新活动时间。
    /// </summary>
    Task<long?> ReserveConversationTurnNoAsync(AssistantScopeSnapshot scope, string conversationId, long expectedLastTurnNo, DateTime activityAt, CancellationToken cancellationToken);
    Task<int> TombstoneConversationAsync(AssistantScopeSnapshot scope, string conversationId, string actorId, DateTime deletedAt, CancellationToken cancellationToken);

    Task<AssistantTurn?> GetTurnAsync(AssistantScopeSnapshot scope, string turnId, CancellationToken cancellationToken);
    Task<AssistantTurn?> GetTurnByClientMessageIdAsync(AssistantScopeSnapshot scope, string conversationId, string clientMessageId, CancellationToken cancellationToken);
    Task<AssistantTurn?> GetTurnByNumberAsync(AssistantScopeSnapshot scope, string conversationId, long turnNo, CancellationToken cancellationToken);
    Task AddTurnAsync(AssistantTurn turn, CancellationToken cancellationToken);
    Task<int> SaveTurnAsync(AssistantTurn turn, CancellationToken cancellationToken);
    Task<int> TombstoneTurnAsync(AssistantScopeSnapshot scope, string turnId, string actorId, DateTime deletedAt, CancellationToken cancellationToken);

    Task<AssistantRun?> GetRunAsync(AssistantScopeSnapshot scope, string runId, CancellationToken cancellationToken);
    Task<AssistantRun?> GetRunByTurnIdAsync(AssistantScopeSnapshot scope, string turnId, CancellationToken cancellationToken);
    Task AddRunAsync(AssistantRun run, CancellationToken cancellationToken);
    Task<int> SaveRunAsync(AssistantRun run, CancellationToken cancellationToken);
    /// <summary>
    /// 以条件更新同时推进运行与轮次到执行状态，避免已取消的运行被重新启动。
    /// </summary>
    Task<bool> TryStartRunBundleAsync(AssistantScopeSnapshot scope, AssistantRun run, AssistantTurn turn, CancellationToken cancellationToken);
    /// <summary>
    /// 按预期状态统一持久化运行、轮次与关联消息的终态，阻止并发结束流程互相覆盖。
    /// </summary>
    Task<bool> TryPersistTerminalBundleAsync(AssistantScopeSnapshot scope, AssistantRun run, AssistantRunStatus expectedRunStatus,
        AssistantTurn turn, AssistantTurnStatus expectedTurnStatus, AssistantMessage? message, AssistantMessageStatus? expectedMessageStatus,
        CancellationToken cancellationToken);
    /// <summary>
    /// 按预期事件序号申请下一个序号；条件更新未命中时返回空值，由调用方处理冲突。
    /// </summary>
    Task<long?> ReserveRunSequenceAsync(AssistantScopeSnapshot scope, string runId, long expectedSequenceHead, CancellationToken cancellationToken);
    Task<int> TombstoneRunAsync(AssistantScopeSnapshot scope, string runId, string actorId, DateTime deletedAt, CancellationToken cancellationToken);

    Task<AssistantMessage?> GetMessageAsync(AssistantScopeSnapshot scope, string messageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssistantMessage>> ListMessagesByTurnAsync(AssistantScopeSnapshot scope, string turnId, CancellationToken cancellationToken);
    Task AddMessageAsync(AssistantMessage message, CancellationToken cancellationToken);
    Task<int> SaveMessageAsync(AssistantMessage message, CancellationToken cancellationToken);
    Task<int> TombstoneMessageAsync(AssistantScopeSnapshot scope, string messageId, string actorId, DateTime deletedAt, CancellationToken cancellationToken);

    Task<AssistantToolCall?> GetToolCallAsync(AssistantScopeSnapshot scope, string toolCallId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssistantToolCall>> ListToolCallsByRunAsync(AssistantScopeSnapshot scope, string runId, CancellationToken cancellationToken);
    Task AddToolCallAsync(AssistantToolCall toolCall, CancellationToken cancellationToken);
    Task<int> SaveToolCallAsync(AssistantToolCall toolCall, CancellationToken cancellationToken);
    Task<int> TombstoneToolCallAsync(AssistantScopeSnapshot scope, string toolCallId, string actorId, DateTime deletedAt, CancellationToken cancellationToken);
}

/// <summary>
/// 按完整运行范围读写会话、对话、轮次、运行、消息与工具调用。
/// 关键状态转换采用条件更新，并与关联记录在同一事务内提交。
/// </summary>
public sealed class AssistantRuntimeRepository : IAssistantRuntimeRepository
{
    private readonly IDbContext _dbContext;

    public AssistantRuntimeRepository(IDbContext dbContext) => _dbContext = dbContext;

    public async Task<AnonymousAssistantSession?> GetSessionAsync(
        AssistantScopeSnapshot scope,
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<SessionRow>()
            .Where(x => x.Id == sessionId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        return row is null ? null : ToDomain(row);
    }

    public Task AddSessionAsync(AnonymousAssistantSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(session)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public Task<int> SaveSessionAsync(AnonymousAssistantSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UpdateAsync(
            client => client.Updateable(ToRow(session))
                .Where(x => x.Id == session.Id
                    && x.AssistantId == session.AssistantId
                    && x.TenantId == session.TenantId
                    && x.OrgId == session.OrgId
                    && x.PublishedVersionId == session.PublishedVersionId
                    && x.ExecutionMode == (int)session.ExecutionMode
                    && !x.IsDeleted)
                .ExecuteCommandAsync());
    }

    public Task<int> TombstoneSessionAsync(AssistantScopeSnapshot scope, string sessionId, string actorId, DateTime deletedAt, CancellationToken cancellationToken)
        => TombstoneAsync<SessionRow>(scope, sessionId, actorId, deletedAt, cancellationToken);

    public async Task<AssistantConversation?> GetConversationAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<ConversationRow>()
            .Where(x => x.Id == conversationId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        if (row is null || !await HasActiveSessionAsync(row.SessionId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    public Task AddConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(conversation)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<int> SaveConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveSessionAsync(conversation.SessionId, ScopeOf(conversation), cancellationToken))
            return 0;
        return await UpdateAsync(
            client => client.Updateable(ToRow(conversation))
                .Where(x => x.Id == conversation.Id
                    && x.AssistantId == conversation.AssistantId
                    && x.TenantId == conversation.TenantId
                    && x.OrgId == conversation.OrgId
                    && x.PublishedVersionId == conversation.PublishedVersionId
                    && x.ExecutionMode == (int)conversation.ExecutionMode
                    && !x.IsDeleted)
                .ExecuteCommandAsync());
    }

    /// <summary>
    /// 在预期对话轮次仍有效时排队写入新轮次、运行及首批消息，提交时保持这些记录一致。
    /// </summary>
    public async Task<bool> TryCreateTurnBundleAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        long expectedLastTurnNo,
        DateTime activityAt,
        AssistantTurn turn,
        AssistantRun run,
        AssistantMessage userMessage,
        AssistantMessage assistantMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveConversationAsync(conversationId, scope, cancellationToken))
            return false;

        var next = expectedLastTurnNo + 1;
        if (expectedLastTurnNo == long.MaxValue)
            return false;

        _dbContext.AddCommand(async client =>
        {
            var affected = await client.Updateable<ConversationRow>()
                .SetColumns(x => new ConversationRow { LastTurnNo = next, LastActivityAt = activityAt })
                .Where(x => x.Id == conversationId && x.AssistantId == scope.AssistantId && x.TenantId == scope.TenantId
                    && x.OrgId == scope.OrgId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && !x.IsDeleted && x.LastTurnNo == expectedLastTurnNo)
                .ExecuteCommandAsync();
            if (affected != 1)
                throw new AssistantRuntimeConcurrencyException();
            return 0;
        });
        _dbContext.AddCommand(client => client.Insertable(ToRow(turn)).ExecuteCommandAsync());
        _dbContext.AddCommand(client => client.Insertable(ToRow(run)).ExecuteCommandAsync());
        _dbContext.AddCommand(client => client.Insertable(ToRow(userMessage)).ExecuteCommandAsync());
        _dbContext.AddCommand(client => client.Insertable(ToRow(assistantMessage)).ExecuteCommandAsync());
        return true;
    }

    /// <summary>
    /// 按预期轮次号申请新的对话轮次，并同步更新活动时间。
    /// </summary>
    public async Task<long?> ReserveConversationTurnNoAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        long expectedLastTurnNo,
        DateTime activityAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedLastTurnNo == long.MaxValue)
            return null;
        var conversation = await FindConversationRowAsync(scope, conversationId, cancellationToken);
        if (conversation is null || !await HasActiveSessionAsync(conversation.SessionId, scope, cancellationToken))
            return null;

        var next = expectedLastTurnNo + 1;
        var affected = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Updateable<ConversationRow>()
            .SetColumns(x => new ConversationRow
            {
                LastTurnNo = next,
                LastActivityAt = activityAt
            })
            .Where(x => x.Id == conversationId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted
                && x.LastTurnNo == expectedLastTurnNo)
            .ExecuteCommandAsync();
        return affected == 1 ? next : null;
    }

    public async Task<int> TombstoneConversationAsync(AssistantScopeSnapshot scope, string conversationId, string actorId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        var row = await FindConversationRowAsync(scope, conversationId, cancellationToken);
        return row is null || !await HasActiveSessionAsync(row.SessionId, scope, cancellationToken)
            ? 0
            : await TombstoneAsync<ConversationRow>(scope, conversationId, actorId, deletedAt, cancellationToken);
    }

    public async Task<AssistantTurn?> GetTurnAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<TurnRow>()
            .Where(x => x.Id == turnId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        if (row is null || !await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    public async Task<AssistantTurn?> GetTurnByClientMessageIdAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        string clientMessageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<TurnRow>()
            .Where(x => x.ConversationId == conversationId
                && x.ClientMessageId == clientMessageId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        return row is null || !await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken)
            ? null
            : ToDomain(row);
    }

    public Task AddTurnAsync(AssistantTurn turn, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(turn)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<AssistantTurn?> GetTurnByNumberAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        long turnNo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<TurnRow>()
            .Where(x => x.ConversationId == conversationId
                && x.TurnNo == turnNo
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        return row is null || !await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken)
            ? null
            : ToDomain(row);
    }

    public async Task<int> SaveTurnAsync(AssistantTurn turn, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveConversationAsync(turn.ConversationId, ScopeOf(turn), cancellationToken))
            return 0;
        return await UpdateAsync(
            client => client.Updateable(ToRow(turn))
                .Where(x => x.Id == turn.Id
                    && x.AssistantId == turn.AssistantId
                    && x.TenantId == turn.TenantId
                    && x.OrgId == turn.OrgId
                    && x.PublishedVersionId == turn.PublishedVersionId
                    && x.ExecutionMode == (int)turn.ExecutionMode
                    && !x.IsDeleted
                    && x.Status == (int)turn.Status)
                .ExecuteCommandAsync());
    }

    public async Task<int> TombstoneTurnAsync(AssistantScopeSnapshot scope, string turnId, string actorId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        var row = await FindTurnRowAsync(scope, turnId, cancellationToken);
        return row is null || !await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken)
            ? 0
            : await TombstoneAsync<TurnRow>(scope, turnId, actorId, deletedAt, cancellationToken);
    }

    public async Task<AssistantRun?> GetRunAsync(
        AssistantScopeSnapshot scope,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<RunRow>()
            .Where(x => x.Id == runId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        if (row is null || !await HasActiveTurnAsync(row.TurnId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    public Task AddRunAsync(AssistantRun run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(run)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<AssistantRun?> GetRunByTurnIdAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<RunRow>()
            .Where(x => x.TurnId == turnId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        return row is null || !await HasActiveTurnAsync(row.TurnId, scope, cancellationToken)
            ? null
            : ToDomain(row);
    }

    public async Task<int> SaveRunAsync(AssistantRun run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveTurnAsync(run.TurnId, ScopeOf(run), cancellationToken))
            return 0;
        /*
         * SequenceHead 通过独立操作预留，数据库中的值可能已领先于当前运行对象。
         */
        return await UpdateAsync(
            client => client.Updateable<RunRow>()
                .SetColumns(x => new RunRow
                {
                    PromptTokens = run.PromptTokens,
                    CompletionTokens = run.CompletionTokens,
                    TotalTokens = run.TotalTokens
                })
                .Where(x => x.Id == run.Id
                    && x.AssistantId == run.AssistantId
                    && x.TenantId == run.TenantId
                    && x.OrgId == run.OrgId
                    && x.PublishedVersionId == run.PublishedVersionId
                    && x.ExecutionMode == (int)run.ExecutionMode
                    && !x.IsDeleted
                    && x.Status == (int)run.Status)
                .ExecuteCommandAsync());
    }

    /// <summary>
    /// 以条件更新同时推进运行与轮次到执行状态，避免已取消的运行被重新启动。
    /// </summary>
    public async Task<bool> TryStartRunBundleAsync(
        AssistantScopeSnapshot scope,
        AssistantRun run,
        AssistantTurn turn,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveTurnAsync(turn.Id, scope, cancellationToken))
            return false;
        _dbContext.AddCommand(async client =>
        {
            var runAffected = await client.Updateable<RunRow>()
                .SetColumns(x => new RunRow
                {
                    Status = (int)run.Status,
                    StartedAt = run.StartedAt,
                    CompletedAt = run.CompletedAt,
                    FailureCode = run.FailureCode,
                    PromptTokens = run.PromptTokens,
                    CompletionTokens = run.CompletionTokens,
                    TotalTokens = run.TotalTokens
                })
                .Where(x => x.Id == run.Id && x.AssistantId == scope.AssistantId && x.TenantId == scope.TenantId
                    && x.OrgId == scope.OrgId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && !x.IsDeleted && x.Status == (int)AssistantRunStatus.Accepted)
                .ExecuteCommandAsync();
            if (runAffected != 1)
                throw new AssistantRuntimeConcurrencyException();
            var turnAffected = await client.Updateable<TurnRow>()
                .SetColumns(x => new TurnRow
                {
                    Status = (int)turn.Status,
                    CompletedAt = turn.CompletedAt,
                    FailureCode = turn.FailureCode
                })
                .Where(x => x.Id == turn.Id && x.AssistantId == scope.AssistantId && x.TenantId == scope.TenantId
                    && x.OrgId == scope.OrgId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && !x.IsDeleted && x.Status == (int)AssistantTurnStatus.Accepted)
                .ExecuteCommandAsync();
            if (turnAffected != 1)
                throw new AssistantRuntimeConcurrencyException();
            return 0;
        });
        return true;
    }

    /// <summary>
    /// 按预期状态统一持久化运行、轮次与关联消息的终态，阻止并发结束流程互相覆盖。
    /// </summary>
    public async Task<bool> TryPersistTerminalBundleAsync(
        AssistantScopeSnapshot scope,
        AssistantRun run,
        AssistantRunStatus expectedRunStatus,
        AssistantTurn turn,
        AssistantTurnStatus expectedTurnStatus,
        AssistantMessage? message,
        AssistantMessageStatus? expectedMessageStatus,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveTurnAsync(turn.Id, scope, cancellationToken))
            return false;
        _dbContext.AddCommand(async client =>
        {
            var runAffected = await client.Updateable<RunRow>()
                .SetColumns(x => new RunRow
                {
                    Status = (int)run.Status,
                    StartedAt = run.StartedAt,
                    CompletedAt = run.CompletedAt,
                    FailureCode = run.FailureCode,
                    PromptTokens = run.PromptTokens,
                    CompletionTokens = run.CompletionTokens,
                    TotalTokens = run.TotalTokens
                })
                .Where(x => x.Id == run.Id && x.AssistantId == scope.AssistantId && x.TenantId == scope.TenantId
                    && x.OrgId == scope.OrgId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && !x.IsDeleted && x.Status == (int)expectedRunStatus)
                .ExecuteCommandAsync();
            if (runAffected != 1)
                throw new AssistantRuntimeConcurrencyException();
            var turnAffected = await client.Updateable<TurnRow>()
                .SetColumns(x => new TurnRow
                {
                    Status = (int)turn.Status,
                    CompletedAt = turn.CompletedAt,
                    FailureCode = turn.FailureCode
                })
                .Where(x => x.Id == turn.Id && x.AssistantId == scope.AssistantId && x.TenantId == scope.TenantId
                    && x.OrgId == scope.OrgId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && !x.IsDeleted && x.Status == (int)expectedTurnStatus)
                .ExecuteCommandAsync();
            if (turnAffected != 1)
                throw new AssistantRuntimeConcurrencyException();
            if (message is not null && expectedMessageStatus is not null)
            {
                var messageUpdate = client.Updateable<MessageRow>()
                    .SetColumns(x => new MessageRow
                    {
                        Status = (int)message.Status,
                        CompletedAt = message.CompletedAt
                    });
                /*
                 * 取消只修改消息状态，不覆盖正文。
                 * 取消方读取消息后，供应商可能又保存了新的文本增量。
                 */
                if (run.Status != AssistantRunStatus.Cancelled)
                    messageUpdate = messageUpdate.SetColumns(x => new MessageRow { ContentJson = message.ContentJson });
                var messageAffected = await messageUpdate
                    .Where(x => x.Id == message.Id && !x.IsDeleted && x.Status == (int)expectedMessageStatus.Value)
                    .ExecuteCommandAsync();
                if (messageAffected != 1)
                    throw new AssistantRuntimeConcurrencyException();
            }
            return 0;
        });
        return true;
    }

    /// <summary>
    /// 按预期事件序号申请下一个序号；条件更新未命中时返回空值，由调用方处理冲突。
    /// </summary>
    public async Task<long?> ReserveRunSequenceAsync(
        AssistantScopeSnapshot scope,
        string runId,
        long expectedSequenceHead,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedSequenceHead == long.MaxValue)
            return null;
        var run = await FindRunRowAsync(scope, runId, cancellationToken);
        if (run is null || !await HasActiveTurnAsync(run.TurnId, scope, cancellationToken))
            return null;

        var next = expectedSequenceHead + 1;
        var affected = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Updateable<RunRow>()
            .SetColumns(x => new RunRow { SequenceHead = next })
            .Where(x => x.Id == runId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted
                && x.SequenceHead == expectedSequenceHead)
            .ExecuteCommandAsync();
        return affected == 1 ? next : null;
    }

    public async Task<int> TombstoneRunAsync(AssistantScopeSnapshot scope, string runId, string actorId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        var row = await FindRunRowAsync(scope, runId, cancellationToken);
        return row is null || !await HasActiveTurnAsync(row.TurnId, scope, cancellationToken)
            ? 0
            : await TombstoneAsync<RunRow>(scope, runId, actorId, deletedAt, cancellationToken);
    }

    public async Task<AssistantMessage?> GetMessageAsync(
        AssistantScopeSnapshot scope,
        string messageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<MessageRow>()
            .Where(x => x.Id == messageId && !x.IsDeleted)
            .FirstAsync();
        if (row is null || !await HasActiveTurnAsync(row.TurnId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    public async Task<IReadOnlyList<AssistantMessage>> ListMessagesByTurnAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveTurnAsync(turnId, scope, cancellationToken))
            return Array.Empty<AssistantMessage>();

        var rows = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<MessageRow>()
            .Where(x => x.TurnId == turnId && !x.IsDeleted)
            .OrderBy(x => x.CreatedAt)
            .OrderBy(x => x.Role)
            .OrderBy(x => x.Id)
            .ToListAsync();
        return rows.Select(ToDomain).ToArray();
    }

    public Task AddMessageAsync(AssistantMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(message)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<int> SaveMessageAsync(AssistantMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveTurnAsync(message.TurnId, await ScopeForMessageAsync(message, cancellationToken), cancellationToken))
            return 0;
        return await UpdateAsync(
            client => client.Updateable(ToRow(message))
                .Where(x => x.Id == message.Id && !x.IsDeleted && x.Status == (int)message.Status)
                .ExecuteCommandAsync());
    }

    public async Task<int> TombstoneMessageAsync(AssistantScopeSnapshot scope, string messageId, string actorId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        if (!await MessageBelongsToScopeAsync(scope, messageId, cancellationToken))
            return 0;
        return await _dbContext.GetSugarScope(ConnectionType.Master)
            .Updateable<MessageRow>()
            .SetColumns(x => new MessageRow { IsDeleted = true, DeletedAt = deletedAt, DeletedBy = AssistantRuntimeGuard.Required(actorId, "ActorId") })
            .Where(x => x.Id == messageId && !x.IsDeleted)
            .ExecuteCommandAsync();
    }

    public async Task<AssistantToolCall?> GetToolCallAsync(
        AssistantScopeSnapshot scope,
        string toolCallId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<ToolCallRow>()
            .Where(x => x.Id == toolCallId && !x.IsDeleted)
            .FirstAsync();
        if (row is null || !await HasActiveRunAsync(row.RunId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    public async Task<IReadOnlyList<AssistantToolCall>> ListToolCallsByRunAsync(
        AssistantScopeSnapshot scope,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveRunAsync(runId, scope, cancellationToken))
            return Array.Empty<AssistantToolCall>();

        var rows = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<ToolCallRow>()
            .Where(x => x.RunId == runId && !x.IsDeleted)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
        return rows.Select(ToDomain).ToArray();
    }

    public Task AddToolCallAsync(AssistantToolCall toolCall, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(toolCall)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    public async Task<int> SaveToolCallAsync(AssistantToolCall toolCall, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveRunAsync(toolCall.RunId, await ScopeForToolCallAsync(toolCall, cancellationToken), cancellationToken))
            return 0;
        return await UpdateAsync(
            client => client.Updateable(ToRow(toolCall))
                .Where(x => x.Id == toolCall.Id && !x.IsDeleted)
                .ExecuteCommandAsync());
    }

    public async Task<int> TombstoneToolCallAsync(AssistantScopeSnapshot scope, string toolCallId, string actorId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        if (!await ToolCallBelongsToScopeAsync(scope, toolCallId, cancellationToken))
            return 0;
        return await _dbContext.GetSugarScope(ConnectionType.Master)
            .Updateable<ToolCallRow>()
            .SetColumns(x => new ToolCallRow { IsDeleted = true, DeletedAt = deletedAt, DeletedBy = AssistantRuntimeGuard.Required(actorId, "ActorId") })
            .Where(x => x.Id == toolCallId && !x.IsDeleted)
            .ExecuteCommandAsync();
    }

    private Task<int> UpdateAsync(Func<ISqlSugarClient, Task<int>> command)
        => command(_dbContext.GetSugarScope(ConnectionType.Master));

    private async Task<bool> HasActiveSessionAsync(
        string sessionId,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<SessionRow>()
            .Where(x => x.Id == sessionId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .AnyAsync();
    }

    private async Task<bool> HasActiveConversationAsync(
        string conversationId,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<ConversationRow>()
            .Where(x => x.Id == conversationId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        return row is not null && await HasActiveSessionAsync(row.SessionId, scope, cancellationToken);
    }

    private async Task<bool> HasActiveTurnAsync(
        string turnId,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<TurnRow>()
            .Where(x => x.Id == turnId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        return row is not null && await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken);
    }

    private async Task<bool> HasActiveRunAsync(
        string runId,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<RunRow>()
            .Where(x => x.Id == runId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
        return row is not null && await HasActiveTurnAsync(row.TurnId, scope, cancellationToken);
    }

    private async Task<bool> MessageBelongsToScopeAsync(AssistantScopeSnapshot scope, string messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<MessageRow>()
            .Where(x => x.Id == messageId && !x.IsDeleted)
            .FirstAsync();
        return row is not null && await HasActiveTurnAsync(row.TurnId, scope, cancellationToken);
    }

    private async Task<bool> ToolCallBelongsToScopeAsync(AssistantScopeSnapshot scope, string toolCallId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<ToolCallRow>()
            .Where(x => x.Id == toolCallId && !x.IsDeleted)
            .FirstAsync();
        return row is not null && await HasActiveRunAsync(row.RunId, scope, cancellationToken);
    }

    private async Task<ConversationRow?> FindConversationRowAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<ConversationRow>()
            .Where(x => x.Id == conversationId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
    }

    private async Task<TurnRow?> FindTurnRowAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<TurnRow>()
            .Where(x => x.Id == turnId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
    }

    private async Task<RunRow?> FindRunRowAsync(
        AssistantScopeSnapshot scope,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<RunRow>()
            .Where(x => x.Id == runId
                && x.AssistantId == scope.AssistantId
                && x.TenantId == scope.TenantId
                && x.OrgId == scope.OrgId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && !x.IsDeleted)
            .FirstAsync();
    }

    private async Task<AssistantScopeSnapshot> ScopeForMessageAsync(
        AssistantMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<TurnRow>()
            .Where(x => x.Id == message.TurnId && !x.IsDeleted)
            .FirstAsync();
        return row is null
            ? throw new InvalidOperationException("Message parent turn was not found.")
            : ScopeOf(row);
    }

    private async Task<AssistantScopeSnapshot> ScopeForToolCallAsync(
        AssistantToolCall toolCall,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope(ConnectionType.Master)
            .Queryable<RunRow>()
            .Where(x => x.Id == toolCall.RunId && !x.IsDeleted)
            .FirstAsync();
        return row is null
            ? throw new InvalidOperationException("Tool call parent run was not found.")
            : ScopeOf(row);
    }

    private Task<int> TombstoneAsync<TRow>(AssistantScopeSnapshot scope, string id, string actorId, DateTime deletedAt, CancellationToken cancellationToken)
        where TRow : LifecycleRow, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = AssistantRuntimeGuard.Required(actorId, "ActorId");
        return _dbContext.GetSugarScope(ConnectionType.Master)
            .Updateable<TRow>()
            .SetColumns(row => new TRow { IsDeleted = true, DeletedAt = deletedAt, DeletedBy = actor })
            .Where(row => row.Id == id
                && !row.IsDeleted
                && row.AssistantId == scope.AssistantId
                && row.TenantId == scope.TenantId
                && row.OrgId == scope.OrgId
                && row.PublishedVersionId == scope.PublishedVersionId
                && row.ExecutionMode == (int)scope.ExecutionMode)
            .ExecuteCommandAsync();
    }

    private static SessionRow ToRow(AnonymousAssistantSession value) => new()
    {
        Id = value.Id, AssistantId = value.AssistantId, TenantId = value.TenantId, OrgId = value.OrgId,
        PublishedVersionId = value.PublishedVersionId, SubjectHash = value.SubjectHash, Locale = value.Locale,
        ExecutionMode = (int)value.ExecutionMode, Status = (int)value.Status, CreatedAt = value.CreatedAt,
        LastSeenAt = value.LastSeenAt, ExpiresAt = value.ExpiresAt, RevokedAt = value.RevokedAt, RevokedBy = value.RevokedBy
    };

    private static ConversationRow ToRow(AssistantConversation value) => new()
    {
        Id = value.Id, SessionId = value.SessionId, AssistantId = value.AssistantId, TenantId = value.TenantId,
        OrgId = value.OrgId, PublishedVersionId = value.PublishedVersionId, Locale = value.Locale,
        ExecutionMode = (int)value.ExecutionMode, Status = (int)value.Status, LastTurnNo = value.LastTurnNo,
        CreatedAt = value.CreatedAt, LastActivityAt = value.LastActivityAt
    };

    private static TurnRow ToRow(AssistantTurn value) => new()
    {
        Id = value.Id, ConversationId = value.ConversationId, SessionId = value.SessionId,
        AssistantId = value.AssistantId, TenantId = value.TenantId, OrgId = value.OrgId,
        PublishedVersionId = value.PublishedVersionId, ClientMessageId = value.ClientMessageId,
        RequestHash = value.RequestHash, TurnNo = value.TurnNo, ExecutionMode = (int)value.ExecutionMode,
        Status = (int)value.Status, CreatedAt = value.CreatedAt, CompletedAt = value.CompletedAt,
        FailureCode = value.FailureCode
    };

    private static RunRow ToRow(AssistantRun value) => new()
    {
        Id = value.Id, TurnId = value.TurnId, ConversationId = value.ConversationId, SessionId = value.SessionId,
        AssistantId = value.AssistantId, TenantId = value.TenantId, OrgId = value.OrgId,
        PublishedVersionId = value.PublishedVersionId, ExecutionMode = (int)value.ExecutionMode,
        ProviderProfileRef = value.ProviderProfileRef, Model = value.Model, Status = (int)value.Status,
        SequenceHead = value.SequenceHead, CreatedAt = value.CreatedAt, StartedAt = value.StartedAt,
        CompletedAt = value.CompletedAt, FailureCode = value.FailureCode, PromptTokens = value.PromptTokens,
        CompletionTokens = value.CompletionTokens, TotalTokens = value.TotalTokens
    };

    private static MessageRow ToRow(AssistantMessage value) => new()
    {
        Id = value.Id, ConversationId = value.ConversationId, TurnId = value.TurnId, RunId = value.RunId,
        Role = (int)value.Role, Status = (int)value.Status, ContentJson = value.ContentJson,
        CreatedAt = value.CreatedAt, CompletedAt = value.CompletedAt
    };

    private static ToolCallRow ToRow(AssistantToolCall value) => new()
    {
        Id = value.Id, RunId = value.RunId, ToolId = value.ToolId, ContractVersion = value.ContractVersion,
        InputHash = value.InputHash, Status = (int)value.Status, OutputJson = value.OutputJson,
        FailureCode = value.FailureCode, CreatedAt = value.CreatedAt, CompletedAt = value.CompletedAt
    };

    private static AssistantScopeSnapshot ScopeOf(AnonymousAssistantSession value)
        => new(value.AssistantId, value.TenantId, value.OrgId, value.PublishedVersionId, value.ExecutionMode);

    private static AssistantScopeSnapshot ScopeOf(AssistantConversation value)
        => new(value.AssistantId, value.TenantId, value.OrgId, value.PublishedVersionId, value.ExecutionMode);

    private static AssistantScopeSnapshot ScopeOf(AssistantTurn value)
        => new(value.AssistantId, value.TenantId, value.OrgId, value.PublishedVersionId, value.ExecutionMode);

    private static AssistantScopeSnapshot ScopeOf(AssistantRun value)
        => new(value.AssistantId, value.TenantId, value.OrgId, value.PublishedVersionId, value.ExecutionMode);

    private static AssistantScopeSnapshot ScopeOf(TurnRow value)
        => new(value.AssistantId, value.TenantId, value.OrgId, value.PublishedVersionId, (AssistantExecutionMode)value.ExecutionMode);

    private static AssistantScopeSnapshot ScopeOf(RunRow value)
        => new(value.AssistantId, value.TenantId, value.OrgId, value.PublishedVersionId, (AssistantExecutionMode)value.ExecutionMode);

    private static AnonymousAssistantSession ToDomain(SessionRow row)
        => AnonymousAssistantSession.Rehydrate(row.Id, row.AssistantId, row.TenantId, row.OrgId,
            row.PublishedVersionId, row.SubjectHash, row.Locale, (AssistantExecutionMode)row.ExecutionMode,
            (AssistantSessionStatus)row.Status, row.CreatedAt, row.LastSeenAt, row.ExpiresAt, row.RevokedAt, row.RevokedBy);

    private static AssistantConversation ToDomain(ConversationRow row)
        => AssistantConversation.Rehydrate(row.Id, row.AssistantId, row.SessionId, row.TenantId, row.OrgId,
            row.PublishedVersionId, row.Locale, (AssistantExecutionMode)row.ExecutionMode,
            (AssistantConversationStatus)row.Status, row.LastTurnNo, row.CreatedAt, row.LastActivityAt);

    private static AssistantTurn ToDomain(TurnRow row)
        => AssistantTurn.Rehydrate(row.Id, row.ConversationId, row.SessionId, row.AssistantId, row.TenantId,
            row.OrgId, row.PublishedVersionId, row.ClientMessageId, row.RequestHash, row.TurnNo,
            (AssistantExecutionMode)row.ExecutionMode, (AssistantTurnStatus)row.Status, row.CreatedAt,
            row.CompletedAt, row.FailureCode);

    private static AssistantRun ToDomain(RunRow row)
        => AssistantRun.Rehydrate(row.Id, row.TurnId, row.ConversationId, row.SessionId, row.AssistantId,
            row.TenantId, row.OrgId, row.PublishedVersionId, (AssistantExecutionMode)row.ExecutionMode,
            row.ProviderProfileRef, row.Model, (AssistantRunStatus)row.Status, row.SequenceHead, row.CreatedAt,
            row.StartedAt, row.CompletedAt, row.FailureCode, row.PromptTokens, row.CompletionTokens, row.TotalTokens);

    private static AssistantMessage ToDomain(MessageRow row)
        => AssistantMessage.Rehydrate(row.Id, row.ConversationId, row.TurnId, row.RunId,
            (AssistantMessageRole)row.Role, (AssistantMessageStatus)row.Status, row.ContentJson,
            row.CreatedAt, row.CompletedAt);

    private static AssistantToolCall ToDomain(ToolCallRow row)
        => AssistantToolCall.Rehydrate(row.Id, row.RunId, row.ToolId, row.ContractVersion, row.InputHash,
            (AssistantToolCallStatus)row.Status, row.OutputJson, row.FailureCode, row.CreatedAt, row.CompletedAt);

    /// <summary>
    /// 运行数据的公共生命周期字段，包含状态与逻辑删除审计信息。
    /// </summary>
    internal abstract class LifecycleRow
    {
        public string Id { get; set; } = string.Empty;
        public string AssistantId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string OrgId { get; set; } = string.Empty;
        public string PublishedVersionId { get; set; } = string.Empty;
        public int ExecutionMode { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }

    /// <summary>
    /// 匿名会话的数据库行映射。
    /// </summary>
    [SugarTable("assistant_anonymous_session")]
    internal sealed class SessionRow : LifecycleRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public new string Id { get => base.Id; set => base.Id = value; }
        [SugarColumn(ColumnName = "assistant_id")] public new string AssistantId { get => base.AssistantId; set => base.AssistantId = value; }
        [SugarColumn(ColumnName = "tenant_id")] public new string TenantId { get => base.TenantId; set => base.TenantId = value; }
        [SugarColumn(ColumnName = "org_id")] public new string OrgId { get => base.OrgId; set => base.OrgId = value; }
        [SugarColumn(ColumnName = "published_version_id")] public new string PublishedVersionId { get => base.PublishedVersionId; set => base.PublishedVersionId = value; }
        [SugarColumn(ColumnName = "execution_mode")] public new int ExecutionMode { get => base.ExecutionMode; set => base.ExecutionMode = value; }
        [SugarColumn(ColumnName = "is_deleted")] public new bool IsDeleted { get => base.IsDeleted; set => base.IsDeleted = value; }
        [SugarColumn(ColumnName = "deleted_at")] public new DateTime? DeletedAt { get => base.DeletedAt; set => base.DeletedAt = value; }
        [SugarColumn(ColumnName = "deleted_by")] public new string? DeletedBy { get => base.DeletedBy; set => base.DeletedBy = value; }
        [SugarColumn(ColumnName = "subject_hash")] public string SubjectHash { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "locale")] public string Locale { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "status")] public int Status { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "last_seen_at")] public DateTime LastSeenAt { get; set; }
        [SugarColumn(ColumnName = "expires_at")] public DateTime ExpiresAt { get; set; }
        [SugarColumn(ColumnName = "revoked_at")] public DateTime? RevokedAt { get; set; }
        [SugarColumn(ColumnName = "revoked_by")] public string? RevokedBy { get; set; }
    }

    /// <summary>
    /// 对话及其当前轮次进度的数据库行映射。
    /// </summary>
    [SugarTable("assistant_conversation")]
    internal sealed class ConversationRow : LifecycleRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public new string Id { get => base.Id; set => base.Id = value; }
        [SugarColumn(ColumnName = "assistant_id")] public new string AssistantId { get => base.AssistantId; set => base.AssistantId = value; }
        [SugarColumn(ColumnName = "tenant_id")] public new string TenantId { get => base.TenantId; set => base.TenantId = value; }
        [SugarColumn(ColumnName = "org_id")] public new string OrgId { get => base.OrgId; set => base.OrgId = value; }
        [SugarColumn(ColumnName = "published_version_id")] public new string PublishedVersionId { get => base.PublishedVersionId; set => base.PublishedVersionId = value; }
        [SugarColumn(ColumnName = "execution_mode")] public new int ExecutionMode { get => base.ExecutionMode; set => base.ExecutionMode = value; }
        [SugarColumn(ColumnName = "is_deleted")] public new bool IsDeleted { get => base.IsDeleted; set => base.IsDeleted = value; }
        [SugarColumn(ColumnName = "deleted_at")] public new DateTime? DeletedAt { get => base.DeletedAt; set => base.DeletedAt = value; }
        [SugarColumn(ColumnName = "deleted_by")] public new string? DeletedBy { get => base.DeletedBy; set => base.DeletedBy = value; }
        [SugarColumn(ColumnName = "session_id")] public string SessionId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "locale")] public string Locale { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "status")] public int Status { get; set; }
        [SugarColumn(ColumnName = "last_turn_no")] public long LastTurnNo { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "last_activity_at")] public DateTime LastActivityAt { get; set; }
    }

    /// <summary>
    /// 用户请求轮次及其幂等信息的数据库行映射。
    /// </summary>
    [SugarTable("assistant_turn")]
    internal sealed class TurnRow : LifecycleRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public new string Id { get => base.Id; set => base.Id = value; }
        [SugarColumn(ColumnName = "assistant_id")] public new string AssistantId { get => base.AssistantId; set => base.AssistantId = value; }
        [SugarColumn(ColumnName = "tenant_id")] public new string TenantId { get => base.TenantId; set => base.TenantId = value; }
        [SugarColumn(ColumnName = "org_id")] public new string OrgId { get => base.OrgId; set => base.OrgId = value; }
        [SugarColumn(ColumnName = "published_version_id")] public new string PublishedVersionId { get => base.PublishedVersionId; set => base.PublishedVersionId = value; }
        [SugarColumn(ColumnName = "execution_mode")] public new int ExecutionMode { get => base.ExecutionMode; set => base.ExecutionMode = value; }
        [SugarColumn(ColumnName = "is_deleted")] public new bool IsDeleted { get => base.IsDeleted; set => base.IsDeleted = value; }
        [SugarColumn(ColumnName = "deleted_at")] public new DateTime? DeletedAt { get => base.DeletedAt; set => base.DeletedAt = value; }
        [SugarColumn(ColumnName = "deleted_by")] public new string? DeletedBy { get => base.DeletedBy; set => base.DeletedBy = value; }
        [SugarColumn(ColumnName = "conversation_id")] public string ConversationId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "session_id")] public string SessionId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "client_message_id")] public string ClientMessageId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "request_hash")] public string RequestHash { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "turn_no")] public long TurnNo { get; set; }
        [SugarColumn(ColumnName = "status")] public int Status { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "completed_at")] public DateTime? CompletedAt { get; set; }
        [SugarColumn(ColumnName = "failure_code")] public string? FailureCode { get; set; }
    }

    /// <summary>
    /// 助手运行状态、模型信息和事件进度的数据库行映射。
    /// </summary>
    [SugarTable("assistant_run")]
    internal sealed class RunRow : LifecycleRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public new string Id { get => base.Id; set => base.Id = value; }
        [SugarColumn(ColumnName = "assistant_id")] public new string AssistantId { get => base.AssistantId; set => base.AssistantId = value; }
        [SugarColumn(ColumnName = "tenant_id")] public new string TenantId { get => base.TenantId; set => base.TenantId = value; }
        [SugarColumn(ColumnName = "org_id")] public new string OrgId { get => base.OrgId; set => base.OrgId = value; }
        [SugarColumn(ColumnName = "published_version_id")] public new string PublishedVersionId { get => base.PublishedVersionId; set => base.PublishedVersionId = value; }
        [SugarColumn(ColumnName = "execution_mode")] public new int ExecutionMode { get => base.ExecutionMode; set => base.ExecutionMode = value; }
        [SugarColumn(ColumnName = "is_deleted")] public new bool IsDeleted { get => base.IsDeleted; set => base.IsDeleted = value; }
        [SugarColumn(ColumnName = "deleted_at")] public new DateTime? DeletedAt { get => base.DeletedAt; set => base.DeletedAt = value; }
        [SugarColumn(ColumnName = "deleted_by")] public new string? DeletedBy { get => base.DeletedBy; set => base.DeletedBy = value; }
        [SugarColumn(ColumnName = "turn_id")] public string TurnId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "conversation_id")] public string ConversationId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "session_id")] public string SessionId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "provider_profile_ref")] public string ProviderProfileRef { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "model")] public string Model { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "status")] public int Status { get; set; }
        [SugarColumn(ColumnName = "sequence_head")] public long SequenceHead { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "started_at")] public DateTime? StartedAt { get; set; }
        [SugarColumn(ColumnName = "completed_at")] public DateTime? CompletedAt { get; set; }
        [SugarColumn(ColumnName = "failure_code")] public string? FailureCode { get; set; }
        [SugarColumn(ColumnName = "prompt_tokens")] public int? PromptTokens { get; set; }
        [SugarColumn(ColumnName = "completion_tokens")] public int? CompletionTokens { get; set; }
        [SugarColumn(ColumnName = "total_tokens")] public int? TotalTokens { get; set; }
    }

    /// <summary>
    /// 对话消息内容与生成状态的数据库行映射。
    /// </summary>
    [SugarTable("assistant_message")]
    internal sealed class MessageRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "conversation_id")] public string ConversationId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "turn_id")] public string TurnId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "run_id")] public string? RunId { get; set; }
        [SugarColumn(ColumnName = "role")] public int Role { get; set; }
        [SugarColumn(ColumnName = "status")] public int Status { get; set; }
        [SugarColumn(ColumnName = "content_json")] public string ContentJson { get; set; } = "{}";
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "completed_at")] public DateTime? CompletedAt { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at")] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by")] public string? DeletedBy { get; set; }
    }

    /// <summary>
    /// 工具调用输入、输出及执行状态的数据库行映射。
    /// </summary>
    [SugarTable("assistant_tool_call")]
    internal sealed class ToolCallRow
    {
        [SugarColumn(IsPrimaryKey = true, ColumnName = "id")] public string Id { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "run_id")] public string RunId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "tool_id")] public string ToolId { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "contract_version")] public string ContractVersion { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "input_hash")] public string InputHash { get; set; } = string.Empty;
        [SugarColumn(ColumnName = "status")] public int Status { get; set; }
        [SugarColumn(ColumnName = "output_json")] public string? OutputJson { get; set; }
        [SugarColumn(ColumnName = "failure_code")] public string? FailureCode { get; set; }
        [SugarColumn(ColumnName = "created_at")] public DateTime CreatedAt { get; set; }
        [SugarColumn(ColumnName = "completed_at")] public DateTime? CompletedAt { get; set; }
        [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
        [SugarColumn(ColumnName = "deleted_at")] public DateTime? DeletedAt { get; set; }
        [SugarColumn(ColumnName = "deleted_by")] public string? DeletedBy { get; set; }
    }
}

/// <summary>
/// 表示运行数据的条件更新未命中预期状态，需要重新读取或报告并发冲突。
/// </summary>
public sealed class AssistantRuntimeConcurrencyException : InvalidOperationException
{
    public AssistantRuntimeConcurrencyException()
        : base("assistant_runtime_concurrency_conflict") { }
}
