#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 文档分块策略值对象，校验片段大小、重叠和标题处理约束。
/// </summary>
public sealed record AssistantKnowledgeChunkingPolicy(
    AssistantKnowledgeChunkingMode Mode,
    int MaxChunkSize,
    int MinChunkSize,
    int OverlapSize,
    int MaxHeadingLevel,
    bool PreserveHeadingPath,
    bool MergeShortChunks,
    long Revision)
{
    /// <summary>
    /// 保存知识分块策略使用的默认设置。
    /// </summary>
    public static AssistantKnowledgeChunkingPolicy Default { get; } = new(AssistantKnowledgeChunkingMode.Hybrid, 1200, 200, 160, 3, true, true, 1);

    /// <summary>
    /// 根据分块策略生成稳定标识，用于识别需要重新处理的文档。
    /// </summary>
    public string StrategyKey
    {
        get
        {
            var canonical = JsonSerializer.Serialize(new
            {
                algorithm = "chunker-v1",
                mode = Mode.ToString().ToLowerInvariant(),
                maxChunkSize = MaxChunkSize,
                minChunkSize = MinChunkSize,
                overlapSize = OverlapSize,
                maxHeadingLevel = MaxHeadingLevel,
                preserveHeadingPath = PreserveHeadingPath,
                mergeShortChunks = MergeShortChunks
            });
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            return $"{Mode.ToString().ToLowerInvariant()}-{hash[..16]}";
        }
    }

    /// <summary>
    /// 校验片段长度、重叠大小及标题层级等分块参数之间的约束。
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        if (MaxChunkSize is < 200 or > 8000) throw new ArgumentOutOfRangeException(nameof(MaxChunkSize));
        if (MinChunkSize < 0 || MinChunkSize > MaxChunkSize) throw new ArgumentOutOfRangeException(nameof(MinChunkSize));
        if (OverlapSize < 0 || OverlapSize >= MaxChunkSize) throw new ArgumentOutOfRangeException(nameof(OverlapSize));
        if (MaxHeadingLevel is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(MaxHeadingLevel));
    }

    /// <summary>
    /// 复制当前分块策略并替换修订号，其他策略设置保持不变。
    /// </summary>
    public AssistantKnowledgeChunkingPolicy WithRevision(long revision) => this with { Revision = revision };
}
