#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20260928-007-public-assistant-knowledge-qna-asset-analytics.md
// Focused proof for Q&A input normalization used by the exact-match direct-answer path.
using SmartAssistant.Modules.Application;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantKnowledgeTests
{
    [Theory]
    [InlineData("  活动  什么时候开始？ ", "活动 什么时候开始?")]
    [InlineData("ＡＢＣ", "ABC")]
    [InlineData("报名\t方式", "报名 方式")]
    public void NormalizeQuestion_is_stable_for_exact_match(string input, string expected)
    {
        Assert.Equal(expected.ToUpperInvariant(), AssistantKnowledgeService.NormalizeQuestion(input));
    }

    [Fact]
    public void NormalizeQuestion_collapses_equivalent_spacing()
    {
        Assert.Equal(
            AssistantKnowledgeService.NormalizeQuestion("如何  报名？"),
            AssistantKnowledgeService.NormalizeQuestion("  如何\t报名？  "));
    }
}
