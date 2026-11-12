#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20260928-007-public-assistant-knowledge-qna-asset-analytics.md
// Focused proof for Q&A input normalization used by the exact-match direct-answer path.
using AI.Assistant.Services.Application;
using Xunit;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证助手知识行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantKnowledgeTests
{
    /// <summary>
    /// 验证问题规范化结果可稳定用于精确匹配。
    /// </summary>
    [Theory]
    [InlineData("  活动  什么时候开始？ ", "活动 什么时候开始?")]
    [InlineData("ＡＢＣ", "ABC")]
    [InlineData("报名\t方式", "报名 方式")]
    public void NormalizeQuestion_is_stable_for_exact_match(string input, string expected)
    {
        Assert.Equal(expected.ToUpperInvariant(), AssistantKnowledgeService.NormalizeQuestion(input));
    }

    /// <summary>
    /// 验证等价空白形式会规范化为相同问题。
    /// </summary>
    [Fact]
    public void NormalizeQuestion_collapses_equivalent_spacing()
    {
        Assert.Equal(
            AssistantKnowledgeService.NormalizeQuestion("如何  报名？"),
            AssistantKnowledgeService.NormalizeQuestion("  如何\t报名？  "));
    }
}
