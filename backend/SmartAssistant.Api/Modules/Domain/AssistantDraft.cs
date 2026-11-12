#nullable enable
using System;
using System.Text.Json;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 可编辑的助手配置聚合，维护草稿配置与乐观并发修订号。
/// </summary>
public sealed class AssistantDraft
{
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AssistantDraft() { }

    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; private set; } = string.Empty;
    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    public string Name { get; private set; } = string.Empty;
    /// <summary>
    /// 记录此资源的可选说明。
    /// </summary>
    public string? Description { get; private set; }
    /// <summary>
    /// 尚未发布的可编辑配置 JSON；公开运行使用独立冻结的发布快照。
    /// </summary>
    public string DraftJson { get; private set; } = "{}";
    /// <summary>
    /// 草稿的乐观并发修订号，每次成功保存后递增。
    /// </summary>
    public long DraftRevision { get; private set; }
    /// <summary>
    /// 客户端提供的创建幂等键，用于避免重复创建相同草稿。
    /// </summary>
    public string? CreationIdempotencyKey { get; private set; }

    /// <summary>
    /// 根据校验后的输入创建助手草稿领域对象，并初始化修订号。
    /// </summary>
    public static AssistantDraft Create(
        string id, string name, string? description,
        string draftJson, string actorUserId, string? idempotencyKey)
    {
        RequireScope(actorUserId);
        var draft = new AssistantDraft
        {
            Id = Require(id, "Id"),
            Name = NormalizeName(name),
            Description = description?.Trim(),
            DraftJson = NormalizeJson(draftJson),
            DraftRevision = 1,
            CreationIdempotencyKey = NormalizeOptional(idempotencyKey)
        };
        return draft;
    }

    /// <summary>
    /// 从持久化数据还原助手草稿的标识、访问范围和生命周期状态。
    /// </summary>
    public static AssistantDraft Rehydrate(
        string id, string name, string? description,
        string draftJson, long revision, string? idempotencyKey)
        => new()
        {
            Id = id,
            Name = name,
            Description = description,
            DraftJson = draftJson,
            DraftRevision = revision,
            CreationIdempotencyKey = idempotencyKey
        };

    /// <summary>
    /// 校验预期草稿修订号后更新配置，并递增修订号。
    /// </summary>
    public void Save(string name, string? description, string draftJson, long expectedRevision, string actorUserId)
    {
        RequireScope(actorUserId);
        if (expectedRevision != DraftRevision)
            throw new BizResultException(BusinessCodes.Conflict, $"assistant_draft_revision_conflict:{DraftRevision}");
        Name = NormalizeName(name);
        Description = description?.Trim();
        DraftJson = NormalizeJson(draftJson);
        DraftRevision++;
    }

    /// <summary>
    /// 校验操作人标识已提供。
    /// </summary>
    private static void RequireScope(string actorUserId)
    {
        _ = Require(actorUserId, "ActorUserId");
    }

    /// <summary>
    /// 校验必填文本并返回去除首尾空白后的值；内容缺失时报告输入错误。
    /// </summary>
    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();

    /// <summary>
    /// 将输入规范化为名称，并统一相关格式。
    /// </summary>
    private static string NormalizeName(string? value)
    {
        var normalized = Require(value, "Name");
        if (normalized.Length > 120)
            throw new BizResultException(BusinessCodes.BusinessException, "Name too long");
        return normalized;
    }

    /// <summary>
    /// 将配置文本解析并重新序列化为规范 JSON；无效 JSON 按业务校验错误拒绝。
    /// </summary>
    private static string NormalizeJson(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        try
        {
            using var document = JsonDocument.Parse(normalized);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw new BizResultException(BusinessCodes.BusinessException, "DraftJson must be valid JSON");
        }
    }

    /// <summary>
    /// 规范化可选字符串；空白内容转换为空值，并校验长度限制。
    /// </summary>
    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
