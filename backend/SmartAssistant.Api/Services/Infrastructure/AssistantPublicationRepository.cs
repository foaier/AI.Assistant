#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Services.Domain;
using SmartAssistant.Services.Contracts;
using SqlSugar;
using SmartAssistant.Services.Domain.Entities;

namespace SmartAssistant.Services.Infrastructure;

/// <summary>
/// 定义发布快照、公开标识及带修订保护的发布指针操作。
/// </summary>
public interface IAssistantPublicationRepository
{
    /// <summary>
    /// 获取指针，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantPublicationPointer?> GetPointerAsync(
        string assistantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 获取版本，并响应调用方的取消请求。
    /// </summary>
    Task<AssistantPublishedVersion?> GetVersionAsync(
        string versionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 获取当前助手密钥，并响应调用方的取消请求。
    /// </summary>
    Task<(AssistantPublicationPointer Pointer, AssistantPublishedVersion Version)?>
        GetCurrentByAssistantKeyAsync(string assistantKey, CancellationToken cancellationToken);

    /// <summary>
    /// 添加版本，并响应调用方的取消请求。
    /// </summary>
    Task AddVersionAsync(AssistantPublishedVersion version, CancellationToken cancellationToken);
    /// <summary>
    /// 添加指针，并响应调用方的取消请求。
    /// </summary>
    Task AddPointerAsync(AssistantPublicationPointer pointer, CancellationToken cancellationToken);
    /// <summary>
    /// 保存指针，并响应调用方的取消请求。
    /// </summary>
    Task SavePointerAsync(
        AssistantPublicationPointer pointer,
        long expectedRevision,
        CancellationToken cancellationToken);
    /// <summary>
    /// 保存版本，并响应调用方的取消请求。
    /// </summary>
    Task SaveVersionAsync(AssistantPublishedVersion version, CancellationToken cancellationToken);
}

/// <summary>
/// 保存冻结的发布版本，并通过条件写入保护版本发布和撤回。
/// </summary>
public sealed class AssistantPublicationRepository : IAssistantPublicationRepository
{
    /// <summary>
    /// 保存数据库上下文依赖，供助手发布仓储执行相关操作。
    /// </summary>
    private readonly IDbContext _dbContext;

    /// <summary>
    /// 接收并保存助手发布仓储执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantPublicationRepository(IDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// 获取指针，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantPublicationPointer?> GetPointerAsync(string assistantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<AssistantPublicationPointerEntity>()
            .Where(value => value.AssistantId == assistantId)
            .FirstAsync();
        return row is null ? null : ToDomain(row);
    }

    /// <summary>
    /// 获取版本，并响应调用方的取消请求。
    /// </summary>
    public async Task<AssistantPublishedVersion?> GetVersionAsync(
        string versionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await _dbContext.GetSugarScope()
            .Queryable<AssistantPublishedVersionEntity>()
            .Where(value => value.Id == versionId)
            .FirstAsync();
        return row is null ? null : ToDomain(row);
    }

    /// <summary>
    /// 获取当前助手密钥，并响应调用方的取消请求。
    /// </summary>
    public async Task<(AssistantPublicationPointer Pointer, AssistantPublishedVersion Version)?> GetCurrentByAssistantKeyAsync(string assistantKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = _dbContext.GetSugarScope();
        var pointerRow = await scope.Queryable<AssistantPublicationPointerEntity>()
            .Where(value => value.AssistantKey == assistantKey
                && value.Status == (int)AssistantPublicationPointerStatus.Active
                && value.CurrentPublishedVersionId != null)
            .FirstAsync();
        if (pointerRow is null || string.IsNullOrWhiteSpace(pointerRow.CurrentPublishedVersionId))
            return null;

        var versionRow = await scope.Queryable<AssistantPublishedVersionEntity>()
            .Where(value => value.Id == pointerRow.CurrentPublishedVersionId
                && value.AssistantId == pointerRow.AssistantId
                && value.AssistantKey == pointerRow.AssistantKey
                && !value.IsRevoked)
            .FirstAsync();
        if (versionRow is null)
            return null;

        return (ToDomain(pointerRow), ToDomain(versionRow));
    }

