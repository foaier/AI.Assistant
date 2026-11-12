#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Platform;
using SqlSugar;
using SmartAssistant.Modules.Domain.Entities;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 定义模型与业务策略当前配置、历史修订的持久化操作。
/// </summary>
public interface IAssistantPolicyRepository
{
    /// <summary>
    /// 列出助手策略相关记录，并响应调用方的取消请求。
    /// </summary>
    Task<IReadOnlyList<AssistantPolicyEntity>> ListAsync(string kind, CancellationToken ct);
    /// <summary>
    /// 读取助手策略记录，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantPolicyEntity?> GetAsync(string kind, string id, CancellationToken ct);
    /// <summary>
    /// 获取模型供应商，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantPolicyEntity?> GetProviderAsync(string id, CancellationToken ct);
    /// <summary>
    /// 获取修订，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantPolicyRevisionEntity?> GetRevisionAsync(string kind, string id, long revision, CancellationToken ct);
    /// <summary>
    /// 保存助手策略及其关联状态，并响应调用方的取消请求。
    /// </summary>
    Task SaveAsync(AssistantPolicyEntity record, long expectedRevision, CancellationToken ct);
}

/// <summary>
/// 保存模型与业务策略配置及历史修订，并检测并发更新。
/// </summary>
public sealed class AssistantPolicyRepository : IAssistantPolicyRepository
{
    /// <summary>
    /// 保存数据库上下文依赖，供助手策略仓储执行相关操作。
    /// </summary>
    private readonly IDbContext _db;
    /// <summary>
    /// 接收并保存助手策略仓储执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantPolicyRepository(IDbContext db) => _db = db;
    /// <summary>
    /// 列出助手策略相关记录，并响应调用方的取消请求。
    /// </summary>
    public async Task<IReadOnlyList<AssistantPolicyEntity>> ListAsync(string kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.GetSugarScope().Queryable<AssistantPolicyEntity>()
            .Where(x => x.Kind == kind)
            .OrderBy(x => x.Name).OrderBy(x => x.Id).ToListAsync();
    }
    /// <summary>
    /// 读取助手策略记录，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantPolicyEntity?> GetAsync(string kind, string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.GetSugarScope().Queryable<AssistantPolicyEntity>()
            .Where(x => x.Id == id && x.Kind == kind).FirstAsync();
    }
    /// <summary>
    /// 获取模型供应商，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantPolicyEntity?> GetProviderAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _db.GetSugarScope().Queryable<AssistantPolicyEntity>()
            .Where(x => x.Id == id && x.Kind == "provider" && x.Enabled).FirstAsync();
    }
    /// <summary>
    /// 获取修订，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantPolicyRevisionEntity?> GetRevisionAsync(string kind, string id, long revision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        /*
         * 只有所属范围内的当前配置仍有效时，历史修订才允许被访问。
         */
        return await _db.GetSugarScope().Queryable<AssistantPolicyRevisionEntity>()
            .InnerJoin<AssistantPolicyEntity>((v, p) => v.RecordId == p.Id && v.Kind == p.Kind)
            .Where((v, p) => v.RecordId == id && v.Revision == revision && v.Kind == kind && p.Enabled).Select((v, p) => v).FirstAsync();
    }
    /// <summary>
    /// 保存助手策略及其关联状态，并响应调用方的取消请求。
    /// </summary>
    public Task SaveAsync(AssistantPolicyEntity record, long expectedRevision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (expectedRevision == 0)
            _db.AddCommand(client => client.Insertable(record).ExecuteCommandAsync());
        else
            _db.AddCommand(async client =>
            {
                var rows = await client.Updateable(record).Where(x => x.Id == record.Id && x.Kind == record.Kind && x.Revision == expectedRevision).ExecuteCommandAsync();
                if (rows != 1) throw new BizResultException(BusinessCodes.Conflict, "assistant_policy_revision_conflict");
                return rows;
            });
        var version = new AssistantPolicyRevisionEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = record.Id,
            Kind = record.Kind,
            Revision = record.Revision,
            SettingsJson = record.SettingsJson,
            Enabled = record.Enabled
        };
        _db.AddCommand(client => client.Insertable(version).ExecuteCommandAsync());
        return Task.CompletedTask;
    }
}
