#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Models.AI;
using SmartAssistant.Models.Knowledge;
using SmartAssistant.Models.Policies;
using SmartAssistant.Models.Publication;
using SmartAssistant.Models.Runtime;
using SmartAssistant.Platform;
using SmartAssistant.Services.Contracts;
using SmartAssistant.Services.Domain;
using SmartAssistant.Services.Infrastructure;
using SmartAssistant.Services.Infrastructure.AI;

namespace SmartAssistant.Services.Application;

/// <summary>
/// 编排一轮助手执行的幂等提交、模型调用、工具调用、取消和终态落库。
/// 通过统一事件代理输出运行进度，供 SSE 与 WebSocket 共用。
/// </summary>
public sealed class AssistantRunOrchestrator : IAssistantRuntimeOrchestrator
{
    /// <summary>
    /// 定义流票据允许使用的事件传输方式。
    /// </summary>
    private static readonly IReadOnlySet<AssistantTransportKind> AllowedTransports =
        new HashSet<AssistantTransportKind> { AssistantTransportKind.Sse, AssistantTransportKind.WebSocket };

    /// <summary>
    /// 保存助手公开授权解析器依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IAssistantPublicAuthorityResolver _authority;
    /// <summary>
    /// 保存助手运行时仓储依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IAssistantRuntimeRepository _runtime;
    /// <summary>
    /// 保存助手执行快照服务依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly AssistantExecutionSnapshotService _snapshots;
    /// <summary>
    /// 保存助手策略服务依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IAssistantPolicyService _policies;
    /// <summary>
    /// 保存数据库上下文依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IDbContext _dbContext;
    /// <summary>
    /// 保存聊天模型模型供应商依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IChatModelProvider _provider;
    /// <summary>
    /// 保存执行事件事件代理依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IRunEventBroker _broker;
    /// <summary>
    /// 保存流式输出票据服务依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IStreamTicketService _tickets;
    /// <summary>
    /// 保存助手知识运行时检索器依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IAssistantKnowledgeRuntimeRetriever _knowledgeRetriever;
    /// <summary>
    /// 保存助手工具注册表依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IAssistantToolRegistry _toolRegistry;
    /// <summary>
    /// 保存助手执行编排器在处理日志记录器时所需的数据或状态。
    /// </summary>
    private readonly ILogger<AssistantRunOrchestrator> _logger;
    /// <summary>
    /// 保存服务范围工厂依赖，供助手执行编排器执行相关操作。
    /// </summary>
    private readonly IServiceScopeFactory? _scopeFactory;
    /*
     * 请求与后台依赖注入作用域共用运行取消源；运行进入终态后移除对应记录。
     */
    /// <summary>
    /// 保存当前运行的取消信号源，使请求取消能够传递到后台模型执行。
    /// </summary>
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> RunCancellation = new(StringComparer.Ordinal);

