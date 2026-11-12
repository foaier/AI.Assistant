#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Extensions.DAL;
using SqlSugar;
using SmartAssistant.Platform;
using SmartAssistant.Services.Domain.Entities;

namespace SmartAssistant.Services.Infrastructure;

/// <summary>
/// 定义文档、版本、片段和索引任务的持久化操作。
/// </summary>
public interface IAssistantKnowledgeDocumentRepository
{
    /// <summary>
    /// 列出文档，并响应调用方的取消请求。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeDocumentEntity>> ListDocumentsAsync(string knowledgeBaseId, CancellationToken ct);
    /// <summary>
    /// 获取文档，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantKnowledgeDocumentEntity?> GetDocumentAsync(string knowledgeBaseId, string documentId, CancellationToken ct);
    /// <summary>
    /// 添加文档，并响应调用方的取消请求。
    /// </summary>
    Task AddDocumentAsync(AssistantKnowledgeDocumentEntity row, CancellationToken ct);
    /// <summary>
    /// 更新文档，并响应调用方的取消请求。
    /// </summary>
    Task UpdateDocumentAsync(AssistantKnowledgeDocumentEntity row, CancellationToken ct);
    /// <summary>
    /// 添加版本，并响应调用方的取消请求。
    /// </summary>
    Task AddVersionAsync(AssistantKnowledgeDocumentVersionEntity row, CancellationToken ct);
    /// <summary>
    /// 更新版本，并响应调用方的取消请求。
    /// </summary>
    Task UpdateVersionAsync(AssistantKnowledgeDocumentVersionEntity row, CancellationToken ct);
    /// <summary>
    /// 获取版本，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantKnowledgeDocumentVersionEntity?> GetVersionAsync(string documentId, int versionNo, CancellationToken ct);
    /// <summary>
    /// 添加片段，并响应调用方的取消请求。
    /// </summary>
    Task AddChunksAsync(IReadOnlyList<AssistantKnowledgeChunkEntity> rows, CancellationToken ct);
    /// <summary>
    /// 更新片段点，并响应调用方的取消请求。
    /// </summary>
    Task UpdateChunkPointIdsAsync(IReadOnlyList<AssistantKnowledgeChunkEntity> rows, CancellationToken ct);
    /// <summary>
    /// 列出片段，并响应调用方的取消请求。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeChunkEntity>> ListChunksAsync(string versionId, CancellationToken ct);
    /// <summary>
    /// 添加索引任务，并响应调用方的取消请求。
    /// </summary>
    Task AddIndexJobAsync(AssistantKnowledgeIndexJobEntity row, CancellationToken ct);
    /// <summary>
    /// 更新索引任务，并响应调用方的取消请求。
    /// </summary>
    Task UpdateIndexJobAsync(AssistantKnowledgeIndexJobEntity row, CancellationToken ct);
    /// <summary>
    /// 获取索引任务，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantKnowledgeIndexJobEntity?> GetIndexJobAsync(string knowledgeBaseId, string jobId, CancellationToken ct);
    /// <summary>
    /// 列出索引任务，并响应调用方的取消请求。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeIndexJobEntity>> ListIndexJobsAsync(string knowledgeBaseId, CancellationToken ct);
}

/// <summary>
/// 保存知识文档及其版本、片段和索引任务，并按知识库限定查询范围。
/// </summary>
public sealed class AssistantKnowledgeDocumentRepository : IAssistantKnowledgeDocumentRepository
{
    /// <summary>
    /// 保存数据库上下文依赖，供助手知识文档仓储执行相关操作。
    /// </summary>
    private readonly IDbContext _dbContext;

    /// <summary>
    /// 接收并保存助手知识文档仓储执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantKnowledgeDocumentRepository(IDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// 列出文档，并响应调用方的取消请求。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeDocumentEntity>> ListDocumentsAsync(string knowledgeBaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeDocumentEntity>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId)
            .OrderBy(x => x.FileName).OrderBy(x => x.Id).ToListAsync();
    }

    /// <summary>
    /// 获取文档，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantKnowledgeDocumentEntity?> GetDocumentAsync(string knowledgeBaseId, string documentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeDocumentEntity>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.Id == documentId)
            .FirstAsync();
    }

    /// <summary>
    /// 添加文档，并响应调用方的取消请求。
    /// </summary>
    public Task AddDocumentAsync(AssistantKnowledgeDocumentEntity row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新文档，并响应调用方的取消请求。
    /// </summary>
    public Task UpdateDocumentAsync(AssistantKnowledgeDocumentEntity row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加版本，并响应调用方的取消请求。
    /// </summary>
    public Task AddVersionAsync(AssistantKnowledgeDocumentVersionEntity row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新版本，并响应调用方的取消请求。
    /// </summary>
    public Task UpdateVersionAsync(AssistantKnowledgeDocumentVersionEntity row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取版本，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantKnowledgeDocumentVersionEntity?> GetVersionAsync(string documentId, int versionNo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeDocumentVersionEntity>()
            .Where(x => x.DocumentId == documentId && x.VersionNo == versionNo)
            .FirstAsync();
    }

    /// <summary>
    /// 添加片段，并响应调用方的取消请求。
    /// </summary>
    public Task AddChunksAsync(IReadOnlyList<AssistantKnowledgeChunkEntity> rows, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (rows.Count > 0)
            _dbContext.AddCommand(client => client.Insertable(rows.ToList()).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新片段点，并响应调用方的取消请求。
    /// </summary>
    public Task UpdateChunkPointIdsAsync(IReadOnlyList<AssistantKnowledgeChunkEntity> rows, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (rows.Count > 0)
            _dbContext.AddCommand(async client =>
            {
                var affected = 0;
                foreach (var row in rows)
                    affected += await client.Updateable(row)
                        .Where(value => value.Id == row.Id
                            && value.DocumentVersionId == row.DocumentVersionId)
                        .ExecuteCommandAsync();
                if (affected != rows.Count)
                    throw new BizResultException(BusinessCodes.Conflict, "knowledge_chunk_point_link_conflict");
                return affected;
            });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 列出片段，并响应调用方的取消请求。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeChunkEntity>> ListChunksAsync(string versionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeChunkEntity>()
            .Where(x => x.DocumentVersionId == versionId)
            .OrderBy(x => x.ChunkIndex).ToListAsync();
    }

    /// <summary>
    /// 添加索引任务，并响应调用方的取消请求。
    /// </summary>
    public Task AddIndexJobAsync(AssistantKnowledgeIndexJobEntity row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新索引任务，并响应调用方的取消请求。
    /// </summary>
    public Task UpdateIndexJobAsync(AssistantKnowledgeIndexJobEntity row, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取索引任务，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantKnowledgeIndexJobEntity?> GetIndexJobAsync(string knowledgeBaseId, string jobId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeIndexJobEntity>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.Id == jobId)
            .FirstAsync();
    }

    /// <summary>
    /// 列出索引任务，并响应调用方的取消请求。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeIndexJobEntity>> ListIndexJobsAsync(string knowledgeBaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeIndexJobEntity>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId)
            .OrderBy("rowid DESC").ToListAsync();
    }
}
