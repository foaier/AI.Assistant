#nullable enable
using System.Text.Json;

namespace AI.Assistant.Models.Assistants;

/// <summary>
/// 管理端使用的助手详情，将草稿配置转换为可直接读取的 JSON 对象。
/// </summary>
public sealed record AssistantDto(
    string Id,
    string Name,
    string? Description,
    long DraftRevision,
    JsonElement Configuration);
