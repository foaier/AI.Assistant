#nullable enable
using System.Text.Json;
using AI.Assistant.Extensions.DAL;
using AI.Assistant.Models.Publication;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Infrastructure;
using AI.Assistant.Services.Contracts;
using AI.Assistant.Services.Domain;
using AI.Assistant.Services.Repositorys;

namespace AI.Assistant.Services.Application;

/// <summary>
/// 定义公开聊天的初始化、匿名会话、对话、消息和流式授权操作。
/// </summary>
public interface IAssistantPublicRuntimeService
{
    /// <summary>
    /// 读取公开助手的有效展示配置与能力信息。
    /// </summary>
    Task<AssistantPublicBootstrap> GetBootstrapAsync(string assistantKey, string locale, CancellationToken cancellationToken);
    /// <summary>
    /// 验证公开助手可用后创建匿名会话，并返回后续访问所需的 Bearer 令牌。
    /// </summary>
    Task<AssistantSessionCreated> CreateSessionAsync(CreateAnonymousAssistantSessionCommand command, CancellationToken cancellationToken);
    /// <summary>
    /// 校验匿名会话令牌和发布范围后创建对话。
    /// </summary>
    Task<AssistantConversationAccepted> CreateConversationAsync(CreateAssistantConversationCommand command, string sessionId, string bearerToken, CancellationToken cancellationToken);
    /// <summary>
    /// 校验会话、令牌与对话归属后，将用户消息提交给运行编排器。
    /// </summary>
    Task<AssistantTurnAccepted> SubmitTurnAsync(SubmitAssistantTurnCommand command, string sessionId, string bearerToken, CancellationToken cancellationToken);
    /// <summary>
    /// 校验当前匿名访问范围后请求取消运行，并返回运行结果状态。
    /// </summary>
    Task<AssistantCancelResult> CancelRunAsync(string assistantKey, string runId, string sessionId, string bearerToken, CancellationToken cancellationToken);
    /// <summary>
    /// 校验会话权限后按稳定顺序分页读取对话消息。
    /// </summary>
    Task<AssistantMessagesPage> GetMessagesAsync(string assistantKey, string conversationId, string sessionId, string bearerToken, int limit, string? cursor, CancellationToken cancellationToken);
    /// <summary>
    /// 同时验证会话令牌、运行归属、票据有效期和传输类型。
    /// </summary>
    Task<AssistantStreamTicket> ValidateStreamTicketAsync(string assistantKey, string conversationId, string sessionId, string bearerToken, string runId, string ticket, AssistantTransportKind transport, CancellationToken cancellationToken);
}

/// <summary>
/// 定义已授权会话的轮次提交与运行取消入口。
/// </summary>
public interface IAssistantRuntimeOrchestrator : IAssistantOrchestrator
{
    /// <summary>
    /// 受理并提交轮次，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantTurnAccepted> SubmitTurnAsync(SubmitAssistantTurnCommand command, string sessionId, CancellationToken cancellationToken);
    /// <summary>
    /// 请求取消指定运行，并将取消信号传递到正在执行的模型调用。
    /// </summary>
    new Task CancelRunAsync(string assistantKey, string runId, string sessionId, CancellationToken cancellationToken);
}

/// <summary>
/// 校验公开版本、匿名会话和访问令牌后执行聊天操作。
/// SSE 与 WebSocket 订阅还需校验绑定到具体运行的流票据。
/// </summary>
public sealed class AssistantPublicRuntimeService : IAssistantPublicRuntimeService
{
    /// <summary>
    /// 定义助手公开运行时服务使用的会话有效期配置值。
    /// </summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    /// <summary>
    /// 定义助手公开运行时服务使用的流式输出票据有效期配置值。
    /// </summary>
    private static readonly TimeSpan StreamTicketLifetime = TimeSpan.FromMinutes(10);
    /// <summary>
    /// 定义流票据允许使用的事件传输方式。
    /// </summary>
    private static readonly IReadOnlySet<AssistantTransportKind> AllowedTransports =
        new HashSet<AssistantTransportKind> { AssistantTransportKind.Sse, AssistantTransportKind.WebSocket };

