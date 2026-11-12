#nullable enable
using System.Text.Json;

namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 发布时冻结的工具定义，包含契约版本和参数结构。
/// </summary>
public sealed record AssistantFrozenTool(
    string ToolId,
    string ContractVersion,
    string Name,
    string Description,
    JsonElement InputSchema);
