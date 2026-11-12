#nullable enable
using System.Linq;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantKnowledgeChunkingTests
{
    // Spec: REQ-20261006-009; preview and indexing share these semantics.
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

    [Fact]
    public void Very_long_heading_paths_do_not_exceed_size_or_make_one_character_chunks()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with { MaxChunkSize = 200, MinChunkSize = 0, OverlapSize = 160 };
        var chunks = AssistantKnowledgeChunker.Split("# " + new string('题', 300) + "\n" + new string('文', 400), policy);
        Assert.InRange(chunks.Count, 2, 30);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 200));
    }

    [Fact]
    public void Skipped_heading_levels_do_not_make_sibling_titles_children()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with { Mode = AssistantKnowledgeChunkingMode.Heading, MinChunkSize = 0 };
        var chunks = AssistantKnowledgeChunker.Split("# 总章\n## 第一节\n### 小节\n# 第二章\n### 同级甲\n甲\n### 同级乙\n乙", policy);
        Assert.Contains("第二章 / 同级乙", chunks.Last());
        Assert.DoesNotContain("同级甲 / 同级乙", chunks.Last());
    }

    [Fact]
    public void Short_final_section_is_merged_when_enabled()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with { Mode = AssistantKnowledgeChunkingMode.Heading, MinChunkSize = 100, MaxChunkSize = 500 };
        var text = "# 第一节\n" + new string('长', 150) + "\n# 第二节\n短内容";
        Assert.Single(AssistantKnowledgeChunker.Split(text, policy));
        Assert.Equal(2, AssistantKnowledgeChunker.Split(text, policy with { MergeShortChunks = false }).Count);
    }

    [Fact]
    public void Invalid_mode_is_rejected()
        => Assert.Throws<System.ArgumentOutOfRangeException>(() => AssistantKnowledgeChunker.Split("text",
            AssistantKnowledgeChunkingPolicy.Default with { Mode = (AssistantKnowledgeChunkingMode)99 }));

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

    [Fact]
    public void Invalid_overlap_is_rejected()
    {
        var policy = AssistantKnowledgeChunkingPolicy.Default with { OverlapSize = 1200 };
        Assert.Throws<System.ArgumentOutOfRangeException>(() => AssistantKnowledgeChunker.Split("text", policy));
    }
}
