#nullable enable
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Models.Transport;

/// <summary>
/// 封装元数据快照相关的内部状态或数据，供所属组件完成相应处理。
/// </summary>
internal sealed record AssistantRedisMetaSnapshot(
    string TurnId,
    long Sequence,
    AssistantRunStatus Status,
    bool Terminal);
