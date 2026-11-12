#nullable enable
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 根据公开助手标识解析当前有效的发布版本及其授权范围。
/// </summary>
public sealed class AssistantPublicAuthorityResolver : IAssistantPublicAuthorityResolver
{
    /// <summary>
    /// 保存助手发布仓储依赖，供助手公开授权解析器执行相关操作。
    /// </summary>
    private readonly IAssistantPublicationRepository _publications;

    /// <summary>
    /// 接收并保存助手公开授权解析器执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantPublicAuthorityResolver(IAssistantPublicationRepository publications)
        => _publications = publications;

    /// <summary>
    /// 按公开助手标识解析有效的发布快照及运行授权范围；不可用时返回空值。
    /// </summary>
    public async Task<AssistantPublicAuthority?> ResolveAsync(
        string assistantKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assistantKey))
            return null;

        var current = await _publications.GetCurrentByAssistantKeyAsync(assistantKey.Trim(), cancellationToken);
        if (current is null)
            return null;

        var (pointer, version) = current.Value;
        if (version.IsRevoked || pointer.CurrentPublishedVersionId is null)
            return null;

        using var document = JsonDocument.Parse(version.SnapshotJson);
        return new AssistantPublicAuthority(
            version.AssistantKey,
            new AssistantScopeSnapshot(
                version.AssistantId,
                version.Id,
                AssistantExecutionMode.Public),
            version.Name,
            version.Description,
            document.RootElement.Clone(),
            version.VersionNo,
            version.SnapshotHash);
    }
}
