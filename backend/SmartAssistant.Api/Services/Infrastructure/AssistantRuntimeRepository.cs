#nullable enable
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Models.Runtime;
using SmartAssistant.Services.Domain;
using SmartAssistant.Services.Domain.Entities;
using SqlSugar;

namespace SmartAssistant.Services.Infrastructure;

/// <summary>
/// 定义公开助手运行数据的持久化边界。
/// 表映射实体统一定义在 Domain.Entities，仓储负责领域对象与实体之间的转换。
/// </summary>
public interface IAssistantRuntimeRepository
{
    /// <summary>
    /// 获取会话，并响应调用方的取消请求。
    /// </summary>
    Task<AnonymousAssistantSession?> GetSessionAsync(AssistantScopeSnapshot scope, string sessionId, CancellationToken cancellationToken);
    /// <summary>
    /// 添加会话，并响应调用方的取消请求。
    /// </summary>
    Task AddSessionAsync(AnonymousAssistantSession session, CancellationToken cancellationToken);
    /// <summary>
    /// 保存会话，并响应调用方的取消请求。
    /// </summary>
    Task<int> SaveSessionAsync(AnonymousAssistantSession session, CancellationToken cancellationToken);
    /// <summary>
    /// 物理删除会话，并响应调用方的取消请求。
    /// </summary>
    Task<int> DeleteSessionAsync(AssistantScopeSnapshot scope, string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// 获取对话，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantConversation?> GetConversationAsync(AssistantScopeSnapshot scope, string conversationId, CancellationToken cancellationToken);
    /// <summary>
    /// 添加对话，并响应调用方的取消请求。
    /// </summary>
    Task AddConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken);
    /// <summary>
    /// 保存对话，并响应调用方的取消请求。
    /// </summary>
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
    /// <summary>
    /// 物理删除对话，并响应调用方的取消请求。
    /// </summary>
    Task<int> DeleteConversationAsync(AssistantScopeSnapshot scope, string conversationId, CancellationToken cancellationToken);

    /// <summary>
    /// 获取轮次，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantTurn?> GetTurnAsync(AssistantScopeSnapshot scope, string turnId, CancellationToken cancellationToken);
    /// <summary>
    /// 获取轮次客户端消息，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantTurn?> GetTurnByClientMessageIdAsync(AssistantScopeSnapshot scope, string conversationId, string clientMessageId, CancellationToken cancellationToken);
    /// <summary>
    /// 获取轮次，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantTurn?> GetTurnByNumberAsync(AssistantScopeSnapshot scope, string conversationId, long turnNo, CancellationToken cancellationToken);
    /// <summary>
    /// 添加轮次，并响应调用方的取消请求。
    /// </summary>
    Task AddTurnAsync(AssistantTurn turn, CancellationToken cancellationToken);
    /// <summary>
    /// 保存轮次，并响应调用方的取消请求。
    /// </summary>
    Task<int> SaveTurnAsync(AssistantTurn turn, CancellationToken cancellationToken);
    /// <summary>
    /// 物理删除轮次，并响应调用方的取消请求。
    /// </summary>
    Task<int> DeleteTurnAsync(AssistantScopeSnapshot scope, string turnId, CancellationToken cancellationToken);

    /// <summary>
    /// 获取执行，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantRun?> GetRunAsync(AssistantScopeSnapshot scope, string runId, CancellationToken cancellationToken);
    /// <summary>
    /// 获取执行轮次，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantRun?> GetRunByTurnIdAsync(AssistantScopeSnapshot scope, string turnId, CancellationToken cancellationToken);
    /// <summary>
    /// 添加执行，并响应调用方的取消请求。
    /// </summary>
    Task AddRunAsync(AssistantRun run, CancellationToken cancellationToken);
    /// <summary>
    /// 保存执行，并响应调用方的取消请求。
    /// </summary>
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
    /// <summary>
    /// 物理删除执行，并响应调用方的取消请求。
    /// </summary>
    Task<int> DeleteRunAsync(AssistantScopeSnapshot scope, string runId, CancellationToken cancellationToken);

