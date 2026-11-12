#nullable enable
namespace AI.Assistant.Models.AI;

/// <summary>
/// 封装结果相关的内部状态或数据，供所属组件完成相应处理。
/// </summary>
internal sealed record OpenAiFlushResult(
    IReadOnlyList<AssistantProviderChunk> Chunks,
    bool Completed,
    bool Stop);