    /// <summary>
    /// 保存助手公开授权解析器依赖，供助手公开运行时服务执行相关操作。
    /// </summary>
    private readonly IAssistantPublicAuthorityResolver _authority;
    /// <summary>
    /// 保存助手运行时仓储依赖，供助手公开运行时服务执行相关操作。
    /// </summary>
    private readonly IAssistantRuntimeRepository _runtime;
    /// <summary>
    /// 保存助手运行时编排器依赖，供助手公开运行时服务执行相关操作。
    /// </summary>
    private readonly IAssistantRuntimeOrchestrator _orchestrator;
    /// <summary>
    /// 保存流式输出票据服务依赖，供助手公开运行时服务执行相关操作。
    /// </summary>
    private readonly IStreamTicketService _tickets;
    /// <summary>
    /// 保存数据库上下文依赖，供助手公开运行时服务执行相关操作。
    /// </summary>
    private readonly IDbContext _dbContext;

    /// <summary>
    /// 接收并保存助手公开运行时服务执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantPublicRuntimeService(
        IAssistantPublicAuthorityResolver authority,
        IAssistantRuntimeRepository runtime,
        IAssistantRuntimeOrchestrator orchestrator,
        IStreamTicketService tickets,
        IDbContext dbContext)
    {
        _authority = authority;
        _runtime = runtime;
        _orchestrator = orchestrator;
        _tickets = tickets;
        _dbContext = dbContext;
    }

    /// <summary>
    /// 读取公开助手的有效展示配置与能力信息。
    /// </summary>
    public async Task<AssistantPublicBootstrap> GetBootstrapAsync(
        string assistantKey,
        string locale,
        CancellationToken cancellationToken)
    {
        var authority = await RequireAuthorityAsync(assistantKey, cancellationToken);
        var normalizedLocale = NormalizeLocale(locale);
        var configuration = authority.Configuration;
        return new AssistantPublicBootstrap(
            authority.AssistantKey,
            authority.Scope.AssistantId,
            authority.Scope.PublishedVersionId,
            normalizedLocale,
            authority.Name,
            authority.Description,
            String(configuration, "welcomeMessage"),
            SafePublicUrl(String(configuration, "iconUrl")),
            AllowedTransports,
            Boolean(configuration, "anonymousChatEnabled", defaultValue: true),
            Boolean(configuration, "historyEnabled", defaultValue: true),
            AssistantExecutionMode.Public,
            String(configuration, "primaryColor") ?? "#2563eb",
            AssistantPresentation.Questions(configuration, configuration.TryGetProperty("runtime", out _)
                ? AssistantExecutionSnapshotService.Read(configuration).Questions.Where(q => q.ShowAsSuggestion).Select(q => q.Question)
                : Array.Empty<string>()),
            AssistantPresentation.Links(configuration),
            AssistantPresentation.ChatBackgroundColor(configuration));
    }

    /// <summary>
    /// 验证公开助手可用后创建匿名会话，并返回后续访问所需的 Bearer 令牌。
    /// </summary>
    public async Task<AssistantSessionCreated> CreateSessionAsync(
        CreateAnonymousAssistantSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ExecutionMode != AssistantExecutionMode.Public)
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_public_execution_mode_required");

        var authority = await RequireAuthorityAsync(command.AssistantKey, cancellationToken);
        if (!Boolean(authority.Configuration, "anonymousChatEnabled", defaultValue: true))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_anonymous_chat_disabled");

        var now = DateTime.Now;
        var bearerToken = NewSecret();
        var session = AnonymousAssistantSession.Create(
            Guid.NewGuid().ToString("N"),
            authority.Scope.AssistantId,
            authority.Scope.PublishedVersionId,
            AnonymousAssistantSession.HashSubject(bearerToken),
            command.Locale,
            AssistantExecutionMode.Public,
            now,
            now.Add(SessionLifetime));

        await _runtime.AddSessionAsync(session, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
        return new AssistantSessionCreated(Map(session), bearerToken);
    }

    /// <summary>
    /// 校验匿名会话令牌和发布范围后创建对话。
    /// </summary>
    public async Task<AssistantConversationAccepted> CreateConversationAsync(
        CreateAssistantConversationCommand command,
        string sessionId,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var authority = await RequireAuthorityAsync(command.AssistantKey, cancellationToken);
        var session = await RequireSessionAsync(authority.Scope, sessionId, bearerToken, cancellationToken);
        var now = DateTime.Now;
        var conversation = AssistantConversation.Create(
            Guid.NewGuid().ToString("N"), authority.Scope.AssistantId, session.Id,
            authority.Scope.PublishedVersionId,
            command.Locale, AssistantExecutionMode.Public, now);
        await _runtime.AddConversationAsync(conversation, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
        return new AssistantConversationAccepted(
            conversation.Id, session.Id, conversation.PublishedVersionId, conversation.Locale);
    }

    /// <summary>
    /// 校验会话、令牌与对话归属后，将用户消息提交给运行编排器。
    /// </summary>
    public async Task<AssistantTurnAccepted> SubmitTurnAsync(
        SubmitAssistantTurnCommand command,
        string sessionId,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var authority = await RequireAuthorityAsync(command.AssistantKey, cancellationToken);
        var session = await RequireSessionAsync(authority.Scope, sessionId, bearerToken, cancellationToken);
        var conversation = await _runtime.GetConversationAsync(authority.Scope, command.ConversationId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_conversation_not_found");
        conversation.EnsureScope(authority.Scope, session.Id);

        return await _orchestrator.SubmitTurnAsync(command, session.Id, cancellationToken);
    }

    /// <summary>
    /// 校验当前匿名访问范围后请求取消运行，并返回运行结果状态。
    /// </summary>
    public async Task<AssistantCancelResult> CancelRunAsync(
        string assistantKey,
        string runId,
        string sessionId,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var authority = await RequireAuthorityAsync(assistantKey, cancellationToken);
        _ = await RequireSessionAsync(authority.Scope, sessionId, bearerToken, cancellationToken);
        var run = await _runtime.GetRunAsync(authority.Scope, runId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_run_not_found");
        run.EnsureScope(authority.Scope, run.TurnId, run.ConversationId, sessionId);
        var before = run.Status;
        await _orchestrator.CancelRunAsync(assistantKey, runId, sessionId, cancellationToken);
        var current = await _runtime.GetRunAsync(authority.Scope, runId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_run_not_found");
        return new AssistantCancelResult(runId, current.Status, before != current.Status);
    }

    /// <summary>
    /// 校验会话权限后按稳定顺序分页读取对话消息。
    /// </summary>
    public async Task<AssistantMessagesPage> GetMessagesAsync(
        string assistantKey,
        string conversationId,
        string sessionId,
        string bearerToken,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var authority = await RequireAuthorityAsync(assistantKey, cancellationToken);
        var session = await RequireSessionAsync(authority.Scope, sessionId, bearerToken, cancellationToken);
        var conversation = await _runtime.GetConversationAsync(authority.Scope, conversationId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_conversation_not_found");
        conversation.EnsureScope(authority.Scope, session.Id);
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var all = new List<(AssistantMessage Message, string ClientMessageId, AssistantTurnStatus TurnStatus)>();
        for (var turnNo = 1L; turnNo <= conversation.LastTurnNo; turnNo++)
        {
            var turn = await FindTurnByNumberAsync(authority.Scope, conversation.Id, turnNo, cancellationToken);
            if (turn is not null)
                all.AddRange((await _runtime.ListMessagesByTurnAsync(authority.Scope, turn.Id, cancellationToken))
                    .Select(message => (message, turn.ClientMessageId, turn.Status)));
        }

        /*
         * 轮次和消息已按业务顺序收集；时间戳相同时不能再按随机标识排序。
         */
        var ordered = all.ToArray();
        var start = ParseCursor(cursor, ordered.Length);
        var page = ordered.Skip(start).Take(boundedLimit + 1).ToArray();
        var hasMore = page.Length > boundedLimit;
        var items = page.Take(boundedLimit).Select(x => Map(x.Message, x.ClientMessageId, x.TurnStatus)).ToArray();
        var next = hasMore && items.Length > 0 ? EncodeCursor(start + items.Length) : null;
        return new AssistantMessagesPage(items, next, hasMore);
    }

    /// <summary>
    /// 同时验证会话令牌、运行归属、票据有效期和传输类型。
    /// </summary>
    public async Task<AssistantStreamTicket> ValidateStreamTicketAsync(
        string assistantKey,
        string conversationId,
        string sessionId,
        string bearerToken,
        string runId,
        string ticket,
        AssistantTransportKind transport,
        CancellationToken cancellationToken)
    {
        var authority = await RequireAuthorityAsync(assistantKey, cancellationToken);
        _ = await RequireSessionAsync(authority.Scope, sessionId, bearerToken, cancellationToken);
        var run = await _runtime.GetRunAsync(authority.Scope, runId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_run_not_found");
        run.EnsureScope(authority.Scope, run.TurnId, conversationId, sessionId);
        var turn = await _runtime.GetTurnAsync(authority.Scope, run.TurnId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_turn_not_found");
        return await _tickets.ValidateAsync(
            ticket, transport, authority.Scope, sessionId, conversationId, turn.Id, run.Id,
            DateTime.Now, cancellationToken);
    }

    /// <summary>
    /// 解析并确认公开助手当前存在有效发布版本，否则拒绝访问。
    /// </summary>
    private async Task<AssistantPublicAuthority> RequireAuthorityAsync(string assistantKey, CancellationToken cancellationToken)
    {
        var authority = await _authority.ResolveAsync(assistantKey, cancellationToken);
        if (authority is null || authority.Scope.ExecutionMode != AssistantExecutionMode.Public)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");
        return authority;
    }

    /// <summary>
    /// 校验匿名会话令牌、会话有效期及其绑定的发布执行范围。
    /// </summary>
    private async Task<AnonymousAssistantSession> RequireSessionAsync(
        AssistantScopeSnapshot scope, string sessionId, string bearerToken, CancellationToken cancellationToken)
    {
        var session = await _runtime.GetSessionAsync(scope, sessionId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_session_not_found");
        session.EnsureAccessible(scope.AssistantId, scope.PublishedVersionId, scope.ExecutionMode, DateTime.Now);
        if (!string.Equals(session.SubjectHash, AnonymousAssistantSession.HashSubject(bearerToken), StringComparison.Ordinal))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_session_token_invalid");
        return session;
    }

    /// <summary>
    /// 按对话和轮次编号查询对应用户请求，并校验会话权限。
    /// </summary>
    private async Task<AssistantTurn?> FindTurnByNumberAsync(
        AssistantScopeSnapshot scope, string conversationId, long turnNo, CancellationToken cancellationToken)
        => await _runtime.GetTurnByNumberAsync(scope, conversationId, turnNo, cancellationToken);

    /// <summary>
    /// 将领域对象转换为应用层返回的数据传输对象。
    /// </summary>
    private static AnonymousAssistantSessionSnapshot Map(AnonymousAssistantSession value)
        => new(value.Id, value.AssistantId, value.PublishedVersionId,
            string.Empty, value.Locale, value.ExecutionMode, value.Status, value.LastSeenAt, value.ExpiresAt);

    /// <summary>
    /// 将领域对象转换为应用层返回的数据传输对象。
    /// </summary>
    private static AssistantMessageSnapshot Map(AssistantMessage value, string? clientMessageId = null, AssistantTurnStatus? turnStatus = null)
        => new(value.Id, value.ConversationId, value.TurnId, value.RunId, value.Role,
            value.Status, value.ContentJson, value.CompletedAt, clientMessageId, turnStatus);

    /// <summary>
    /// 规范化语言标识；不支持的语言回退到默认语言。
    /// </summary>
    private static string NormalizeLocale(string? locale)
        => string.IsNullOrWhiteSpace(locale) ? "zh-cn" : locale.Trim().Replace('_', '-').ToLowerInvariant();

    /// <summary>
    /// 读取 JSON 中指定名称的字符串字段，并统一空值处理方式。
    /// </summary>
    private static string? String(JsonElement json, string property)
        => json.ValueKind == JsonValueKind.Object
           && json.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// 读取 JSON 中指定名称的布尔字段，并在字段缺失时使用调用方提供的默认值。
    /// </summary>
    private static bool Boolean(JsonElement json, string property, bool defaultValue)
        => json.ValueKind == JsonValueKind.Object
           && json.TryGetProperty(property, out var value)
           && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;

    /// <summary>
    /// 过滤不安全或不可公开访问的资源链接。
    /// </summary>
    private static string? SafePublicUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl)
            || value.Contains('\\'))
            return null;

        if (value.StartsWith("/", StringComparison.Ordinal))
            return !value.StartsWith("//", StringComparison.Ordinal)
                && Uri.TryCreate(value, UriKind.Relative, out _)
                    ? value
                    : null;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && string.IsNullOrEmpty(uri.UserInfo)
                ? uri.ToString()
                : null;
    }

    /// <summary>
    /// 使用密码学安全随机数生成高熵匿名会话凭据。
    /// </summary>
    private static string NewSecret()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// 解码分页游标并校验偏移位置，非法游标返回稳定业务错误。
    /// </summary>
    private static int ParseCursor(string? cursor, int count)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        if (!int.TryParse(cursor, out var index) || index < 0 || index > count)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_messages_cursor_invalid");
        return index;
    }

    /// <summary>
    /// 将分页偏移位置编码为可在后续请求中安全传回的游标。
    /// </summary>
    private static string EncodeCursor(int index) => index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
