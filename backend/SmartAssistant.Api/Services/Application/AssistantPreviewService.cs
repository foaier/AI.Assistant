#nullable enable
using System.Text.Json;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Models.Preview;
using SmartAssistant.Models.Publication;
using SmartAssistant.Models.Runtime;
using SmartAssistant.Platform;
using SmartAssistant.Services.Domain;
using SmartAssistant.Services.Domain.Entities;
using SmartAssistant.Services.Infrastructure;

namespace SmartAssistant.Services.Application;

/// <summary>
/// 为管理端创建有期限的草稿预览，并复用正式运行链路提交消息和读取结果。
/// </summary>
public sealed class AssistantPreviewService(IAssistantDraftRepository drafts, AssistantPreviewRepository previews,
    AssistantExecutionSnapshotService snapshots, IAssistantRuntimeRepository runtime,
    AssistantRunOrchestrator orchestrator, IDbContext db)
{
    /// <summary>
    /// 按预期草稿修订创建执行快照和有期限的预览会话。
    /// </summary>
    public async Task<AssistantPreviewCreated> CreateAsync(string assistant, long revision, CancellationToken ct)
    {
        var draft = await drafts.GetAsync(assistant, ct)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_not_found");
        if (draft.DraftRevision != revision) throw new BizResultException(BusinessCodes.Conflict, "assistant_draft_revision_conflict");
        var json = await snapshots.FreezeAsync(draft, ct);
        var now = DateTime.Now;
        var record = new AssistantPreviewEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            AssistantId = assistant,
            DraftRevision = revision,
            ConfigurationJson = json,
            SessionId = Guid.NewGuid().ToString("N"),
            ConversationId = Guid.NewGuid().ToString("N"),
            ExpiresAt = now.AddHours(1)
        };
        previews.Add(record);
        await runtime.AddSessionAsync(AnonymousAssistantSession.Create(record.SessionId, assistant, record.Id,
            AnonymousAssistantSession.HashSubject(record.SessionId), "zh-cn", AssistantExecutionMode.Preview, now, record.ExpiresAt), ct);
        await runtime.AddConversationAsync(AssistantConversation.Create(record.ConversationId, assistant, record.SessionId,
            record.Id, "zh-cn", AssistantExecutionMode.Preview, now), ct);
        await db.CommitAsync(ct);
        using var document = JsonDocument.Parse(json);
        return new(record.Id, record.ConversationId, revision, record.ExpiresAt,
            AssistantExecutionSnapshotService.Read(document.RootElement).Policy.Model);
    }

    /// <summary>
    /// 验证预览记录与范围后，将用户消息交给共用编排器执行。
    /// </summary>
    public async Task<AssistantTurnAccepted> SubmitAsync(string assistant,
        string preview, SubmitAssistantPreviewRequest request, CancellationToken ct)
    {
        var record = await RequireAsync(assistant, preview, ct);
        using var document = JsonDocument.Parse(record.ConfigurationJson);
        var authority = new AssistantPublicAuthority(assistant, Scope(record), assistant, null,
            document.RootElement.Clone(), record.DraftRevision, record.Id);
        return await orchestrator.SubmitAuthorizedTurnAsync(new(assistant, record.ConversationId,
            request.ClientMessageId, "text", request.Text, "zh-cn"), record.SessionId, authority, ct);
    }

    /// <summary>
    /// 读取预览对话消息并返回运行中状态及失败信息。
    /// </summary>
    public async Task<AssistantPreviewMessages> MessagesAsync(string assistant, string preview, CancellationToken ct)
    {
        var record = await RequireAsync(assistant, preview, ct);
        var scope = Scope(record);
        var conversation = await runtime.GetConversationAsync(scope, record.ConversationId, ct)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_conversation_not_found");
        var items = new List<AssistantMessageSnapshot>();
        var running = false;
        string? failure = null;
        for (var number = Math.Max(1, conversation.LastTurnNo - 49); number <= conversation.LastTurnNo; number++)
        {
            var turn = await runtime.GetTurnByNumberAsync(scope, record.ConversationId, number, ct);
            if (turn is null) continue;
            running |= turn.Status is AssistantTurnStatus.Accepted or AssistantTurnStatus.Running;
            if (number == conversation.LastTurnNo) failure = turn.FailureCode;
            items.AddRange((await runtime.ListMessagesByTurnAsync(scope, turn.Id, ct)).Select(message => new AssistantMessageSnapshot(
                message.Id, message.ConversationId, message.TurnId, message.RunId, message.Role,
                message.Status, message.ContentJson, message.CompletedAt)));
        }
        return new(items, running, failure);
    }

    /// <summary>
    /// 校验预览记录属于指定助手且尚未过期。
    /// </summary>
    private async Task<AssistantPreviewEntity> RequireAsync(string assistant, string id, CancellationToken ct)
    {
        if (await drafts.GetAsync(assistant, ct) is null)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_not_found");
        var record = await previews.GetAsync(assistant, id, ct);
        if (record is null || record.ExpiresAt <= DateTime.Now)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_preview_expired");
        return record;
    }
    /// <summary>
    /// 从预览快照还原绑定草稿修订的运行授权范围。
    /// </summary>
    private static AssistantScopeSnapshot Scope(AssistantPreviewEntity record)
        => new(record.AssistantId, record.Id, AssistantExecutionMode.Preview);
}
