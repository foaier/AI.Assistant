#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AI.Assistant.Extensions.DAL;
using AI.Assistant.Services.Domain;
using AI.Assistant.Infrastructure;
using SqlSugar;
using AI.Assistant.Services.Domain.Entities;

namespace AI.Assistant.Services.Repositorys;

/// <summary>
/// 定义助手草稿的范围查询、幂等查询和带修订校验的写入操作。
/// </summary>
public interface IAssistantDraftRepository
{
    /// <summary>
    /// 列出助手草稿相关记录，并响应调用方的取消请求。
    /// </summary>
    Task<IReadOnlyList<AssistantDraft>> ListAsync(CancellationToken cancellationToken);
    /// <summary>
    /// 读取助手草稿记录，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantDraft?> GetAsync(string id, CancellationToken cancellationToken);
    /// <summary>
    /// 按创建幂等键读取已有草稿。
    /// </summary>
    Task<AssistantDraft?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken);
    /// <summary>
    /// 新增助手草稿记录，并响应调用方的取消请求。
    /// </summary>
    Task AddAsync(AssistantDraft draft, CancellationToken cancellationToken);
    /// <summary>
    /// 将带预期修订条件的草稿更新加入事务队列，未命中时报告并发冲突。
    /// </summary>
    Task SaveAsync(AssistantDraft draft, long expectedRevision, CancellationToken cancellationToken);
    /// <summary>
    /// 物理删除助手草稿及关联运行数据，并响应调用方的取消请求。
    /// </summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}

/// <summary>
/// 将助手草稿映射到数据库，并以预期修订号保护并发更新。
/// </summary>
public sealed class AssistantDraftRepository : IAssistantDraftRepository
{
    /// <summary>
    /// 保存数据库上下文依赖，供助手草稿仓储执行相关操作。
    /// </summary>
    private readonly IDbContext _dbContext;

    /// <summary>
    /// 接收并保存助手草稿仓储执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantDraftRepository(IDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// 读取助手草稿记录，并响应调用方的取消请求。
    /// </summary>
    public Task<AssistantDraft?> GetAsync(string id, CancellationToken cancellationToken)
        => Query(scope => scope.Id == id, cancellationToken);

    /// <summary>
    /// 列出助手草稿相关记录，并响应调用方的取消请求。
    /// </summary>
    public async Task<IReadOnlyList<AssistantDraft>> ListAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await _dbContext.GetSugarScope()
            .Queryable<AssistantDraftEntity>()
            .OrderBy(scope => scope.Name).OrderBy(scope => scope.Id)
            .ToListAsync();
        return rows.Select(ToDomain).ToArray();
    }

    /// <summary>
    /// 按创建幂等键读取已有草稿。
    /// </summary>
    public Task<AssistantDraft?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken)
        => Query(scope => scope.CreationIdempotencyKey == key, cancellationToken);

    /// <summary>
    /// 新增助手草稿记录，并响应调用方的取消请求。
    /// </summary>
    public Task AddAsync(AssistantDraft draft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(draft)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 将带预期修订条件的草稿更新加入事务队列，未命中时报告并发冲突。
    /// </summary>
    public Task SaveAsync(AssistantDraft draft, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(async client =>
        {
            var affected = await client.Updateable(ToRow(draft))
                .Where(row => row.Id == draft.Id
                    && row.DraftRevision == expectedRevision)
                .ExecuteCommandAsync();
            if (affected != 1)
                throw new DraftRevisionConflictException();
            return affected;
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 物理删除助手草稿及关联运行数据，并响应调用方的取消请求。
    /// </summary>
    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(async client =>
        {
            /*
             * 在提交事务内再次核对发布状态，避免检查后发生发布而删除有效入口。
             * 会话删除通过外键级联清理对话、轮次、运行、消息和工具调用。
             */
            if (await client.Queryable<AssistantPublicationPointerEntity>()
                .Where(row => row.AssistantId == id && row.Status == 1)
                .AnyAsync())
                throw new BizResultException(BusinessCodes.Conflict, "assistant_published_revoke_before_delete");
            await client.Deleteable<SessionEntity>()
                .Where(row => row.AssistantId == id).ExecuteCommandAsync();
            await client.Deleteable<AssistantPreviewEntity>()
                .Where(row => row.AssistantId == id).ExecuteCommandAsync();
            await client.Deleteable<AssistantPublicationPointerEntity>()
                .Where(row => row.AssistantId == id).ExecuteCommandAsync();
            await client.Deleteable<AssistantPublishedVersionEntity>()
                .Where(row => row.AssistantId == id).ExecuteCommandAsync();
            return await client.Deleteable<AssistantDraftEntity>()
                .Where(row => row.Id == id).ExecuteCommandAsync();
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 按调用方提供的范围与筛选条件读取草稿，并响应取消请求。
    /// </summary>
    private async Task<AssistantDraft?> Query(Expression<Func<AssistantDraftEntity, bool>> predicate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<AssistantDraftEntity>()
            .Where(predicate)
            .FirstAsync();
        return row is null ? null : ToDomain(row);
    }

    /// <summary>
    /// 将数据转换为助手草稿。
    /// </summary>
    private static AssistantDraftEntity ToRow(AssistantDraft draft) => new()
    {
        Id = draft.Id,
        Name = draft.Name,
        Description = draft.Description,
        DraftJson = draft.DraftJson,
        DraftRevision = draft.DraftRevision,
        CreationIdempotencyKey = draft.CreationIdempotencyKey
    };

    /// <summary>
    /// 将数据转换为助手草稿。
    /// </summary>
    private static AssistantDraft ToDomain(AssistantDraftEntity row)
    {
        return AssistantDraft.Rehydrate(row.Id, row.Name, row.Description,
            row.DraftJson, row.DraftRevision, row.CreationIdempotencyKey);
    }

    /// <summary>
    /// 表示草稿持久化时预期修订号已失效。
    /// </summary>
    public sealed class DraftRevisionConflictException : Exception
    {
        /// <summary>
        /// 接收并保存草稿修订冲突执行所需的依赖，建立后续操作使用的运行状态。
        /// </summary>
        public DraftRevisionConflictException() : base("assistant_draft_revision_conflict") { }
    }
}
