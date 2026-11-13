#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using AI.Assistant.Extensions.DAL;
using AI.Assistant.Services.Contracts;
using AI.Assistant.Infrastructure;
using SqlSugar;
using AI.Assistant.Extensions.Common;
using AI.Assistant.Services.Domain.Entities;

namespace AI.Assistant.Services.Repositorys;

/// <summary>
/// 定义知识库嵌入配置的修订保存与生效代次提升操作。
/// </summary>
public interface IAssistantKnowledgeEmbeddingProfileRepository
{
    /// <summary>
    /// 读取助手知识嵌入配置记录，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantKnowledgeEmbeddingProfileEntity?> GetAsync(string knowledgeBaseId, CancellationToken ct);
    /// <summary>
    /// 新增助手知识嵌入配置记录，并响应调用方的取消请求。
    /// </summary>
    Task AddAsync(AssistantKnowledgeEmbeddingProfileEntity row, CancellationToken ct);
    /// <summary>
    /// 保存助手知识嵌入配置及其关联状态，并响应调用方的取消请求。
    /// </summary>
    Task SaveAsync(AssistantKnowledgeEmbeddingProfileEntity row, long expectedRevision, CancellationToken ct);
    /// <summary>
    /// 仅在修订号与目标代次仍匹配时更新生效嵌入配置，防止旧任务覆盖新配置。
    /// </summary>
    Task TryPromoteAsync(AssistantKnowledgeEmbeddingProfileEntity row, long expectedRevision, long expectedTargetGeneration, CancellationToken ct);
}

/// <summary>
/// 持久化目标与生效嵌入配置，并通过条件更新保护代次切换。
/// </summary>
public sealed class AssistantKnowledgeEmbeddingProfileRepository : IAssistantKnowledgeEmbeddingProfileRepository
{
    /// <summary>
    /// 保存数据库上下文依赖，供助手知识嵌入配置仓储执行相关操作。
    /// </summary>
    private readonly IDbContext _db;

    /// <summary>
    /// 接收并保存助手知识嵌入配置仓储执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantKnowledgeEmbeddingProfileRepository(IDbContext db) => _db = db;

    /// <summary>
    /// 读取助手知识嵌入配置记录，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantKnowledgeEmbeddingProfileEntity?> GetAsync(string knowledgeBaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.GetSugarScope().Queryable<AssistantKnowledgeEmbeddingProfileEntity>()
            .InnerJoin<AssistantKnowledgeBaseEntity>((profile, basis) =>
                profile.KnowledgeBaseId == basis.Id)
            .Where((profile, basis) => profile.KnowledgeBaseId == knowledgeBaseId)
            .Select((profile, basis) => profile)
            .FirstAsync();
    }

    /// <summary>
    /// 新增助手知识嵌入配置记录，并响应调用方的取消请求。
    /// </summary>
    public Task AddAsync(AssistantKnowledgeEmbeddingProfileEntity row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存助手知识嵌入配置及其关联状态，并响应调用方的取消请求。
    /// </summary>
    public Task SaveAsync(AssistantKnowledgeEmbeddingProfileEntity row, long expectedRevision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.AddCommand(async client =>
        {
            var affected = await client.Updateable(row)
                .Where(value => value.Id == row.Id
                    && value.KnowledgeBaseId == row.KnowledgeBaseId && value.Revision == expectedRevision)
                .ExecuteCommandAsync();
            if (affected != 1)
                throw new BizResultException(BusinessCodes.Conflict, "knowledge_embedding_profile_revision_conflict");
            return affected;
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 仅在修订号与目标代次仍匹配时更新生效嵌入配置，防止旧任务覆盖新配置。
    /// </summary>
    public Task TryPromoteAsync(AssistantKnowledgeEmbeddingProfileEntity row, long expectedRevision,
        long expectedTargetGeneration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.AddCommand(async client =>
        {
            return await client.Updateable(row)
                .Where(value => value.Id == row.Id
                    && value.KnowledgeBaseId == row.KnowledgeBaseId
                    && value.Revision == expectedRevision && value.TargetGeneration == expectedTargetGeneration)
                .ExecuteCommandAsync() == 1 ? 1
                : throw new BizResultException(BusinessCodes.Conflict, "knowledge_embedding_profile_revision_conflict");
        });
        return Task.CompletedTask;
    }
}