    /// <summary>
    /// 接收并保存助手执行编排器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantRunOrchestrator(
        IAssistantPublicAuthorityResolver authority,
        IAssistantRuntimeRepository runtime,
        IDbContext dbContext,
        IChatModelProvider provider,
        IRunEventBroker broker,
        IStreamTicketService tickets,
        AssistantExecutionSnapshotService snapshots,
        IAssistantPolicyService policies,
        IAssistantKnowledgeRuntimeRetriever knowledgeRetriever,
        IAssistantToolRegistry toolRegistry,
        ILogger<AssistantRunOrchestrator> logger,
        IServiceScopeFactory? scopeFactory = null)
    {
        _authority = authority;
        _runtime = runtime;
        _dbContext = dbContext;
        _provider = provider;
        _broker = broker;
        _tickets = tickets;
        _snapshots = snapshots;
        _policies = policies;
        _knowledgeRetriever = knowledgeRetriever;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// 受理并提交轮次，并响应调用方的取消请求。
    /// </summary>
    public Task<AssistantTurnAccepted> SubmitTurnAsync(
        SubmitAssistantTurnCommand command,
        CancellationToken cancellationToken = default)
        => throw new BizResultException(BusinessCodes.Forbidden, "assistant_session_required");

    /// <summary>
    /// 受理并提交轮次，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantTurnAccepted> SubmitTurnAsync(
        SubmitAssistantTurnCommand command,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var authority = await RequireAuthorityAsync(command.AssistantKey, cancellationToken);
        return await SubmitAuthorizedTurnAsync(command, sessionId, authority, cancellationToken);
    }

    /*
     * 此入口只接收服务端授权解析器生成的范围，供公开运行和管理预览共用。
     */
    /// <summary>
    /// 在已解析的服务端授权范围内校验会话和幂等消息，持久化轮次后启动后台执行并签发流票据。
    /// </summary>
    public async Task<AssistantTurnAccepted> SubmitAuthorizedTurnAsync(
        SubmitAssistantTurnCommand command, string sessionId, AssistantPublicAuthority authority,
        CancellationToken cancellationToken = default)
    {
        var scope = authority.Scope;
        var session = await _runtime.GetSessionAsync(scope, sessionId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_session_not_found");
        session.EnsureAccessible(scope.AssistantId, scope.PublishedVersionId, scope.ExecutionMode, DateTime.UtcNow);
        var conversation = await _runtime.GetConversationAsync(scope, command.ConversationId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_conversation_not_found");
        conversation.EnsureScope(scope, session.Id);

        var existing = await _runtime.GetTurnByClientMessageIdAsync(
            scope, conversation.Id, command.ClientMessageId, cancellationToken);
        if (existing is not null)
        {
            _ = AssistantIdempotency.Evaluate(existing, command.MessageType, command.Text, command.Locale);
            var existingRun = await _runtime.GetRunByTurnIdAsync(scope, existing.Id, cancellationToken)
                ?? throw new BizResultException(BusinessCodes.Conflict, "assistant_turn_run_missing");
            var replayTicket = await _tickets.IssueAsync(
                scope, session.Id, conversation.Id, existing.Id, existingRun.Id,
                AllowedTransports, DateTime.UtcNow.AddMinutes(10), cancellationToken);
            return new AssistantTurnAccepted(existing.Id, existingRun.Id, conversation.Id,
                scope.PublishedVersionId, replayTicket.Value, existingRun.SequenceHead, replayTicket.ExpiresAt);
        }

        var executionSnapshot = AssistantExecutionSnapshotService.Read(authority.Configuration);
        var policy = executionSnapshot.Policy;
        scope = scope with { WebSearchConfigRef = executionSnapshot.WebSearch?.ConfigRef };
        var directAnswer = await _snapshots.FindDirectAnswerAsync(authority, command.Text, cancellationToken);
        var providerProfileRef = directAnswer is null
            ? policy.ProviderProfileRef
            : "custom-qa";
        var model = directAnswer is null
            ? policy.Model
            : "direct-answer";
        AssistantTurn? turn = null;
        AssistantRun? run = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            conversation = attempt == 0
                ? conversation
                : await _runtime.GetConversationAsync(scope, conversation.Id, cancellationToken)
                    ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_conversation_not_found");
            var now = DateTime.UtcNow;
            var turnNo = conversation.LastTurnNo == long.MaxValue ? long.MaxValue : conversation.LastTurnNo + 1;
            turn = AssistantTurn.Create(
                Guid.NewGuid().ToString("N"), conversation.Id, session.Id, scope.AssistantId,
                scope.PublishedVersionId, command.ClientMessageId,
                command.MessageType, command.Text, command.Locale, turnNo, scope.ExecutionMode);
            run = AssistantRun.Create(Guid.NewGuid().ToString("N"), turn, providerProfileRef, model);
            var userMessage = AssistantMessage.CreateUser(Guid.NewGuid().ToString("N"), conversation.Id, turn.Id, command.Text, now);
            var assistantMessage = AssistantMessage.CreateAssistant(Guid.NewGuid().ToString("N"), conversation.Id, turn.Id, run.Id, now);
            var queued = await _runtime.TryCreateTurnBundleAsync(scope, conversation.Id, conversation.LastTurnNo, now,
                turn, run, userMessage, assistantMessage, cancellationToken);
            if (!queued)
            {
                if (conversation.LastTurnNo == long.MaxValue)
                    throw new BizResultException(BusinessCodes.Conflict, "assistant_turn_number_exhausted");
                turn = null;
                run = null;
                continue;
            }
            try
            {
                await _dbContext.CommitAsync(cancellationToken);
                break;
            }
            catch (AssistantRuntimeConcurrencyException) when (attempt < 2)
            {
                turn = null;
                run = null;
            }
        }
        if (turn is null || run is null)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_turn_number_conflict");

        var acceptedSequence = await PublishEventAsync(
            scope, run, AssistantRunEventTypes.RunAccepted,
            new { runId = run.Id, turnId = turn.Id, conversationId = conversation.Id },
            cancellationToken);
        var ticket = await _tickets.IssueAsync(
            scope, session.Id, conversation.Id, turn.Id, run.Id,
            AllowedTransports, DateTime.UtcNow.AddMinutes(10), cancellationToken);

        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        RunCancellation[run.Id] = linkedCancellation;
        if (_scopeFactory is not null)
        {
            _ = Task.Run(async () =>
            {
                await using var backgroundScope = _scopeFactory.CreateAsyncScope();
                var background = backgroundScope.ServiceProvider.GetRequiredService<AssistantRunOrchestrator>();
                await background.ExecuteRunAsync(scope, session.Id, conversation.Id, turn.Id, run.Id,
                    command.Locale, command.Text, directAnswer, policy, executionSnapshot, linkedCancellation.Token);
            }, CancellationToken.None);
        }
        else
        {
            _ = ExecuteRunAsync(scope, session.Id, conversation.Id, turn.Id, run.Id,
                command.Locale, command.Text, directAnswer, policy, executionSnapshot, linkedCancellation.Token);
        }

        return new AssistantTurnAccepted(
            turn.Id, run.Id, conversation.Id, scope.PublishedVersionId,
            ticket.Value, acceptedSequence, ticket.ExpiresAt);
    }

    /// <summary>
    /// 重新校验公开范围并尝试持久化取消终态，随后停止模型执行并发布取消事件。
    /// </summary>
    public async Task CancelRunAsync(
        string assistantKey,
        string runId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var authority = await RequireAuthorityAsync(assistantKey, cancellationToken);
        var scope = authority.Scope;
        var session = await _runtime.GetSessionAsync(scope, sessionId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_session_not_found");
        session.EnsureAccessible(scope.AssistantId, scope.PublishedVersionId, scope.ExecutionMode, DateTime.UtcNow);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var run = await _runtime.GetRunAsync(scope, runId, cancellationToken)
                ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_run_not_found");
            run.EnsureScope(scope, run.TurnId, run.ConversationId, session.Id);
            if (run.Status is AssistantRunStatus.Completed or AssistantRunStatus.Failed
                or AssistantRunStatus.Cancelled or AssistantRunStatus.Expired)
                return;

            var turn = await _runtime.GetTurnAsync(scope, run.TurnId, cancellationToken)
                ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_turn_not_found");
            var messages = await _runtime.ListMessagesByTurnAsync(scope, turn.Id, cancellationToken);
            var assistantMessage = messages.FirstOrDefault(item => item.Role == AssistantMessageRole.Assistant);
            var expectedRunStatus = run.Status;
            var expectedTurnStatus = turn.Status;
            var expectedMessageStatus = assistantMessage?.Status;
            var now = DateTime.UtcNow;
            try
            {
                /*
                 * 运行和轮次分两次读取，期间可能有其他终态事务提交。
                 * 判断取消是否成功前重新读取运行，避免依据不一致快照作出判断。
                 */
                if (turn.Status is AssistantTurnStatus.Completed or AssistantTurnStatus.Failed
                    or AssistantTurnStatus.Cancelled or AssistantTurnStatus.Expired)
                    throw new AssistantRuntimeConcurrencyException();
                run.Cancel(now);
                turn.Cancel(now);
                if (assistantMessage is not null && assistantMessage.Status == AssistantMessageStatus.Streaming)
                    assistantMessage.Fail(now);
                if (!await _runtime.TryPersistTerminalBundleAsync(scope, run, expectedRunStatus, turn, expectedTurnStatus,
                    assistantMessage, expectedMessageStatus, cancellationToken))
                    throw new BizResultException(BusinessCodes.NotFound, "assistant_run_not_found");
                await _dbContext.CommitAsync(cancellationToken);
            }
            catch (AssistantRuntimeConcurrencyException) when (attempt < 2)
            {
                /*
                 * 后台执行可能已将状态从 Accepted 推进到 Running。
                 * 重新读取后才能判断取消或其他终态是否已经生效。
                 */
                continue;
            }

            /*
             * 先提交取消终态，使后台清理读取到已落库的结果，避免再次争抢写入。
             * 随后立即停止模型调用，即使事件投递存在延迟也不继续消耗上游资源。
             */
            if (RunCancellation.TryGetValue(run.Id, out var cts))
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { /* The worker already finished. */ }
            }
            await PublishEventAsync(scope, run, AssistantRunEventTypes.RunCancelled,
                new { runId = run.Id, turnId = turn.Id, reason = "cancelled" }, CancellationToken.None);
            return;
        }
    }

    /// <summary>
    /// 执行已受理的运行，处理直接问答、模型流、工具调用、用量及终态事件。
    /// </summary>
    public async Task ExecuteRunAsync(
        AssistantScopeSnapshot scope,
        string sessionId,
        string conversationId,
        string turnId,
        string runId,
        string locale,
        string text,
        string? directAnswer,
        AssistantResolvedPolicy policy,
        AssistantExecutionSnapshot executionSnapshot,
        CancellationToken cancellationToken)
    {
        var stage = "load_run";
        try
        {
            var run = await _runtime.GetRunAsync(scope, runId, cancellationToken)
                ?? throw new InvalidOperationException("assistant_run_not_found");
            var turn = await _runtime.GetTurnAsync(scope, turnId, cancellationToken)
                ?? throw new InvalidOperationException("assistant_turn_not_found");
            var messages = await _runtime.ListMessagesByTurnAsync(scope, turn.Id, cancellationToken);
            var assistantMessage = messages.FirstOrDefault(item => item.Role == AssistantMessageRole.Assistant)
                ?? throw new InvalidOperationException("assistant_message_not_found");
            if (run.Status is AssistantRunStatus.Completed or AssistantRunStatus.Failed
                or AssistantRunStatus.Cancelled or AssistantRunStatus.Expired)
                return;

            stage = "start_run";
            var now = DateTime.UtcNow;
            run.Start(now);
            turn.Start();
            await _runtime.TryStartRunBundleAsync(scope, run, turn, cancellationToken);
            try
            {
                await _dbContext.CommitAsync(cancellationToken);
            }
            catch (AssistantRuntimeConcurrencyException)
            {
                return;
            }
            await PublishEventAsync(scope, run, AssistantRunEventTypes.RunStarted,
                new { runId, turnId }, cancellationToken);

            stage = "load_history";
            var history = await BuildHistoryAsync(scope, conversationId, turn.TurnNo, cancellationToken);
            if (directAnswer is not null)
            {
                stage = "complete_direct_answer";
                await CompleteDirectAnswerAsync(scope, run, turn, assistantMessage, directAnswer, cancellationToken);
                return;
            }
            stage = "check_policy";
            await _policies.EnsureAvailableAsync(policy, cancellationToken);
            stage = "retrieve_knowledge";
            var evidence = policy.KnowledgeEnabled
                ? await _knowledgeRetriever.RetrieveAsync(
                    scope, executionSnapshot, text, policy.RetrievalLimit, cancellationToken)
                : Array.Empty<AssistantKnowledgeRuntimeMatch>();
            if (executionSnapshot.Documents.Count > 0
                && policy.KnowledgeEnabled
                && policy.RequireEvidence
                && evidence.Count == 0)
            {
                await CompleteDirectAnswerAsync(
                    scope, run, turn, assistantMessage, policy.NoAnswerMessage, cancellationToken,
                    source: "knowledge_no_answer");
                return;
            }
            var instructions = AssistantRunInstructionComposer.Compose(
                policy.Instructions,
                evidence,
                executionSnapshot.WebSearch is not null);
            var providerMessages = history.Append(new AssistantProviderMessage(AssistantMessageRole.User, text)).ToList();
            var frozenTools = executionSnapshot.Tools ?? Array.Empty<AssistantFrozenTool>();
            var toolDefinitions = frozenTools.Select(item => new AssistantToolDefinition(
                item.ToolId, item.ContractVersion, item.Name, item.Description, item.InputSchema)).ToArray();
            var textBuffer = new StringBuilder();
            for (var toolRound = 0; toolRound < 4; toolRound++)
            {
                stage = "provider_stream";
                var request = new AssistantProviderRequest(
                    run.ProviderProfileRef,
                    run.Model,
                    providerMessages,
                    Temperature: policy.Temperature,
                    MaxTokens: policy.MaxOutputTokens,
                    Tools: toolDefinitions.Length == 0 ? null : toolDefinitions,
                    ToolChoice: toolDefinitions.Length == 0 ? null : "auto",
                    ParallelToolCalls: toolDefinitions.Length == 0 ? null : false,
                    Instructions: instructions);
                var assembler = new AssistantProviderToolCallAssembler();
                var providerCompleted = false;
                await foreach (var chunk in _provider.StreamAsync(request, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (chunk.Kind)
                    {
                        case AssistantProviderChunkKind.TextDelta when !string.IsNullOrEmpty(chunk.TextDelta):
                            textBuffer.Append(chunk.TextDelta);
                            assistantMessage.AppendAssistantText(chunk.TextDelta);
                            await _runtime.SaveMessageAsync(assistantMessage, cancellationToken);
                            await _dbContext.CommitAsync(cancellationToken);
                            await PublishEventAsync(scope, run, AssistantRunEventTypes.MessageDelta,
                                new { runId, turnId, text = chunk.TextDelta }, cancellationToken);
                            break;
                        case AssistantProviderChunkKind.ToolCallDelta when chunk.ToolCall is not null:
                            assembler.Append(chunk.ToolCall);
                            break;
                        case AssistantProviderChunkKind.Usage when chunk.Usage is not null:
                            run.RecordUsage(chunk.Usage);
                            await _runtime.SaveRunAsync(run, cancellationToken);
                            await _dbContext.CommitAsync(cancellationToken);
                            break;
                        case AssistantProviderChunkKind.Error:
                            await FailRunAsync(scope, run, turn, assistantMessage,
                                chunk.Error?.Code ?? "assistant_provider_error", cancellationToken);
                            return;
                        case AssistantProviderChunkKind.Completed:
                            /*
                             * OpenAI 兼容接口可能在 finish_reason 之后才返回用量，先消费完流再汇总。
                             */
                            providerCompleted = true;
                            break;
                    }
                }

                if (!providerCompleted)
                {
                    await FailRunAsync(scope, run, turn, assistantMessage,
                        "assistant_provider_stream_incomplete", cancellationToken);
                    return;
                }

                IReadOnlyList<AssistantAssembledToolCall> calls;
                try
                {
                    calls = assembler.CompleteAll();
                }
                catch (BizResultException exception)
                {
                    await FailRunAsync(scope, run, turn, assistantMessage, exception.BusinessMessage, cancellationToken);
                    return;
                }
                if (calls.Count == 0)
                {
                    stage = "complete_run";
                    await CompleteRunAsync(scope, run, turn, assistantMessage, textBuffer.ToString(), evidence, cancellationToken);
                    return;
                }

                var assistantCalls = calls.Select(call => new AssistantProviderToolCall(
                    call.ToolCallId, call.Name, call.ArgumentsJson)).ToArray();
                providerMessages.Add(new AssistantProviderMessage(
                    AssistantMessageRole.Assistant, string.Empty, ToolCalls: assistantCalls));
                var assistantToolMessage = AssistantMessage.CreateAssistantToolCalls(
                    Guid.NewGuid().ToString("N"), turn.ConversationId, turn.Id, run.Id,
                    assistantCalls, DateTime.UtcNow);
                await _runtime.AddMessageAsync(assistantToolMessage, cancellationToken);
                await _dbContext.CommitAsync(cancellationToken);
                foreach (var call in calls)
                {
                    stage = "execute_tool";
                    var definition = frozenTools.FirstOrDefault(item =>
                        string.Equals(item.Name, call.Name, StringComparison.Ordinal)
                        || string.Equals(item.ToolId, call.Name, StringComparison.Ordinal));
                    if (definition is null)
                    {
                        var deniedDefinition = new AssistantFrozenTool(
                            call.Name, "v1", call.Name, "", JsonSerializer.SerializeToElement(new { }));
                        var denied = await ExecuteToolCallAsync(
                            scope, run, turn, deniedDefinition, call, cancellationToken);
                        providerMessages.Add(new AssistantProviderMessage(
                            AssistantMessageRole.Tool, denied.OutputJson, ToolCallId: call.ToolCallId));
                        continue;
                    }

                    var toolResult = await ExecuteToolCallAsync(scope, run, turn, definition, call, cancellationToken);
                    providerMessages.Add(new AssistantProviderMessage(
                        AssistantMessageRole.Tool,
                        toolResult.OutputJson,
                        ToolCallId: call.ToolCallId));
                }
            }

            await FailRunAsync(scope, run, turn, assistantMessage,
                "assistant_tool_call_round_limit", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryCancelAfterCancellationAsync(scope, turnId, runId, cancellationToken);
        }
        catch (RunAlreadyCancelledException)
        {
            /*
             * 其他请求或进程已经负责写入取消终态与事件；释放供应商流即可停止当前执行。
             */
        }
        catch (Exception exception)
        {
            _logger.LogError("Assistant run {RunId} failed at {Stage}; exception type {ExceptionType}",
                runId, stage, exception.GetType().FullName);
            await TryFailAfterExceptionAsync(scope, turnId, runId, CancellationToken.None);
        }
        finally
        {
            RunCancellation.TryRemove(runId, out var cts);
            cts?.Dispose();
        }
    }

    /// <summary>
    /// 原子保存运行、轮次和助手消息的完成状态及模型用量，再发布终态事件。
    /// </summary>
    private async Task CompleteRunAsync(
        AssistantScopeSnapshot scope,
        AssistantRun run,
        AssistantTurn turn,
        AssistantMessage assistantMessage,
        string text,
        IReadOnlyList<AssistantKnowledgeRuntimeMatch> evidence,
        CancellationToken cancellationToken)
    {
        var expectedRunStatus = run.Status;
        var expectedTurnStatus = turn.Status;
        var expectedMessageStatus = assistantMessage.Status;
        var now = DateTime.UtcNow;
        assistantMessage.Complete(JsonSerializer.Serialize(new
        {
            type = "text",
            text,
            source = evidence.Count > 0 ? "knowledge" : "model",
            citations = evidence.Select(item => new
            {
                segmentId = item.SegmentId,
                documentId = item.DocumentId,
                documentName = item.DocumentName,
                segmentIndex = item.SegmentIndex,
                relevance = item.Relevance
            }).ToArray()
        }), now);
        run.Complete(now);
        turn.Complete(now);
        await _runtime.TryPersistTerminalBundleAsync(scope, run, expectedRunStatus, turn, expectedTurnStatus,
            assistantMessage, expectedMessageStatus, cancellationToken);
        try
        {
            await _dbContext.CommitAsync(cancellationToken);
        }
        catch (AssistantRuntimeConcurrencyException)
        {
            return;
        }
        await PublishEventAsync(scope, run, AssistantRunEventTypes.MessageCompleted,
            new
            {
                runId = run.Id,
                turnId = turn.Id,
                text,
                source = evidence.Count > 0 ? "knowledge" : "model",
                citations = evidence.Select(item => new { item.SegmentId, item.DocumentId, item.DocumentName, item.SegmentIndex, item.Relevance }).ToArray()
            }, cancellationToken);
        await PublishEventAsync(scope, run, AssistantRunEventTypes.RunCompleted,
            new
            {
                runId = run.Id,
                turnId = turn.Id,
                source = evidence.Count > 0 ? "knowledge" : "model",
                citations = evidence.Select(item => new { item.SegmentId, item.DocumentId, item.DocumentName, item.SegmentIndex, item.Relevance }).ToArray(),
                usage = new { run.PromptTokens, run.CompletionTokens, run.TotalTokens }
            }, cancellationToken);
    }

    /// <summary>
    /// 按已冻结的工具定义解析模型参数、校验能力并执行处理器。
    /// </summary>
    private async Task<AssistantToolExecutionResult> ExecuteToolCallAsync(
        AssistantScopeSnapshot scope,
        AssistantRun run,
        AssistantTurn turn,
        AssistantFrozenTool definition,
        AssistantAssembledToolCall call,
        CancellationToken cancellationToken)
    {
        var toolTimer = System.Diagnostics.Stopwatch.StartNew();
        var inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(call.ArgumentsJson)))
            .ToLowerInvariant();
        var toolCall = AssistantToolCall.Create(
            Guid.NewGuid().ToString("N"), run.Id, definition.ToolId, definition.ContractVersion,
            inputHash);
        toolCall.Start();
        await _runtime.AddToolCallAsync(toolCall, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
        await PublishEventAsync(scope, run, AssistantRunEventTypes.ToolCallStarted,
            new
            {
                runId = run.Id,
                turnId = turn.Id,
                toolCallId = call.ToolCallId,
                toolId = definition.ToolId,
                contractVersion = definition.ContractVersion
            }, cancellationToken);

        var handler = await _toolRegistry.ResolveHandlerAsync(
            definition.ToolId, definition.ContractVersion, cancellationToken);
        if (handler is null)
        {
            toolCall.Deny("assistant_tool_handler_unavailable", DateTime.UtcNow);
            toolCall.RecordDuration(toolTimer.Elapsed);
            await _runtime.SaveToolCallAsync(toolCall, cancellationToken);
            await _dbContext.CommitAsync(cancellationToken);
            await PublishEventAsync(scope, run, AssistantRunEventTypes.ToolCallCompleted,
                new { runId = run.Id, turnId = turn.Id, toolCallId = call.ToolCallId, toolId = definition.ToolId, status = "denied", code = "assistant_tool_handler_unavailable" }, cancellationToken);
            return new AssistantToolExecutionResult(JsonSerializer.Serialize(new { error = "tool_unavailable" }), false);
        }

        try
        {
            using var inputDocument = JsonDocument.Parse(call.ArgumentsJson);
            var output = await handler.ExecuteAsync(inputDocument.RootElement.Clone(), scope, cancellationToken);
            var outputJson = output.GetRawText();
            toolCall.Complete(outputJson, DateTime.UtcNow);
            toolCall.RecordDuration(toolTimer.Elapsed);
            await _runtime.SaveToolCallAsync(toolCall, cancellationToken);
            await _dbContext.CommitAsync(cancellationToken);
            var toolMessage = AssistantMessage.CreateTool(
                Guid.NewGuid().ToString("N"), turn.ConversationId, turn.Id, run.Id,
                JsonSerializer.Serialize(new { type = "tool_result", toolCallId = call.ToolCallId, output }),
                DateTime.UtcNow);
            await _runtime.AddMessageAsync(toolMessage, cancellationToken);
            await _dbContext.CommitAsync(cancellationToken);
            await PublishEventAsync(scope, run, AssistantRunEventTypes.ToolCallCompleted,
                new { runId = run.Id, turnId = turn.Id, toolCallId = call.ToolCallId, toolId = definition.ToolId, status = "completed" }, cancellationToken);
            return new AssistantToolExecutionResult(outputJson, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failureCode = exception is BizResultException biz
                ? SanitizeFailureCode(biz.BusinessMessage)
                : "assistant_tool_execution_failed";
            if (toolCall.Status is AssistantToolCallStatus.Running)
                toolCall.Fail(failureCode, DateTime.UtcNow);
            toolCall.RecordDuration(toolTimer.Elapsed);
            await _runtime.SaveToolCallAsync(toolCall, CancellationToken.None);
            await _dbContext.CommitAsync(CancellationToken.None);
            await PublishEventAsync(scope, run, AssistantRunEventTypes.ToolCallCompleted,
                new { runId = run.Id, turnId = turn.Id, toolCallId = call.ToolCallId, toolId = definition.ToolId, status = "failed", code = failureCode }, CancellationToken.None);
            return new AssistantToolExecutionResult(
                JsonSerializer.Serialize(new { error = "tool_execution_failed" }), false);
        }
    }

    /// <summary>
    /// 持久化运行失败状态与稳定错误码，并向订阅者发布失败事件。
    /// </summary>
    private async Task FailRunAsync(
        AssistantScopeSnapshot scope,
        AssistantRun run,
        AssistantTurn turn,
        AssistantMessage assistantMessage,
        string code,
        CancellationToken cancellationToken)
    {
        var expectedRunStatus = run.Status;
        var expectedTurnStatus = turn.Status;
        var expectedMessageStatus = assistantMessage.Status;
        var now = DateTime.UtcNow;
        if (assistantMessage.Status == AssistantMessageStatus.Streaming)
            assistantMessage.Fail(now);
        if (run.Status is not (AssistantRunStatus.Completed or AssistantRunStatus.Failed or AssistantRunStatus.Cancelled or AssistantRunStatus.Expired))
            run.Fail(SanitizeFailureCode(code), now);
        if (turn.Status is not (AssistantTurnStatus.Completed or AssistantTurnStatus.Failed or AssistantTurnStatus.Cancelled or AssistantTurnStatus.Expired))
            turn.Fail(SanitizeFailureCode(code), now);
        await _runtime.TryPersistTerminalBundleAsync(scope, run, expectedRunStatus, turn, expectedTurnStatus,
            assistantMessage, expectedMessageStatus, cancellationToken);
        try
        {
            await _dbContext.CommitAsync(cancellationToken);
        }
        catch (AssistantRuntimeConcurrencyException)
        {
            return;
        }
        await PublishEventAsync(scope, run, AssistantRunEventTypes.RunFailed,
            new { runId = run.Id, turnId = turn.Id, code = SanitizeFailureCode(code) }, cancellationToken);
    }

    /// <summary>
    /// 先预留持久事件序号再发布事件，并保留重试时使用的同一事件信封。
    /// </summary>
    private async Task<long> PublishEventAsync<TPayload>(
        AssistantScopeSnapshot scope,
        AssistantRun run,
        string type,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        /*
         * 终态提交后，即使请求或供应商令牌被取消，也必须继续投递对应事件。
         * 尚未预留事件序号时，其他工作仍可响应取消。
         */
        var waitToken = run.Status is AssistantRunStatus.Completed or AssistantRunStatus.Failed
            or AssistantRunStatus.Cancelled or AssistantRunStatus.Expired
            ? CancellationToken.None : cancellationToken;
        long? next = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            waitToken.ThrowIfCancellationRequested();
            var current = await _runtime.GetRunAsync(scope, run.Id, waitToken) ?? run;
            if (current.Status == AssistantRunStatus.Cancelled && type != AssistantRunEventTypes.RunCancelled)
                throw new RunAlreadyCancelledException();
            next = await _runtime.ReserveRunSequenceAsync(scope, run.Id, current.SequenceHead, CancellationToken.None);
            if (next is not null) break;
        }
        if (next is null)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_run_sequence_conflict");
        var envelope = AssistantRunEventEnvelope.Create(
            $"{run.Id}:{next.Value}:{type}", run.Id, run.TurnId, next.Value, type, DateTime.UtcNow, payload);
        /*
         * 序号预留与事件发布构成同一个取消边界。
         * 已预留的事件不能丢弃，否则事件代理会因序号缺口拒绝后续事件。
         */
        var deliveryStarted = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                await _broker.PublishAsync(envelope, CancellationToken.None);
                return next.Value;
            }
            catch (BizResultException exception) when (
                exception.BusinessMessage == "assistant_event_sequence_gap"
                && Stopwatch.GetElapsedTime(deliveryStarted) < TimeSpan.FromSeconds(5))
            {
                /*
                 * 其他请求或进程可能尚未发布前一个已预留事件。
                 * 重试当前同一事件信封，不再申请新序号；持续的回放损坏仍需报告错误。
                 */
                await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// 标记运行已被取消，使后台执行停止后续状态推进。
    /// </summary>
    private sealed class RunAlreadyCancelledException : Exception;

    /// <summary>
    /// 使用条件更新为对话预留下一轮编号，遇到并发修改时重新读取并重试。
    /// </summary>
    private async Task<long> ReserveTurnNoAsync(
        AssistantScopeSnapshot scope,
        AssistantConversation conversation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var next = await _runtime.ReserveConversationTurnNoAsync(
                scope, conversation.Id, conversation.LastTurnNo, DateTime.UtcNow, cancellationToken);
            if (next is not null) return next.Value;
            var latest = await _runtime.GetConversationAsync(scope, conversation.Id, cancellationToken)
                ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_conversation_not_found");
            conversation = latest;
        }
        throw new BizResultException(BusinessCodes.Conflict, "assistant_turn_number_conflict");
    }

    /// <summary>
    /// 将当前对话先前轮次整理为模型所需上下文，并排除失败或未完成内容。
    /// </summary>
    private async Task<IReadOnlyList<AssistantProviderMessage>> BuildHistoryAsync(
        AssistantScopeSnapshot scope,
        string conversationId,
        long currentTurnNo,
        CancellationToken cancellationToken)
    {
        var history = new List<AssistantProviderMessage>();
        for (var number = 1L; number < currentTurnNo; number++)
        {
            var turn = await _runtime.GetTurnByNumberAsync(scope, conversationId, number, cancellationToken);
            if (turn is null) continue;
            var messages = await _runtime.ListMessagesByTurnAsync(scope, turn.Id, cancellationToken);
            foreach (var message in messages.Where(item => item.Status == AssistantMessageStatus.Completed))
            {
                var providerMessage = ToProviderMessage(message);
                if (providerMessage is not null)
                    history.Add(providerMessage);
            }
        }
        return history;
    }

    /// <summary>
    /// 把已持久化的对话消息转换为供应商协议中的消息结构。
    /// </summary>
    private static AssistantProviderMessage? ToProviderMessage(AssistantMessage message)
    {
        try
        {
            using var document = JsonDocument.Parse(message.ContentJson);
            var root = document.RootElement;
            if (message.Role == AssistantMessageRole.Assistant
                && root.TryGetProperty("toolCalls", out var toolCalls)
                && toolCalls.ValueKind == JsonValueKind.Array)
            {
                var calls = toolCalls.EnumerateArray()
                    .Select(item => new AssistantProviderToolCall(
                        item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                        item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                        item.TryGetProperty("arguments", out var arguments) ? arguments.GetRawText() : "{}"))
                    .Where(item => !string.IsNullOrWhiteSpace(item.ToolCallId) && !string.IsNullOrWhiteSpace(item.Name))
                    .ToArray();
                return calls.Length == 0
                    ? null
                    : new AssistantProviderMessage(message.Role, string.Empty, ToolCalls: calls);
            }

            if (message.Role == AssistantMessageRole.Tool
                && root.TryGetProperty("output", out var output))
            {
                var toolCallId = root.TryGetProperty("toolCallId", out var id)
                    ? id.GetString()
                    : null;
                return new AssistantProviderMessage(
                    AssistantMessageRole.Tool,
                    output.GetRawText(),
                    ToolCallId: toolCallId);
            }

            var text = ExtractText(message.ContentJson);
            return string.IsNullOrWhiteSpace(text) ? null : new AssistantProviderMessage(message.Role, text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 在供应商或调用方取消后尝试写入取消终态，并保持终态只提交一次。
    /// </summary>
    private async Task TryCancelAfterCancellationAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        string runId,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await _runtime.GetRunAsync(scope, runId, CancellationToken.None);
            var turn = await _runtime.GetTurnAsync(scope, turnId, CancellationToken.None);
            if (run is null || turn is null || run.Status is AssistantRunStatus.Completed or AssistantRunStatus.Failed or AssistantRunStatus.Cancelled or AssistantRunStatus.Expired)
                return;
            var messages = await _runtime.ListMessagesByTurnAsync(scope, turn.Id, CancellationToken.None);
            var assistantMessage = messages.FirstOrDefault(item => item.Role == AssistantMessageRole.Assistant);
            var expectedRunStatus = run.Status;
            var expectedTurnStatus = turn.Status;
            var expectedMessageStatus = assistantMessage?.Status;
            run.Cancel(DateTime.UtcNow);
            turn.Cancel(DateTime.UtcNow);
            if (assistantMessage is not null && assistantMessage.Status == AssistantMessageStatus.Streaming)
                assistantMessage.Fail(DateTime.UtcNow);
            await _runtime.TryPersistTerminalBundleAsync(scope, run, expectedRunStatus, turn, expectedTurnStatus,
                assistantMessage, expectedMessageStatus, CancellationToken.None);
            await _dbContext.CommitAsync(CancellationToken.None);
            await PublishEventAsync(scope, run, AssistantRunEventTypes.RunCancelled,
                new { runId, turnId, reason = "cancelled" }, CancellationToken.None);
        }
        catch
        {
            /*
             * 运行状态已有条件更新和终态转换保护，后台清理失败不能泄露到公开传输响应。
             */
        }
    }

    /// <summary>
    /// 在运行异常后尝试写入稳定失败状态，避免覆盖已有取消或成功终态。
    /// </summary>
    private async Task TryFailAfterExceptionAsync(
        AssistantScopeSnapshot scope,
        string turnId,
        string runId,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await _runtime.GetRunAsync(scope, runId, cancellationToken);
            var turn = await _runtime.GetTurnAsync(scope, turnId, cancellationToken);
            if (run is null || turn is null
                || run.Status is AssistantRunStatus.Completed
                    or AssistantRunStatus.Failed
                    or AssistantRunStatus.Cancelled
                    or AssistantRunStatus.Expired)
                return;
            var messages = await _runtime.ListMessagesByTurnAsync(scope, turn.Id, cancellationToken);
            var assistantMessage = messages.FirstOrDefault(item => item.Role == AssistantMessageRole.Assistant);
            if (assistantMessage is null) return;
            await FailRunAsync(scope, run, turn, assistantMessage, "assistant_runtime_failed", cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError("Assistant run {RunId} failure handling failed; exception type {ExceptionType}",
                runId, exception.GetType().FullName);
        }
    }

    /// <summary>
    /// 解析并确认公开助手当前存在有效发布版本，否则拒绝访问。
    /// </summary>
    private async Task<AssistantPublicAuthority> RequireAuthorityAsync(string assistantKey, CancellationToken cancellationToken)
        => await _authority.ResolveAsync(assistantKey, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");

    /// <summary>
    /// 为固定问答直接建立完成消息和运行终态，无需调用模型供应商。
    /// </summary>
    private async Task CompleteDirectAnswerAsync(
        AssistantScopeSnapshot scope,
        AssistantRun run,
        AssistantTurn turn,
        AssistantMessage assistantMessage,
        string answer,
        CancellationToken cancellationToken,
        string source = "custom_qa")
    {
        var expectedRunStatus = run.Status;
        var expectedTurnStatus = turn.Status;
        var expectedMessageStatus = assistantMessage.Status;
        var now = DateTime.UtcNow;
        assistantMessage.Complete(JsonSerializer.Serialize(new { type = "text", text = answer, source }), now);
        run.Complete(now);
        turn.Complete(now);
        await _runtime.TryPersistTerminalBundleAsync(scope, run, expectedRunStatus, turn, expectedTurnStatus,
            assistantMessage, expectedMessageStatus, cancellationToken);
        try
        {
            await _dbContext.CommitAsync(cancellationToken);
        }
        catch (AssistantRuntimeConcurrencyException)
        {
            return;
        }
        await PublishEventAsync(scope, run, AssistantRunEventTypes.MessageCompleted,
            new { runId = run.Id, turnId = turn.Id, text = answer, source }, cancellationToken);
        await PublishEventAsync(scope, run, AssistantRunEventTypes.RunCompleted,
            new { runId = run.Id, turnId = turn.Id, source, providerInvoked = false }, cancellationToken);
    }

    /// <summary>
    /// 从模型输出的 JSON 内容中提取正文文本，并忽略非文本结构。
    /// </summary>
    private static string ExtractText(string contentJson)
    {
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            return document.RootElement.TryGetProperty("text", out var text)
                ? text.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 把内部异常归一化为可公开的稳定错误码，并过滤敏感细节。
    /// </summary>
    private static string SanitizeFailureCode(string code)
        => string.IsNullOrWhiteSpace(code) ? "assistant_runtime_failed" : code.Trim()[..Math.Min(code.Trim().Length, 80)];
}
