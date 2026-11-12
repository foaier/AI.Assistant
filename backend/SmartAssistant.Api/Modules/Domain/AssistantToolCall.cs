#nullable enable
using System;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 记录一次工具调用的输入、执行状态、输出及拒绝或失败原因。
/// </summary>
public sealed class AssistantToolCall
{
    private AssistantToolCall() { }

    public string Id { get; private set; } = string.Empty;
    public string RunId { get; private set; } = string.Empty;
    public string ToolId { get; private set; } = string.Empty;
    public string ContractVersion { get; private set; } = string.Empty;
    public string InputHash { get; private set; } = string.Empty;
    public AssistantToolCallStatus Status { get; private set; }
    public string? OutputJson { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public static AssistantToolCall Create(
        string id,
        string runId,
        string toolId,
        string contractVersion,
        string inputHash,
        DateTime now)
        => new()
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            RunId = AssistantRuntimeGuard.Required(runId, "RunId"),
            ToolId = AssistantRuntimeGuard.Required(toolId, "ToolId", 160),
            ContractVersion = AssistantRuntimeGuard.Required(contractVersion, "ContractVersion", 64),
            InputHash = AssistantRuntimeGuard.Required(inputHash, "InputHash", 128),
            Status = AssistantToolCallStatus.Requested,
            CreatedAt = now
        };

    public static AssistantToolCall Rehydrate(
        string id,
        string runId,
        string toolId,
        string contractVersion,
        string inputHash,
        AssistantToolCallStatus status,
        string? outputJson,
        string? failureCode,
        DateTime createdAt,
        DateTime? completedAt)
        => new()
        {
            Id = id,
            RunId = runId,
            ToolId = toolId,
            ContractVersion = contractVersion,
            InputHash = inputHash,
            Status = status,
            OutputJson = outputJson,
            FailureCode = failureCode,
            CreatedAt = createdAt,
            CompletedAt = completedAt
        };

    public void Start()
    {
        if (Status != AssistantToolCallStatus.Requested)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_tool_call_not_requested");
        Status = AssistantToolCallStatus.Running;
    }

    public void Complete(string outputJson, DateTime now)
    {
        if (Status != AssistantToolCallStatus.Running)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_tool_call_not_running");
        OutputJson = AssistantRuntimeGuard.NormalizeJson(outputJson, "OutputJson");
        Status = AssistantToolCallStatus.Completed;
        CompletedAt = now;
    }

    public void Deny(string failureCode, DateTime now)
        => CompleteTerminal(AssistantToolCallStatus.Denied, failureCode, now);

    public void Fail(string failureCode, DateTime now)
        => CompleteTerminal(AssistantToolCallStatus.Failed, failureCode, now);

    private void CompleteTerminal(AssistantToolCallStatus terminalStatus, string failureCode, DateTime now)
    {
        if (Status is AssistantToolCallStatus.Completed
            or AssistantToolCallStatus.Denied
            or AssistantToolCallStatus.Failed)
        {
            throw new BizResultException(BusinessCodes.Conflict, "assistant_tool_call_terminal_already_set");
        }

        Status = terminalStatus;
        FailureCode = AssistantRuntimeGuard.Required(failureCode, "FailureCode", 160);
        CompletedAt = now;
    }
}
