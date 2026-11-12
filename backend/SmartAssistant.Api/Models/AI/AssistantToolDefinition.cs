#nullable enable
using System.Text.Json;

namespace SmartAssistant.Models.AI;

/// <summary>
/// 工具的名称、用途和输入结构，供模型声明与运行时校验使用。
/// </summary>
public sealed record AssistantToolDefinition(
    string ToolId,
    string ContractVersion,
    string Name,
    string Description,
    JsonElement InputSchema);
