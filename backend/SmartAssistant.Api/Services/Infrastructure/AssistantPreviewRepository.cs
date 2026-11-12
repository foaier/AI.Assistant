#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SqlSugar;
using SmartAssistant.Services.Domain.Entities;

namespace SmartAssistant.Services.Infrastructure;

/// <summary>
/// 保存与读取草稿预览记录。
/// </summary>
public sealed class AssistantPreviewRepository(IDbContext db)
{
    /// <summary>
    /// 读取助手预览记录，并响应调用方的取消请求。
    /// </summary>
    public Task<AssistantPreviewEntity?> GetAsync(string assistant, string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return db.GetSugarScope().Queryable<AssistantPreviewEntity>()
            .Where(x => x.Id == id && x.AssistantId == assistant).FirstAsync()!;
    }
    /// <summary>
    /// 将预览记录加入事务上下文，等待调用方统一提交。
    /// </summary>
    public void Add(AssistantPreviewEntity record) => db.AddCommand(client => client.Insertable(record).ExecuteCommandAsync());
}
