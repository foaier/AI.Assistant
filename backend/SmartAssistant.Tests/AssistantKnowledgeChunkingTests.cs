#nullable enable
using System.Linq;
using SmartAssistant.Services.Infrastructure;
using SmartAssistant.Services.Contracts;
using Xunit;
using SmartAssistant.Models.Knowledge;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证助手知识分块行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantKnowledgeChunkingTests
{
    // Spec: REQ-20261006-009; preview and indexing share these semantics.
    /// <summary>
    /// 验证修改任何分块规则都会改变策略标识，重复保存相同规则则保持稳定。
    /// </summary>
    [Fact]
    public void Every_rule_option_changes_the_strategy_but_resaving_does_not()
    {
        var original = AssistantKnowledgeChunkingPolicy.Default;
        foreach (var changed in new[]
        {
            original with { MinChunkSize = 100 }, original with { PreserveHeadingPath = false },
            original with { MergeShortChunks = false }, original with { MaxHeadingLevel = 4 },
            original with { MaxChunkSize = 1300 }, original with { OverlapSize = 100 },
            original with { Mode = AssistantKnowledgeChunkingMode.Fixed }
        }) Assert.NotEqual(original.StrategyKey, changed.StrategyKey);
        Assert.Equal(original.StrategyKey, original.WithRevision(2).StrategyKey);
    }

    /// <summary>
    /// 验证标题模式保留完整章节，混合模式会继续切分长章节。
    /// </summary>
    [Fact]
    public void Heading_keeps_a_complete_section_while_hybrid_splits_long_sections()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with { MaxChunkSize = 200, MinChunkSize = 0, OverlapSize = 20 };
        var text = "# 标题\n" + new string('文', 500);
        Assert.Single(AssistantKnowledgeChunker.Split(text, policy with { Mode = AssistantKnowledgeChunkingMode.Heading }));
        var hybrid = AssistantKnowledgeChunker.Split(text, policy);
        Assert.True(hybrid.Count > 1);
        Assert.All(hybrid, chunk => Assert.InRange(chunk.Length, 1, 200));
    }

    /// <summary>
    /// 验证超长标题路径不会导致片段超长或退化为单字符片段。
    /// </summary>
    [Fact]
    public void Very_long_heading_paths_do_not_exceed_size_or_make_one_character_chunks()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with { MaxChunkSize = 200, MinChunkSize = 0, OverlapSize = 160 };
        var chunks = AssistantKnowledgeChunker.Split("# " + new string('题', 300) + "\n" + new string('文', 400), policy);
        Assert.InRange(chunks.Count, 2, 30);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 200));
    }

    /// <summary>
    /// 验证跳级标题不会将同级标题错误归为子标题。
    /// </summary>
    [Fact]
    public void Skipped_heading_levels_do_not_make_sibling_titles_children()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with { Mode = AssistantKnowledgeChunkingMode.Heading, MinChunkSize = 0 };
        var chunks = AssistantKnowledgeChunker.Split("# 总章\n## 第一节\n### 小节\n# 第二章\n### 同级甲\n甲\n### 同级乙\n乙", policy);
        Assert.Contains("第二章 / 同级乙", chunks.Last());
        Assert.DoesNotContain("同级甲 / 同级乙", chunks.Last());
    }

    /// <summary>
    /// 验证启用短片段合并后，末尾短章节能够并入相邻片段。
    /// </summary>
    [Fact]
    public void Short_final_section_is_merged_when_enabled()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with { Mode = AssistantKnowledgeChunkingMode.Heading, MinChunkSize = 100, MaxChunkSize = 500 };
        var text = "# 第一节\n" + new string('长', 150) + "\n# 第二节\n短内容";
        Assert.Single(AssistantKnowledgeChunker.Split(text, policy));
        Assert.Equal(2, AssistantKnowledgeChunker.Split(text, policy with { MergeShortChunks = false }).Count);
    }

    /// <summary>
    /// 验证无效分块模式被拒绝。
    /// </summary>
    [Fact]
    public void Invalid_mode_is_rejected()
        => Assert.Throws<System.ArgumentOutOfRangeException>(() => AssistantKnowledgeChunker.Split("text",
            AssistantKnowledgeChunkingPolicy.Default with { Mode = (AssistantKnowledgeChunkingMode)99 }));

    /// <summary>
    /// 验证固定分块模式遵守片段大小和重叠限制。
    /// </summary>
    [Fact]
    public void Fixed_mode_respects_size_and_overlap()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with
        {
            Mode = AssistantKnowledgeChunkingMode.Fixed, MaxChunkSize = 200, MinChunkSize = 0, OverlapSize = 20
        };
        var chunks = AssistantKnowledgeChunker.Split(new string('x', 420), policy);
        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 200));
        Assert.Contains(chunks[0][^20..], chunks[1]);
    }

    /// <summary>
    /// 验证标题分块模式在各片段中保留标题路径。
    /// </summary>
    [Fact]
    public void Heading_mode_keeps_heading_path_in_each_section()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with
        {
            Mode = AssistantKnowledgeChunkingMode.Heading, MaxChunkSize = 500, MinChunkSize = 0
        };
        var chunks = AssistantKnowledgeChunker.Split("# 活动\n开场说明\n## 报名\n报名方式", policy);
        Assert.Equal(2, chunks.Count);
        Assert.Contains("活动", chunks[1]);
        Assert.Contains("报名", chunks[1]);
    }

    /// <summary>
    /// 验证无效重叠长度被拒绝。
    /// </summary>
    [Fact]
    public void Invalid_overlap_is_rejected()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with { OverlapSize = 1200 };
        Assert.Throws<System.ArgumentOutOfRangeException>(() => AssistantKnowledgeChunker.Split("text", policy));
    }
}
