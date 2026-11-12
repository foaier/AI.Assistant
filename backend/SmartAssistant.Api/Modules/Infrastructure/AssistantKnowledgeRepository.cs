#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Contracts;
using SqlSugar;
using SmartAssistant.Modules.Domain.Entities;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 定义知识库、固定问答、分块策略及助手引用检查的持久化边界。
/// </summary>
public interface IAssistantKnowledgeRepository
{
    /// <summary>
    /// 列出知识库，并响应调用方的取消请求。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeBaseEntity>> ListBasesAsync(CancellationToken cancellationToken);
    /// <summary>
    /// 获取知识库，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantKnowledgeBaseEntity?> GetBaseAsync(string id, CancellationToken cancellationToken);
    /// <summary>
    /// 添加知识库，并响应调用方的取消请求。
    /// </summary>
    Task AddBaseAsync(AssistantKnowledgeBaseEntity row, CancellationToken cancellationToken);
    /// <summary>
    /// 判断是否存在或已具备助手引用，并响应调用方的取消请求。
    /// </summary>
    Task<bool> HasAssistantReferenceAsync(string knowledgeBaseId, CancellationToken cancellationToken);
    /// <summary>
    /// 删除知识库，并响应调用方的取消请求。
    /// </summary>
    Task DeleteBaseAsync(string id, CancellationToken cancellationToken);
    /// <summary>
    /// 获取分块策略，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantKnowledgeChunkingPolicyEntity?> GetChunkingPolicyAsync(string knowledgeBaseId, CancellationToken cancellationToken);
    /// <summary>
    /// 添加分块策略，并响应调用方的取消请求。
    /// </summary>
    Task AddChunkingPolicyAsync(AssistantKnowledgeChunkingPolicyEntity row, CancellationToken cancellationToken);
    /// <summary>
    /// 保存分块策略，并响应调用方的取消请求。
    /// </summary>
    Task SaveChunkingPolicyAsync(AssistantKnowledgeChunkingPolicyEntity row, CancellationToken cancellationToken);
    /// <summary>
    /// 列出固定问答，并响应调用方的取消请求。
    /// </summary>
    Task<IReadOnlyList<AssistantKnowledgeQnaEntity>> ListQnaAsync(string knowledgeBaseId, CancellationToken cancellationToken);
    /// <summary>
    /// 获取固定问答，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantKnowledgeQnaEntity?> GetQnaAsync(string knowledgeBaseId, string id, CancellationToken cancellationToken);
    /// <summary>
    /// 获取固定问答问题，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantKnowledgeQnaEntity?> GetQnaByNormalizedQuestionAsync(string knowledgeBaseId, string normalizedQuestion, string? exceptId, CancellationToken cancellationToken);
    /// <summary>
    /// 查找启用回答，并响应调用方的取消请求。
    /// </summary>
    Task<string?> FindEnabledAnswerAsync(string knowledgeBaseId, string normalizedQuestion, CancellationToken cancellationToken);
    /// <summary>
    /// 添加固定问答，并响应调用方的取消请求。
    /// </summary>
    Task AddQnaAsync(AssistantKnowledgeQnaEntity row, CancellationToken cancellationToken);
    /// <summary>
    /// 保存固定问答，并响应调用方的取消请求。
    /// </summary>
    Task SaveQnaAsync(AssistantKnowledgeQnaEntity row, CancellationToken cancellationToken);
    /// <summary>
    /// 删除固定问答，并响应调用方的取消请求。
    /// </summary>
    Task DeleteQnaAsync(string knowledgeBaseId, string id, CancellationToken cancellationToken);
}

/// <summary>
/// 实现知识库与固定问答持久化，并检查删除前的助手绑定关系。
/// </summary>
public sealed class AssistantKnowledgeRepository : IAssistantKnowledgeRepository
{
    /// <summary>
    /// 保存数据库上下文依赖，供助手知识仓储执行相关操作。
    /// </summary>
    private readonly IDbContext _dbContext;