    /// <summary>
    /// 获取消息，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantMessage?> GetMessageAsync(AssistantScopeSnapshot scope, string messageId, CancellationToken cancellationToken);
    /// <summary>
    /// 列出消息轮次，并响应调用方的取消请求。
    /// </summary>
    Task<IReadOnlyList<AssistantMessage>> ListMessagesByTurnAsync(AssistantScopeSnapshot scope, string turnId, CancellationToken cancellationToken);
    /// <summary>
    /// 添加消息，并响应调用方的取消请求。
    /// </summary>
    Task AddMessageAsync(AssistantMessage message, CancellationToken cancellationToken);
    /// <summary>
    /// 保存消息，并响应调用方的取消请求。
    /// </summary>
    Task<int> SaveMessageAsync(AssistantMessage message, CancellationToken cancellationToken);
    /// <summary>
    /// 物理删除消息，并响应调用方的取消请求。
    /// </summary>
    Task<int> DeleteMessageAsync(AssistantScopeSnapshot scope, string messageId, CancellationToken cancellationToken);

    /// <summary>
    /// 获取工具调用，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantToolCall?> GetToolCallAsync(AssistantScopeSnapshot scope, string toolCallId, CancellationToken cancellationToken);
    /// <summary>
    /// 列出工具调用执行，并响应调用方的取消请求。
    /// </summary>
    Task<IReadOnlyList<AssistantToolCall>> ListToolCallsByRunAsync(AssistantScopeSnapshot scope, string runId, CancellationToken cancellationToken);
    /// <summary>
    /// 添加工具调用，并响应调用方的取消请求。
    /// </summary>
    Task AddToolCallAsync(AssistantToolCall toolCall, CancellationToken cancellationToken);
    /// <summary>
    /// 保存工具调用，并响应调用方的取消请求。
    /// </summary>
    Task<int> SaveToolCallAsync(AssistantToolCall toolCall, CancellationToken cancellationToken);
    /// <summary>
    /// 物理删除工具调用，并响应调用方的取消请求。
    /// </summary>
    Task<int> DeleteToolCallAsync(AssistantScopeSnapshot scope, string toolCallId, CancellationToken cancellationToken);
}

/// <summary>
/// 按完整运行范围读写会话、对话、轮次、运行、消息与工具调用。
/// 关键状态转换采用条件更新，并与关联记录在同一事务内提交。
/// </summary>
public sealed class AssistantRuntimeRepository : IAssistantRuntimeRepository
{
    /// <summary>
    /// 保存数据库上下文依赖，供助手运行时仓储执行相关操作。
    /// </summary>
    private readonly IDbContext _dbContext;

