#nullable enable
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Models.Transport;

/// <summary>
/// 封装票据相关的内部状态或数据，供所属组件完成相应处理。
/// </summary>
internal sealed class AssistantStreamTicketPayload
{
    /// <summary>
    /// 表示票据的版本信息。
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// 记录流票据的用途，以便校验其允许执行的操作。
    /// </summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    public string AssistantId { get; init; } = string.Empty;

    /// <summary>
    /// 绑定创建此会话或运行时使用的固定发布版本。
    /// </summary>
    public string PublishedVersionId { get; init; } = string.Empty;

    /// <summary>
    /// 关联创建该对话或请求的匿名会话。
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// 关联此消息、轮次或运行所属的对话。
    /// </summary>
    public string ConversationId { get; init; } = string.Empty;

    /// <summary>
    /// 关联此记录所属的用户请求轮次。
    /// </summary>
    public string TurnId { get; init; } = string.Empty;

    /// <summary>
    /// 关联产生此事件、消息或工具调用的执行运行。
    /// </summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>
    /// 区分公开执行和管理端预览执行。
    /// </summary>
    public AssistantExecutionMode ExecutionMode { get; init; }

    /// <summary>
    /// 表示票据的传输信息。
    /// </summary>
    public AssistantTransportKind[] AllowedTransports { get; init; } = Array.Empty<AssistantTransportKind>();

    /// <summary>
    /// 以 Unix 秒数记录票据失效时间。
    /// </summary>
    public long ExpiresAtUnixSeconds { get; init; }

    /// <summary>
    /// 保存票据中的随机标识，使不同签发结果具有不同的负载。
    /// </summary>
    public string Nonce { get; init; } = string.Empty;
}