    /// <summary>
    /// 添加版本，并响应调用方的取消请求。
    /// </summary>
    public Task AddVersionAsync(AssistantPublishedVersion version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(version)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加指针，并响应调用方的取消请求。
    /// </summary>
    public Task AddPointerAsync(AssistantPublicationPointer pointer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(client => client.Insertable(ToRow(pointer)).ExecuteCommandAsync());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存指针，并响应调用方的取消请求。
    /// </summary>
    public Task SavePointerAsync(AssistantPublicationPointer pointer, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(async client =>
        {
            var affected = await client.Updateable(ToRow(pointer))
                .Where(row => row.Id == pointer.Id
                    && row.PointerRevision == expectedRevision)
                .ExecuteCommandAsync();
            if (affected != 1)
                throw new PointerRevisionConflictException();
            return affected;
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存版本，并响应调用方的取消请求。
    /// </summary>
    public Task SaveVersionAsync(AssistantPublishedVersion version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AddCommand(async client =>
        {
            var affected = await client.Updateable(ToRow(version))
                .Where(row => row.Id == version.Id
                    && !row.IsRevoked)
                .ExecuteCommandAsync();
            if (affected != 1)
                throw new VersionRevokeConflictException();
            return affected;
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 将数据转换为助手发布。
    /// </summary>
    private static AssistantPublicationPointerEntity ToRow(AssistantPublicationPointer value) => new()
    {
        Id = value.Id,
        AssistantKey = value.AssistantKey,
        AssistantId = value.AssistantId,
        CurrentPublishedVersionId = value.CurrentPublishedVersionId,
        CurrentVersionNo = value.CurrentVersionNo,
        PointerRevision = value.PointerRevision,
        Status = (int)value.Status
    };

    /// <summary>
    /// 将数据转换为助手发布。
    /// </summary>
    private static AssistantPublishedVersionEntity ToRow(AssistantPublishedVersion value) => new()
    {
        Id = value.Id,
        AssistantKey = value.AssistantKey,
        AssistantId = value.AssistantId,
        VersionNo = value.VersionNo,
        Name = value.Name,
        Description = value.Description,
        SnapshotJson = value.SnapshotJson,
        SnapshotHash = value.SnapshotHash,
        IsRevoked = value.IsRevoked,
        RevokedAt = value.RevokedAt,
        PublishedAt = value.PublishedAt
    };

    /// <summary>
    /// 将数据转换为助手发布。
    /// </summary>
    private static AssistantPublicationPointer ToDomain(AssistantPublicationPointerEntity row)
        => AssistantPublicationPointer.Rehydrate(
            row.Id,
            row.AssistantKey,
            row.AssistantId,
            row.CurrentPublishedVersionId,
            row.CurrentVersionNo,
            row.PointerRevision,
            (AssistantPublicationPointerStatus)row.Status);

    /// <summary>
    /// 将数据转换为助手发布。
    /// </summary>
    private static AssistantPublishedVersion ToDomain(AssistantPublishedVersionEntity row)
        => AssistantPublishedVersion.Rehydrate(
            row.Id,
            row.AssistantKey,
            row.AssistantId,
            row.VersionNo,
            row.Name,
            row.Description,
            row.SnapshotJson,
            row.SnapshotHash,
            row.IsRevoked,
            row.RevokedAt,
            row.PublishedAt);

    /// <summary>
    /// 表示发布指针在提交前已被其他操作修改。
    /// </summary>
    public sealed class PointerRevisionConflictException : Exception
    {
        /// <summary>
        /// 接收并保存指针修订冲突执行所需的依赖，建立后续操作使用的运行状态。
        /// </summary>
        public PointerRevisionConflictException() : base("assistant_publication_pointer_revision_conflict") { }
    }

    /// <summary>
    /// 表示发布版本在撤回写入时发生状态冲突。
    /// </summary>
    public sealed class VersionRevokeConflictException : Exception
    {
        /// <summary>
        /// 接收并保存版本撤回冲突执行所需的依赖，建立后续操作使用的运行状态。
        /// </summary>
        public VersionRevokeConflictException() : base("assistant_publication_version_revoke_conflict") { }
    }
}
