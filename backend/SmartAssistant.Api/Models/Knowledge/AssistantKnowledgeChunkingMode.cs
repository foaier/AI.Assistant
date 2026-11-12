#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 知识文档的分块模式：按标题、固定长度或混合方式切分。
/// </summary>
public enum AssistantKnowledgeChunkingMode
{
    /// <summary>
    /// 按文档标题结构切分。
    /// </summary>
    Heading = 0,

    /// <summary>
    /// 按固定文本长度切分。
    /// </summary>
    Fixed = 1,

    /// <summary>
    /// 结合标题结构和片段长度的混合模式。
    /// </summary>
    Hybrid = 2
}
