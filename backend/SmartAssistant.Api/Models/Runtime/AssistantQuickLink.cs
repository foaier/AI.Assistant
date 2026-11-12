#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 公开聊天页面展示的快捷链接及标题。
/// </summary>
public sealed record AssistantQuickLink(
    string Id,
    string Label,
    string Url,
    string Icon = "link",
    string? IconUrl = null);
