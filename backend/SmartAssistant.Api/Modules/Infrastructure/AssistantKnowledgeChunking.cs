#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using SmartAssistant.Modules.Contracts;

namespace SmartAssistant.Modules.Infrastructure;

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
    public static AssistantKnowledgeChunkingPolicy Default { get; } = new(
        AssistantKnowledgeChunkingMode.Hybrid, 1200, 200, 160, 3, true, true, 1);

    public string StrategyKey
    {
        get
        {
            var canonical = JsonSerializer.Serialize(new
            {
                algorithm = "chunker-v1", mode = Mode.ToString().ToLowerInvariant(),
                maxChunkSize = MaxChunkSize, minChunkSize = MinChunkSize,
                overlapSize = OverlapSize, maxHeadingLevel = MaxHeadingLevel,
                preserveHeadingPath = PreserveHeadingPath, mergeShortChunks = MergeShortChunks
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

    public AssistantKnowledgeChunkingPolicy WithRevision(long revision) => this with { Revision = revision };
}

/// <summary>
/// 按标题、固定长度或混合策略将文档正文切分为可检索片段。
/// </summary>
public static class AssistantKnowledgeChunker
{
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
            ? new[] { new Section(string.Empty, normalized) }
            : BuildSections(normalized, policy);
        var result = new List<string>();
        foreach (var section in sections)
        {
            var prefix = policy.PreserveHeadingPath && !string.IsNullOrWhiteSpace(section.Path)
                ? section.Path + "\n\n" : string.Empty;
            var body = section.Text.Trim();
            if (body.Length == 0) continue;
            var shouldSplit = policy.Mode != AssistantKnowledgeChunkingMode.Heading;
            if (shouldSplit && prefix.Length > policy.MaxChunkSize / 3)
                prefix = prefix[..Math.Max(0, policy.MaxChunkSize / 3)].TrimEnd() + "\n\n";
            var available = Math.Max(1, policy.MaxChunkSize - prefix.Length);
            var effectiveOverlap = Math.Min(policy.OverlapSize, available / 2);
            var pieces = shouldSplit ? SplitBySize(body, available, effectiveOverlap) : new[] { body };
            foreach (var piece in pieces) result.Add((prefix + piece).Trim());
        }
        if (policy.MergeShortChunks && policy.MinChunkSize > 0)
            result = MergeShort(result, policy.MinChunkSize, policy.MaxChunkSize);
        return result.Where(x => x.Length > 0).ToArray();
    }

    private static IReadOnlyList<Section> BuildSections(string text, AssistantKnowledgeChunkingPolicy policy)
    {
        var sections = new List<Section>();
        var path = new List<(int Level, string Text)>();
        var body = new List<string>();
        void Flush() { var value = string.Join('\n', body).Trim(); if (value.Length > 0) sections.Add(new Section(string.Join(" / ", path.Select(x => x.Text)), value)); body.Clear(); }
        foreach (var line in text.Split('\n'))
        {
            var match = Heading.Match(line);
            if (!match.Success || match.Groups["marks"].Value.Length > policy.MaxHeadingLevel) { body.Add(line); continue; }
            Flush();
            var level = match.Groups["marks"].Value.Length;
            while (path.Count > 0 && path[^1].Level >= level) path.RemoveAt(path.Count - 1);
            path.Add((level, match.Groups["text"].Value.Trim()));
        }
        Flush();
        return sections.Count == 0 ? new[] { new Section(string.Empty, text) } : sections;
    }

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

    private sealed record Section(string Path, string Text);
}
