#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Infrastructure.AI;

/// <summary>
/// 已合并完毕的模型工具调用，包含完整的调用标识和参数。
/// </summary>
public sealed record AssistantAssembledToolCall(
    int Index,
    string ToolCallId,
    string Name,
    string ArgumentsJson);

/// <summary>
/// 按调用索引合并 OpenAI 兼容接口返回的工具参数增量。
/// 本类只负责组装，授权、参数校验与实际执行由工具注册表和编排器负责。
/// </summary>
public sealed class AssistantProviderToolCallAssembler
{
    private readonly Dictionary<int, MutableToolCall> _calls = new();

    /// <summary>
    /// 按工具调用索引合并名称和参数增量，并限制累积内容大小。
    /// </summary>
    public AssistantAssembledToolCall Append(AssistantProviderToolCallDelta delta)
    {
        if (delta.Index < 0)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_tool_call_index_invalid");

        if (!_calls.TryGetValue(delta.Index, out var call))
        {
            call = new MutableToolCall(delta.Index);
            _calls.Add(delta.Index, call);
        }

        call.ToolCallId ??= NormalizeOptional(delta.ToolCallId, 160);
        call.Name ??= NormalizeOptional(delta.Name, 160);
        if (!string.IsNullOrEmpty(delta.ArgumentsDelta))
            call.Arguments.Append(delta.ArgumentsDelta);

        return Snapshot(call, requireComplete: false);
    }

    /// <summary>
    /// 完成指定工具调用的参数组装，并校验调用标识、名称和参数格式。
    /// </summary>
    public AssistantAssembledToolCall Complete(int index)
    {
        if (!_calls.TryGetValue(index, out var call))
            throw new BizResultException(BusinessCodes.NotFound, "assistant_tool_call_not_found");

        return Snapshot(call, requireComplete: true);
    }

    /// <summary>
    /// 完成所有已收到的工具调用并返回可供编排器校验的完整请求。
    /// </summary>
    public IReadOnlyList<AssistantAssembledToolCall> CompleteAll()
    {
        var result = new List<AssistantAssembledToolCall>(_calls.Count);
        foreach (var call in _calls.Values)
            result.Add(Snapshot(call, requireComplete: true));
        result.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        return result;
    }

    private static AssistantAssembledToolCall Snapshot(
        MutableToolCall call,
        bool requireComplete)
    {
        var toolCallId = NormalizeOptional(call.ToolCallId, 160);
        var name = NormalizeOptional(call.Name, 160);
        if (requireComplete)
        {
            if (toolCallId is null)
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_tool_call_id_missing");
            if (name is null)
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_tool_call_name_missing");
            if (call.Arguments.Length == 0)
                throw new BizResultException(BusinessCodes.BusinessException, "assistant_tool_call_arguments_missing");
            try
            {
                using var document = JsonDocument.Parse(call.Arguments.ToString());
                return new AssistantAssembledToolCall(
                    call.Index,
                    toolCallId,
                    name,
                    JsonSerializer.Serialize(document.RootElement));
            }
            catch (JsonException)
            {
                throw new BizResultException(
                    BusinessCodes.BusinessException,
                    "assistant_tool_call_arguments_invalid_json");
            }
        }

        return new AssistantAssembledToolCall(
            call.Index,
            toolCallId ?? string.Empty,
            name ?? string.Empty,
            call.Arguments.ToString());
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_tool_call_value_too_long");
        return normalized;
    }

    private sealed class MutableToolCall
    {
        public MutableToolCall(int index) => Index = index;

        public int Index { get; }
        public string? ToolCallId { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
