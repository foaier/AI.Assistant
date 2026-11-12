#nullable enable
namespace AI.Assistant.Models.Policies;

/// <summary>
/// 保存联网搜索设置并检测并发修改的命令。
/// </summary>
public sealed record SaveAssistantWebSearchConfigCommand(
    AssistantWebSearchSettings Settings,
    long ExpectedRevision);