    /// <summary>
    /// 接收并保存助手运行时仓储执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantRuntimeRepository(IDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// 获取会话，并响应调用方的取消请求。
    /// </summary>
    public async Task<AnonymousAssistantSession?> GetSessionAsync(
        AssistantScopeSnapshot scope,
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<SessionEntity>()
            .Where(x => x.Id == sessionId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        return row is null ? null : ToDomain(row);
    }

    /// <summary>
    /// 添加会话，并响应调用方的取消请求。
    /// </summary>
    public Task AddSessionAsync(AnonymousAssistantSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(session)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存会话，并响应调用方的取消请求。
    /// </summary>
    public Task<int> SaveSessionAsync(AnonymousAssistantSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UpdateAsync(
            client => client.Updateable(ToRow(session))
                .Where(x => x.Id == session.Id
                    && x.AssistantId == session.AssistantId
                    && x.PublishedVersionId == session.PublishedVersionId
                    && x.ExecutionMode == (int)session.ExecutionMode)
                .ExecuteCommandAsync());
    }

    /// <summary>
    /// 物理删除会话，并响应调用方的取消请求。
    /// </summary>
    public Task<int> DeleteSessionAsync(AssistantScopeSnapshot scope, string sessionId, CancellationToken cancellationToken)
        => DeleteAsync<SessionEntity>(scope, sessionId, cancellationToken);

    /// <summary>
    /// 获取对话，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantConversation?> GetConversationAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<ConversationEntity>()
            .Where(x => x.Id == conversationId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        if (row is null || !await HasActiveSessionAsync(row.SessionId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    /// <summary>
    /// 添加对话，并响应调用方的取消请求。
    /// </summary>
    public Task AddConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(conversation)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存对话，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> SaveConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveSessionAsync(conversation.SessionId, ScopeOf(conversation), cancellationToken))
            return 0;
        return await UpdateAsync(
            client => client.Updateable(ToRow(conversation))
                .Where(x => x.Id == conversation.Id
                    && x.AssistantId == conversation.AssistantId
                    && x.PublishedVersionId == conversation.PublishedVersionId
                    && x.ExecutionMode == (int)conversation.ExecutionMode)
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
            var affected = await client.Updateable<ConversationEntity>()
                .SetColumns(x => new ConversationEntity { LastTurnNo = next, LastActivityAt = activityAt })
                .Where(x => x.Id == conversationId && x.AssistantId == scope.AssistantId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && x.LastTurnNo == expectedLastTurnNo)
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
        var affected = await _dbContext.GetSugarScope()
            .Updateable<ConversationEntity>()
            .SetColumns(x => new ConversationEntity
            {
                LastTurnNo = next,
                LastActivityAt = activityAt
            })
            .Where(x => x.Id == conversationId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && x.LastTurnNo == expectedLastTurnNo)
            .ExecuteCommandAsync();
        return affected == 1 ? next : null;
    }

    /// <summary>
    /// 物理删除对话，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> DeleteConversationAsync(AssistantScopeSnapshot scope, string conversationId, CancellationToken cancellationToken)
    {
        var row = await FindConversationRowAsync(scope, conversationId, cancellationToken);
        return row is null || !await HasActiveSessionAsync(row.SessionId, scope, cancellationToken)
            ? 0
            : await DeleteAsync<ConversationEntity>(scope, conversationId, cancellationToken);
    }

    /// <summary>
    /// 获取轮次，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantTurn?> GetTurnAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<TurnEntity>()
            .Where(x => x.Id == turnId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        if (row is null || !await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    /// <summary>
    /// 获取轮次客户端消息，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantTurn?> GetTurnByClientMessageIdAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        string clientMessageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<TurnEntity>()
            .Where(x => x.ConversationId == conversationId
                && x.ClientMessageId == clientMessageId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        return row is null || !await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken)
            ? null
            : ToDomain(row);
    }

    /// <summary>
    /// 添加轮次，并响应调用方的取消请求。
    /// </summary>
    public Task AddTurnAsync(AssistantTurn turn, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(turn)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取轮次，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantTurn?> GetTurnByNumberAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        long turnNo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<TurnEntity>()
            .Where(x => x.ConversationId == conversationId
                && x.TurnNo == turnNo
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        return row is null || !await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken)
            ? null
            : ToDomain(row);
    }

    /// <summary>
    /// 保存轮次，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> SaveTurnAsync(AssistantTurn turn, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveConversationAsync(turn.ConversationId, ScopeOf(turn), cancellationToken))
            return 0;
        return await UpdateAsync(
            client => client.Updateable(ToRow(turn))
                .Where(x => x.Id == turn.Id
                    && x.AssistantId == turn.AssistantId
                    && x.PublishedVersionId == turn.PublishedVersionId
                    && x.ExecutionMode == (int)turn.ExecutionMode
                    && x.Status == (int)turn.Status)
                .ExecuteCommandAsync());
    }

    /// <summary>
    /// 物理删除轮次，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> DeleteTurnAsync(AssistantScopeSnapshot scope, string turnId, CancellationToken cancellationToken)
    {
        var row = await FindTurnRowAsync(scope, turnId, cancellationToken);
        return row is null || !await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken)
            ? 0
            : await DeleteAsync<TurnEntity>(scope, turnId, cancellationToken);
    }

    /// <summary>
    /// 获取执行，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantRun?> GetRunAsync(
        AssistantScopeSnapshot scope,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<RunEntity>()
            .Where(x => x.Id == runId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        if (row is null || !await HasActiveTurnAsync(row.TurnId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    /// <summary>
    /// 添加执行，并响应调用方的取消请求。
    /// </summary>
    public Task AddRunAsync(AssistantRun run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(run)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取执行轮次，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantRun?> GetRunByTurnIdAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<RunEntity>()
            .Where(x => x.TurnId == turnId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        return row is null || !await HasActiveTurnAsync(row.TurnId, scope, cancellationToken)
            ? null
            : ToDomain(row);
    }

    /// <summary>
    /// 保存执行，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> SaveRunAsync(AssistantRun run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveTurnAsync(run.TurnId, ScopeOf(run), cancellationToken))
            return 0;
        /*
         * SequenceHead 通过独立操作预留，数据库中的值可能已领先于当前运行对象。
         */
        return await UpdateAsync(
            client => client.Updateable<RunEntity>()
                .SetColumns(x => new RunEntity
                {
                    PromptTokens = run.PromptTokens,
                    CompletionTokens = run.CompletionTokens,
                    TotalTokens = run.TotalTokens
                })
                .Where(x => x.Id == run.Id
                    && x.AssistantId == run.AssistantId
                    && x.PublishedVersionId == run.PublishedVersionId
                    && x.ExecutionMode == (int)run.ExecutionMode
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
            var runAffected = await client.Updateable<RunEntity>()
                .SetColumns(x => new RunEntity
                {
                    Status = (int)run.Status,
                    StartedAt = run.StartedAt,
                    CompletedAt = run.CompletedAt,
                    FailureCode = run.FailureCode,
                    PromptTokens = run.PromptTokens,
                    CompletionTokens = run.CompletionTokens,
                    TotalTokens = run.TotalTokens
                })
                .Where(x => x.Id == run.Id && x.AssistantId == scope.AssistantId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && x.Status == (int)AssistantRunStatus.Accepted)
                .ExecuteCommandAsync();
            if (runAffected != 1)
                throw new AssistantRuntimeConcurrencyException();
            var turnAffected = await client.Updateable<TurnEntity>()
                .SetColumns(x => new TurnEntity
                {
                    Status = (int)turn.Status,
                    CompletedAt = turn.CompletedAt,
                    FailureCode = turn.FailureCode
                })
                .Where(x => x.Id == turn.Id && x.AssistantId == scope.AssistantId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && x.Status == (int)AssistantTurnStatus.Accepted)
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
            var runAffected = await client.Updateable<RunEntity>()
                .SetColumns(x => new RunEntity
                {
                    Status = (int)run.Status,
                    StartedAt = run.StartedAt,
                    CompletedAt = run.CompletedAt,
                    FailureCode = run.FailureCode,
                    PromptTokens = run.PromptTokens,
                    CompletionTokens = run.CompletionTokens,
                    TotalTokens = run.TotalTokens
                })
                .Where(x => x.Id == run.Id && x.AssistantId == scope.AssistantId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && x.Status == (int)expectedRunStatus)
                .ExecuteCommandAsync();
            if (runAffected != 1)
                throw new AssistantRuntimeConcurrencyException();
            var turnAffected = await client.Updateable<TurnEntity>()
                .SetColumns(x => new TurnEntity
                {
                    Status = (int)turn.Status,
                    CompletedAt = turn.CompletedAt,
                    FailureCode = turn.FailureCode
                })
                .Where(x => x.Id == turn.Id && x.AssistantId == scope.AssistantId && x.PublishedVersionId == scope.PublishedVersionId
                    && x.ExecutionMode == (int)scope.ExecutionMode && x.Status == (int)expectedTurnStatus)
                .ExecuteCommandAsync();
            if (turnAffected != 1)
                throw new AssistantRuntimeConcurrencyException();
            if (message is not null && expectedMessageStatus is not null)
            {
                var messageUpdate = client.Updateable<MessageEntity>()
                    .SetColumns(x => new MessageEntity
                    {
                        Status = (int)message.Status,
                        CompletedAt = message.CompletedAt
                    });
                /*
                 * 取消只修改消息状态，不覆盖正文。
                 * 取消方读取消息后，供应商可能又保存了新的文本增量。
                 */
                if (run.Status != AssistantRunStatus.Cancelled)
                    messageUpdate = messageUpdate.SetColumns(x => new MessageEntity { ContentJson = message.ContentJson });
                var messageAffected = await messageUpdate
                    .Where(x => x.Id == message.Id && x.Status == (int)expectedMessageStatus.Value)
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
        var affected = await _dbContext.GetSugarScope()
            .Updateable<RunEntity>()
            .SetColumns(x => new RunEntity { SequenceHead = next })
            .Where(x => x.Id == runId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode
                && x.SequenceHead == expectedSequenceHead)
            .ExecuteCommandAsync();
        return affected == 1 ? next : null;
    }

    /// <summary>
    /// 物理删除执行，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> DeleteRunAsync(AssistantScopeSnapshot scope, string runId, CancellationToken cancellationToken)
    {
        var row = await FindRunRowAsync(scope, runId, cancellationToken);
        return row is null || !await HasActiveTurnAsync(row.TurnId, scope, cancellationToken)
            ? 0
            : await DeleteAsync<RunEntity>(scope, runId, cancellationToken);
    }

    /// <summary>
    /// 获取消息，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantMessage?> GetMessageAsync(
        AssistantScopeSnapshot scope,
        string messageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<MessageEntity>()
            .Where(x => x.Id == messageId)
            .FirstAsync();
        if (row is null || !await HasActiveTurnAsync(row.TurnId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    /// <summary>
    /// 列出消息轮次，并响应调用方的取消请求。
    /// </summary>
    public async Task<IReadOnlyList<AssistantMessage>> ListMessagesByTurnAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveTurnAsync(turnId, scope, cancellationToken))
            return Array.Empty<AssistantMessage>();

        var rows = await _dbContext.GetSugarScope()
            .Queryable<MessageEntity>()
            .Where(x => x.TurnId == turnId)
            .OrderBy("rowid")
            .ToListAsync();
        return rows.Select(ToDomain).ToArray();
    }

    /// <summary>
    /// 添加消息，并响应调用方的取消请求。
    /// </summary>
    public Task AddMessageAsync(AssistantMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(message)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存消息，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> SaveMessageAsync(AssistantMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveTurnAsync(message.TurnId, await ScopeForMessageAsync(message, cancellationToken), cancellationToken))
            return 0;
        return await UpdateAsync(
            client => client.Updateable(ToRow(message))
                .Where(x => x.Id == message.Id && x.Status == (int)message.Status)
                .ExecuteCommandAsync());
    }

    /// <summary>
    /// 物理删除消息，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> DeleteMessageAsync(AssistantScopeSnapshot scope, string messageId, CancellationToken cancellationToken)
    {
        if (!await MessageBelongsToScopeAsync(scope, messageId, cancellationToken))
            return 0;
        return await _dbContext.GetSugarScope()
            .Deleteable<MessageEntity>()
            .Where(x => x.Id == messageId)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 获取工具调用，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantToolCall?> GetToolCallAsync(
        AssistantScopeSnapshot scope,
        string toolCallId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<ToolCallEntity>()
            .Where(x => x.Id == toolCallId)
            .FirstAsync();
        if (row is null || !await HasActiveRunAsync(row.RunId, scope, cancellationToken))
            return null;
        return ToDomain(row);
    }

    /// <summary>
    /// 列出工具调用执行，并响应调用方的取消请求。
    /// </summary>
    public async Task<IReadOnlyList<AssistantToolCall>> ListToolCallsByRunAsync(
        AssistantScopeSnapshot scope,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveRunAsync(runId, scope, cancellationToken))
            return Array.Empty<AssistantToolCall>();

        var rows = await _dbContext.GetSugarScope()
            .Queryable<ToolCallEntity>()
            .Where(x => x.RunId == runId)
            .OrderBy("rowid")
            .ToListAsync();
        return rows.Select(ToDomain).ToArray();
    }

    /// <summary>
    /// 添加工具调用，并响应调用方的取消请求。
    /// </summary>
    public Task AddToolCallAsync(AssistantToolCall toolCall, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(toolCall)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存工具调用，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> SaveToolCallAsync(AssistantToolCall toolCall, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await HasActiveRunAsync(toolCall.RunId, await ScopeForToolCallAsync(toolCall, cancellationToken), cancellationToken))
            return 0;
        return await UpdateAsync(
            client => client.Updateable(ToRow(toolCall))
                .Where(x => x.Id == toolCall.Id)
                .ExecuteCommandAsync());
    }

    /// <summary>
    /// 物理删除工具调用，并响应调用方的取消请求。
    /// </summary>
    public async Task<int> DeleteToolCallAsync(AssistantScopeSnapshot scope, string toolCallId, CancellationToken cancellationToken)
    {
        if (!await ToolCallBelongsToScopeAsync(scope, toolCallId, cancellationToken))
            return 0;
        return await _dbContext.GetSugarScope()
            .Deleteable<ToolCallEntity>()
            .Where(x => x.Id == toolCallId)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 更新助手运行时记录，并响应调用方的取消请求。
    /// </summary>
    private Task<int> UpdateAsync(Func<ISqlSugarClient, Task<int>> command)
        => command(_dbContext.GetSugarScope());

    /// <summary>
    /// 判断是否存在或已具备生效会话，并响应调用方的取消请求。
    /// </summary>
    private async Task<bool> HasActiveSessionAsync(
        string sessionId,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope()
            .Queryable<SessionEntity>()
            .Where(x => x.Id == sessionId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .AnyAsync();
    }

    /// <summary>
    /// 判断是否存在或已具备生效对话，并响应调用方的取消请求。
    /// </summary>
    private async Task<bool> HasActiveConversationAsync(
        string conversationId,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<ConversationEntity>()
            .Where(x => x.Id == conversationId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        return row is not null && await HasActiveSessionAsync(row.SessionId, scope, cancellationToken);
    }

    /// <summary>
    /// 判断是否存在或已具备生效轮次，并响应调用方的取消请求。
    /// </summary>
    private async Task<bool> HasActiveTurnAsync(
        string turnId,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<TurnEntity>()
            .Where(x => x.Id == turnId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        return row is not null && await HasActiveConversationAsync(row.ConversationId, scope, cancellationToken);
    }

    /// <summary>
    /// 判断是否存在或已具备生效执行，并响应调用方的取消请求。
    /// </summary>
    private async Task<bool> HasActiveRunAsync(
        string runId,
        AssistantScopeSnapshot scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<RunEntity>()
            .Where(x => x.Id == runId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
        return row is not null && await HasActiveTurnAsync(row.TurnId, scope, cancellationToken);
    }

    /// <summary>
    /// 确认消息所属轮次仍处于指定助手、发布版本和执行模式范围内。
    /// </summary>
    private async Task<bool> MessageBelongsToScopeAsync(AssistantScopeSnapshot scope, string messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<MessageEntity>()
            .Where(x => x.Id == messageId)
            .FirstAsync();
        return row is not null && await HasActiveTurnAsync(row.TurnId, scope, cancellationToken);
    }

    /// <summary>
    /// 封装工具调用属于范围处理，并保持调用方要求的范围与状态校验。
    /// </summary>
    private async Task<bool> ToolCallBelongsToScopeAsync(AssistantScopeSnapshot scope, string toolCallId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<ToolCallEntity>()
            .Where(x => x.Id == toolCallId)
            .FirstAsync();
        return row is not null && await HasActiveRunAsync(row.RunId, scope, cancellationToken);
    }

    /// <summary>
    /// 查找对话，并响应调用方的取消请求。
    /// </summary>
    private async Task<ConversationEntity?> FindConversationRowAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope()
            .Queryable<ConversationEntity>()
            .Where(x => x.Id == conversationId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
    }

    /// <summary>
    /// 查找轮次，并响应调用方的取消请求。
    /// </summary>
    private async Task<TurnEntity?> FindTurnRowAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope()
            .Queryable<TurnEntity>()
            .Where(x => x.Id == turnId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
    }

    /// <summary>
    /// 查找执行，并响应调用方的取消请求。
    /// </summary>
    private async Task<RunEntity?> FindRunRowAsync(
        AssistantScopeSnapshot scope,
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope()
            .Queryable<RunEntity>()
            .Where(x => x.Id == runId
                && x.AssistantId == scope.AssistantId
                && x.PublishedVersionId == scope.PublishedVersionId
                && x.ExecutionMode == (int)scope.ExecutionMode)
            .FirstAsync();
    }

    /// <summary>
    /// 读取消息所属轮次并还原访问范围；找不到有效轮次时报告数据不一致。
    /// </summary>
    private async Task<AssistantScopeSnapshot> ScopeForMessageAsync(
        AssistantMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<TurnEntity>()
            .Where(x => x.Id == message.TurnId)
            .FirstAsync();
        return row is null
            ? throw new InvalidOperationException("Message parent turn was not found.")
            : ScopeOf(row);
    }

    /// <summary>
    /// 读取工具调用所属运行并还原访问范围；找不到有效运行时报告数据不一致。
    /// </summary>
    private async Task<AssistantScopeSnapshot> ScopeForToolCallAsync(
        AssistantToolCall toolCall,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<RunEntity>()
            .Where(x => x.Id == toolCall.RunId)
            .FirstAsync();
        return row is null
            ? throw new InvalidOperationException("Tool call parent run was not found.")
            : ScopeOf(row);
    }

    /// <summary>
    /// 物理删除助手运行时，并响应调用方的取消请求。
    /// </summary>
    private Task<int> DeleteAsync<TRow>(AssistantScopeSnapshot scope, string id, CancellationToken cancellationToken)
        where TRow : LifecycleEntity, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _dbContext.GetSugarScope()
            .Deleteable<TRow>()
            .Where(row => row.Id == id
                && row.AssistantId == scope.AssistantId
                && row.PublishedVersionId == scope.PublishedVersionId
                && row.ExecutionMode == (int)scope.ExecutionMode)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static SessionEntity ToRow(AnonymousAssistantSession value) => new()
    {
        Id = value.Id,
        AssistantId = value.AssistantId,
        PublishedVersionId = value.PublishedVersionId,
        SubjectHash = value.SubjectHash,
        Locale = value.Locale,
        ExecutionMode = (int)value.ExecutionMode,
        Status = (int)value.Status,
        LastSeenAt = value.LastSeenAt,
        ExpiresAt = value.ExpiresAt,
        RevokedAt = value.RevokedAt
    };

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static ConversationEntity ToRow(AssistantConversation value) => new()
    {
        Id = value.Id,
        SessionId = value.SessionId,
        AssistantId = value.AssistantId,
        PublishedVersionId = value.PublishedVersionId,
        Locale = value.Locale,
        ExecutionMode = (int)value.ExecutionMode,
        Status = (int)value.Status,
        LastTurnNo = value.LastTurnNo,
        LastActivityAt = value.LastActivityAt
    };

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static TurnEntity ToRow(AssistantTurn value) => new()
    {
        Id = value.Id,
        ConversationId = value.ConversationId,
        SessionId = value.SessionId,
        AssistantId = value.AssistantId,
        PublishedVersionId = value.PublishedVersionId,
        ClientMessageId = value.ClientMessageId,
        RequestHash = value.RequestHash,
        TurnNo = value.TurnNo,
        ExecutionMode = (int)value.ExecutionMode,
        Status = (int)value.Status,
        CompletedAt = value.CompletedAt,
        FailureCode = value.FailureCode
    };

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static RunEntity ToRow(AssistantRun value) => new()
    {
        Id = value.Id,
        TurnId = value.TurnId,
        ConversationId = value.ConversationId,
        SessionId = value.SessionId,
        AssistantId = value.AssistantId,
        PublishedVersionId = value.PublishedVersionId,
        ExecutionMode = (int)value.ExecutionMode,
        ProviderProfileRef = value.ProviderProfileRef,
        Model = value.Model,
        Status = (int)value.Status,
        SequenceHead = value.SequenceHead,
        StartedAt = value.StartedAt,
        CompletedAt = value.CompletedAt,
        FailureCode = value.FailureCode,
        PromptTokens = value.PromptTokens,
        CompletionTokens = value.CompletionTokens,
        TotalTokens = value.TotalTokens
    };

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static MessageEntity ToRow(AssistantMessage value) => new()
    {
        Id = value.Id,
        ConversationId = value.ConversationId,
        TurnId = value.TurnId,
        RunId = value.RunId,
        Role = (int)value.Role,
        Status = (int)value.Status,
        ContentJson = value.ContentJson,
        CompletedAt = value.CompletedAt
    };

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static ToolCallEntity ToRow(AssistantToolCall value) => new()
    {
        Id = value.Id,
        RunId = value.RunId,
        ToolId = value.ToolId,
        ContractVersion = value.ContractVersion,
        InputHash = value.InputHash,
        Status = (int)value.Status,
        OutputJson = value.OutputJson,
        FailureCode = value.FailureCode,
        DurationMs = value.DurationMs,
        CompletedAt = value.CompletedAt
    };

    /// <summary>
    /// 从当前助手运行时记录中提取访问范围，供后续操作执行一致性校验。
    /// </summary>
    private static AssistantScopeSnapshot ScopeOf(AnonymousAssistantSession value)
        => new(value.AssistantId, value.PublishedVersionId, value.ExecutionMode);

    /// <summary>
    /// 从当前助手运行时记录中提取访问范围，供后续操作执行一致性校验。
    /// </summary>
    private static AssistantScopeSnapshot ScopeOf(AssistantConversation value)
        => new(value.AssistantId, value.PublishedVersionId, value.ExecutionMode);

    /// <summary>
    /// 从当前助手运行时记录中提取访问范围，供后续操作执行一致性校验。
    /// </summary>
    private static AssistantScopeSnapshot ScopeOf(AssistantTurn value)
        => new(value.AssistantId, value.PublishedVersionId, value.ExecutionMode);

    /// <summary>
    /// 从当前助手运行时记录中提取访问范围，供后续操作执行一致性校验。
    /// </summary>
    private static AssistantScopeSnapshot ScopeOf(AssistantRun value)
        => new(value.AssistantId, value.PublishedVersionId, value.ExecutionMode);

    /// <summary>
    /// 从当前助手运行时记录中提取访问范围，供后续操作执行一致性校验。
    /// </summary>
    private static AssistantScopeSnapshot ScopeOf(TurnEntity value)
        => new(value.AssistantId, value.PublishedVersionId, (AssistantExecutionMode)value.ExecutionMode);

    /// <summary>
    /// 从当前助手运行时记录中提取访问范围，供后续操作执行一致性校验。
    /// </summary>
    private static AssistantScopeSnapshot ScopeOf(RunEntity value)
        => new(value.AssistantId, value.PublishedVersionId, (AssistantExecutionMode)value.ExecutionMode);

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static AnonymousAssistantSession ToDomain(SessionEntity row)
        => AnonymousAssistantSession.Rehydrate(row.Id, row.AssistantId, row.PublishedVersionId, row.SubjectHash, row.Locale, (AssistantExecutionMode)row.ExecutionMode,
            (AssistantSessionStatus)row.Status, row.LastSeenAt, row.ExpiresAt, row.RevokedAt);

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static AssistantConversation ToDomain(ConversationEntity row)
        => AssistantConversation.Rehydrate(row.Id, row.AssistantId, row.SessionId, row.PublishedVersionId, row.Locale, (AssistantExecutionMode)row.ExecutionMode,
            (AssistantConversationStatus)row.Status, row.LastTurnNo, row.LastActivityAt);

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static AssistantTurn ToDomain(TurnEntity row)
        => AssistantTurn.Rehydrate(row.Id, row.ConversationId, row.SessionId, row.AssistantId, row.PublishedVersionId, row.ClientMessageId, row.RequestHash, row.TurnNo,
            (AssistantExecutionMode)row.ExecutionMode, (AssistantTurnStatus)row.Status, row.CompletedAt, row.FailureCode);

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static AssistantRun ToDomain(RunEntity row)
        => AssistantRun.Rehydrate(row.Id, row.TurnId, row.ConversationId, row.SessionId, row.AssistantId,
            row.PublishedVersionId, (AssistantExecutionMode)row.ExecutionMode,
            row.ProviderProfileRef, row.Model, (AssistantRunStatus)row.Status, row.SequenceHead, row.StartedAt, row.CompletedAt, row.FailureCode, row.PromptTokens, row.CompletionTokens, row.TotalTokens);

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static AssistantMessage ToDomain(MessageEntity row)
        => AssistantMessage.Rehydrate(row.Id, row.ConversationId, row.TurnId, row.RunId,
            (AssistantMessageRole)row.Role, (AssistantMessageStatus)row.Status, row.ContentJson,
            row.CompletedAt);

    /// <summary>
    /// 将数据转换为助手运行时。
    /// </summary>
    private static AssistantToolCall ToDomain(ToolCallEntity row)
        => AssistantToolCall.Rehydrate(row.Id, row.RunId, row.ToolId, row.ContractVersion, row.InputHash,
            (AssistantToolCallStatus)row.Status, row.OutputJson, row.FailureCode, row.CompletedAt, row.DurationMs);
}

/// <summary>
/// 表示运行数据的条件更新未命中预期状态，需要重新读取或报告并发冲突。
/// </summary>
public sealed class AssistantRuntimeConcurrencyException : InvalidOperationException
{
    /// <summary>
    /// 接收并保存助手运行时执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantRuntimeConcurrencyException()
        : base("assistant_runtime_concurrency_conflict") { }
}