    /// <summary>
    /// 接收并保存助手知识仓储执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantKnowledgeRepository(IDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// 列出知识库，并响应调用方的取消请求。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeBaseEntity>> ListBasesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeBaseEntity>()
            .OrderBy(x => x.Name).OrderBy(x => x.Id).ToListAsync();
    }

    /// <summary>
    /// 获取知识库，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantKnowledgeBaseEntity?> GetBaseAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeBaseEntity>()
            .Where(x => x.Id == id).FirstAsync();
    }

    /// <summary>
    /// 添加知识库，并响应调用方的取消请求。
    /// </summary>
    public Task AddBaseAsync(AssistantKnowledgeBaseEntity row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 判断是否存在或已具备助手引用，并响应调用方的取消请求。
    /// </summary>
    public async Task<bool> HasAssistantReferenceAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await _dbContext.GetSugarScope()
            .Queryable<AssistantDraftReferenceEntity>()
            .Select(x => x.DraftJson)
            .ToListAsync();
        foreach (var json in rows)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (ReferencesKnowledgeBase(document.RootElement, knowledgeBaseId))
                    return true;
            }
            catch (JsonException)
            {
                /*
                 * 新写入的草稿已校验 JSON；引用检查忽略历史损坏记录。
                 * 正常读取草稿时仍会暴露原有配置错误。
                 */
            }
        }

        var publishedSnapshots = await _dbContext.GetSugarScope()
            .Queryable<AssistantPublishedVersionReferenceEntity>()
            .InnerJoin<AssistantPublicationPointerReferenceEntity>((version, pointer) =>
                version.Id == pointer.CurrentPublishedVersionId
                && version.AssistantId == pointer.AssistantId
                && pointer.Status == 1)
            .Where((version, pointer) =>
                !version.IsRevoked)
            .Select((version, pointer) => version.SnapshotJson)
            .ToListAsync();
        foreach (var json in publishedSnapshots)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (ReferencesKnowledgeBase(document.RootElement, knowledgeBaseId))
                    return true;
            }
            catch (JsonException)
            {
                /*
                 * 格式损坏的历史快照无法通过正常发布流程生效，检查当前引用时忽略该记录。
                 */
            }
        }

        return false;
    }

    /// <summary>
    /// 检查配置 JSON 是否引用指定知识库标识。
    /// </summary>
    private static bool ReferencesKnowledgeBase(JsonElement value, string knowledgeBaseId)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (string.Equals(property.Name, "knowledgeBaseId", StringComparison.Ordinal)
                        && property.Value.ValueKind == JsonValueKind.String
                        && string.Equals(property.Value.GetString(), knowledgeBaseId, StringComparison.Ordinal))
                        return true;
                    if (ReferencesKnowledgeBase(property.Value, knowledgeBaseId))
                        return true;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (ReferencesKnowledgeBase(item, knowledgeBaseId))
                        return true;
                }
                break;
        }

        return false;
    }

    /// <summary>
    /// 删除知识库，并响应调用方的取消请求。
    /// </summary>
    public Task DeleteBaseAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Deleteable<AssistantKnowledgeBaseEntity>()
            .Where(row => row.Id == id)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取分块策略，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantKnowledgeChunkingPolicyEntity?> GetChunkingPolicyAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeChunkingPolicyEntity>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId)
            .FirstAsync();
    }

    /// <summary>
    /// 添加分块策略，并响应调用方的取消请求。
    /// </summary>
    public Task AddChunkingPolicyAsync(AssistantKnowledgeChunkingPolicyEntity row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存分块策略，并响应调用方的取消请求。
    /// </summary>
    public Task SaveChunkingPolicyAsync(AssistantKnowledgeChunkingPolicyEntity row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 列出固定问答，并响应调用方的取消请求。
    /// </summary>
    public async Task<IReadOnlyList<AssistantKnowledgeQnaEntity>> ListQnaAsync(string knowledgeBaseId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeQnaEntity>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId)
            .OrderBy(x => x.SortOrder).OrderBy(x => x.Question).ToListAsync();
    }

    /// <summary>
    /// 获取固定问答，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantKnowledgeQnaEntity?> GetQnaAsync(string knowledgeBaseId, string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeQnaEntity>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.Id == id).FirstAsync();
    }

    /// <summary>
    /// 获取固定问答问题，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantKnowledgeQnaEntity?> GetQnaByNormalizedQuestionAsync(string knowledgeBaseId, string normalizedQuestion, string? exceptId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeQnaEntity>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId
                && x.NormalizedQuestion == normalizedQuestion
            && (exceptId == null || x.Id != exceptId)).FirstAsync();
    }

    /// <summary>
    /// 查找启用回答，并响应调用方的取消请求。
    /// </summary>
    public async Task<string?> FindEnabledAnswerAsync(string knowledgeBaseId, string normalizedQuestion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope().Queryable<AssistantKnowledgeQnaEntity>()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId
                && x.NormalizedQuestion == normalizedQuestion && x.Enabled)
            .FirstAsync();
        return string.IsNullOrWhiteSpace(row?.Answer) ? null : row.Answer;
    }

    /// <summary>
    /// 添加固定问答，并响应调用方的取消请求。
    /// </summary>
    public Task AddQnaAsync(AssistantKnowledgeQnaEntity row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(row).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存固定问答，并响应调用方的取消请求。
    /// </summary>
    public Task SaveQnaAsync(AssistantKnowledgeQnaEntity row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Updateable(row)
            .Where(x => x.Id == row.Id)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 删除固定问答，并响应调用方的取消请求。
    /// </summary>
    public Task DeleteQnaAsync(string knowledgeBaseId, string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Deleteable<AssistantKnowledgeQnaEntity>()
            .Where(row => row.Id == id
                && row.KnowledgeBaseId == knowledgeBaseId)
            .ExecuteCommandAsync());
        return Task.CompletedTask;
    }
}
