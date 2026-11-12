#nullable enable
using System.Text.RegularExpressions;
using SmartAssistant.Models.Knowledge;

namespace SmartAssistant.Services.Infrastructure;

/// <summary>
/// 按标题、固定长度或混合策略将文档正文切分为可检索片段。
/// </summary>
public static class AssistantKnowledgeChunker
{
    /// <summary>
    /// 用于识别 Markdown 文档标题行的正则表达式。
    /// </summary>
    private static readonly Regex Heading = new("^(?<marks>#{1,6})\\s+(?<text>.+?)\\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 按照指定分块模式生成有序文本片段，保留配置要求的标题路径和重叠内容。
    /// </summary>
    public static IReadOnlyList<string> Split(string text, AssistantKnowledgeChunkingPolicy policy)
    {
        policy.Validate();
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length == 0) return Array.Empty<string>();
        var sections = policy.Mode == AssistantKnowledgeChunkingMode.Fixed
            ? new[] { new AssistantKnowledgeSection(string.Empty, normalized) }
            : BuildSections(normalized, policy);
        var result = new List<string>();
        foreach (var section in sections)
        {
            var prefix = policy.PreserveHeadingPath && !string.IsNullOrWhiteSpace(section.Path)
                ? section.Path + "\n\n" : string.Empty;
            var body = section.Text.Trim();
            if (body.Length == 0)
                continue;
            var shouldSplit = policy.Mode != AssistantKnowledgeChunkingMode.Heading;
            if (shouldSplit && prefix.Length > policy.MaxChunkSize / 3)
                prefix = prefix[..Math.Max(0, policy.MaxChunkSize / 3)].TrimEnd() + "\n\n";
            var available = Math.Max(1, policy.MaxChunkSize - prefix.Length);
            var effectiveOverlap = Math.Min(policy.OverlapSize, available / 2);
            var pieces = shouldSplit ? SplitBySize(body, available, effectiveOverlap) : new[] { body };
            foreach (var piece in pieces)
                result.Add((prefix + piece).Trim());
        }
        if (policy.MergeShortChunks && policy.MinChunkSize > 0)
            result = MergeShort(result, policy.MinChunkSize, policy.MaxChunkSize);
        return result.Where(x => x.Length > 0).ToArray();
    }

    /// <summary>
    /// 按 Markdown 标题层级整理正文分段，并记录每段的标题路径。
    /// </summary>
    private static IReadOnlyList<AssistantKnowledgeSection> BuildSections(string text, AssistantKnowledgeChunkingPolicy policy)
    {
        var sections = new List<AssistantKnowledgeSection>();
        var path = new List<(int Level, string Text)>();
        var body = new List<string>();
        /*
         * 将当前正文缓冲区整理为文档分段，并清空缓冲区以继续读取后续内容。
         */
        void Flush()
        {
            var value = string.Join('\n', body).Trim();
            if (value.Length > 0)
                sections.Add(new AssistantKnowledgeSection(string.Join(" / ", path.Select(x => x.Text)), value));
            body.Clear();
        }
        foreach (var line in text.Split('\n'))
        {
            var match = Heading.Match(line);
            if (!match.Success || match.Groups["marks"].Value.Length > policy.MaxHeadingLevel)
            {
                body.Add(line);
                continue;
            }
            Flush();
            var level = match.Groups["marks"].Value.Length;
            while (path.Count > 0 && path[^1].Level >= level)
            {
                path.RemoveAt(path.Count - 1);
            }
            path.Add((level, match.Groups["text"].Value.Trim()));
        }
        Flush();
        return sections.Count == 0 ? new[] { new AssistantKnowledgeSection(string.Empty, text) } : sections;
    }

    /// <summary>
    /// 按最大长度拆分正文，优先在换行处断开，并保留相邻片段的重叠内容。
    /// </summary>
    private static IReadOnlyList<string> SplitBySize(string text, int size, int overlap)
    {
        if (text.Length <= size) return new[] { text };
        var result = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(size, text.Length - start);
            var end = start + length;
            if (end < text.Length)
            {
                var boundary = text.LastIndexOf('\n', end - 1, length);
                if (boundary > start + size / 2) end = boundary;
            }
            var piece = text[start..end].Trim();
            if (piece.Length > 0) result.Add(piece);
            if (end >= text.Length) break;
            start = Math.Max(start + 1, end - overlap);
        }
        return result;
    }

    /// <summary>
    /// 合并长度不足的相邻片段，同时遵守配置的最大片段长度。
    /// </summary>
    private static List<string> MergeShort(List<string> input, int minimum, int maximum)
    {
        var result = new List<string>();
        foreach (var item in input)
        {
            if (result.Count > 0 && item.Length < minimum && result[^1].Length + item.Length + 2 <= maximum)
                result[^1] += "\n\n" + item;
            else if (result.Count > 0 && result[^1].Length < minimum && result[^1].Length + item.Length + 2 <= maximum)
                result[^1] += "\n\n" + item;
            else result.Add(item);
        }
        return result;
    }
}
