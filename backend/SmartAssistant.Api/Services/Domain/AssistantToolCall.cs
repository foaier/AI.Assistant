#nullable enable
using SmartAssistant.Models.Runtime;
using SmartAssistant.Platform;

namespace SmartAssistant.Services.Domain;

/// <summary>
/// 记录一次工具调用的输入、执行状态、输出及拒绝或失败原因。
/// </summary>
public sealed class AssistantToolCall
{
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AssistantToolCall() { }

    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; private set; } = string.Empty;
    /// <summary>
    /// 关联产生此事件、消息或工具调用的执行运行。
    /// </summary>
    public string RunId { get; private set; } = string.Empty;
    /// <summary>
    /// 关联当前使用的工具定义。
    /// </summary>
    public string ToolId { get; private set; } = string.Empty;
    /// <summary>
    /// 表示助手工具调用的契约版本信息。
    /// </summary>
    public string ContractVersion { get; private set; } = string.Empty;
    /// <summary>
    /// 保存工具调用输入的摘要。
    /// </summary>
    public string InputHash { get; private set; } = string.Empty;
    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    public AssistantToolCallStatus Status { get; private set; }
    /// <summary>
    /// 保存工具执行结果的 JSON 内容。
    /// </summary>
    public string? OutputJson { get; private set; }
    /// <summary>
    /// 保存运行或索引失败的稳定错误码。
    /// </summary>
    public string? FailureCode { get; private set; }
    /// <summary>
    /// 保存工具执行实际耗时（毫秒），未结束的调用不参与耗时统计。
    /// </summary>
    public double? DurationMs { get; private set; }

    /// <summary>
    /// 记录处理完成或进入终态的时间。
    /// </summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// 根据校验后的输入创建助手工具调用领域对象，并初始化业务状态。
    /// </summary>
    public static AssistantToolCall Create(
        string id,
        string runId,
        string toolId,
        string contractVersion,
        string inputHash)
        => new()
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            RunId = AssistantRuntimeGuard.Required(runId, "RunId"),
            ToolId = AssistantRuntimeGuard.Required(toolId, "ToolId", 160),
            ContractVersion = AssistantRuntimeGuard.Required(contractVersion, "ContractVersion", 64),
            InputHash = AssistantRuntimeGuard.Required(inputHash, "InputHash", 128),
            Status = AssistantToolCallStatus.Requested
        };

    /// <summary>
    /// 从持久化数据还原助手工具调用的标识、访问范围和生命周期状态。
    /// </summary>
    public static AssistantToolCall Rehydrate(
        string id,
        string runId,
        string toolId,
        string contractVersion,
        string inputHash,
        AssistantToolCallStatus status,
        string? outputJson,
        string? failureCode,
        DateTime? completedAt,
        double? durationMs = null)
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
            CompletedAt = completedAt,
            DurationMs = durationMs
        };

    /// <summary>
    /// 记录终态工具调用的单调时钟耗时，避免系统时间调整影响统计。
    /// </summary>
    public void RecordDuration(TimeSpan duration)
    {
        if (Status is not (AssistantToolCallStatus.Completed or AssistantToolCallStatus.Failed or AssistantToolCallStatus.Denied))
            throw new InvalidOperationException("Tool call must be terminal before recording duration.");
        DurationMs = Math.Max(0, duration.TotalMilliseconds);
    }

    /// <summary>
    /// 将助手工具调用推进到执行中状态。
    /// </summary>
    public void Start()
    {
        if (Status != AssistantToolCallStatus.Requested)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_tool_call_not_requested");
        Status = AssistantToolCallStatus.Running;
    }

    /// <summary>
    /// 将助手工具调用标记为成功完成，并记录完成时间。
    /// </summary>
    public void Complete(string outputJson, DateTime now)
    {
        if (Status != AssistantToolCallStatus.Running)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_tool_call_not_running");
        OutputJson = AssistantRuntimeGuard.NormalizeJson(outputJson, "OutputJson");
        Status = AssistantToolCallStatus.Completed;
        CompletedAt = now;
    }

    /// <summary>
    /// 将助手工具调用标记为已拒绝，并记录状态转换时间。
    /// </summary>
    public void Deny(string failureCode, DateTime now)
        => CompleteTerminal(AssistantToolCallStatus.Denied, failureCode, now);

    /// <summary>
    /// 将助手工具调用标记为失败，并保留失败状态供调用方查询。
    /// </summary>
    public void Fail(string failureCode, DateTime now)
        => CompleteTerminal(AssistantToolCallStatus.Failed, failureCode, now);

    /// <summary>
    /// 将领域对象写入指定终态，并保存最终结果及状态转换时间。
    /// </summary>
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
