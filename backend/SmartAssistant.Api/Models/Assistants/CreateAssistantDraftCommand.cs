#nullable enable
namespace SmartAssistant.Models.Assistants;

/// <summary>
/// 创建助手草稿的命令，包含配置和可选的创建幂等键。
/// </summary>
public sealed record CreateAssistantDraftCommand(
    string Name,
    string? Description,
    string DraftJson,
    string? IdempotencyKey);
