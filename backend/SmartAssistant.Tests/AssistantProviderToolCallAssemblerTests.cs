// Spec: specs/136-public-assistant/requests/REQ-20260925-004-public-assistant-s2-chat-runtime-admission.md,
// S2-C-02 deterministic streamed tool-call argument assembly.
#nullable enable
using System;
using SmartAssistant.Services.Infrastructure.AI;
using SmartAssistant.Services.Contracts;
using SmartAssistant.Platform;
using Xunit;
using SmartAssistant.Models.AI;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证助手模型供应商工具调用组装器行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantProviderToolCallAssemblerTests
{
    /// <summary>
    /// 验证分段工具参数按索引组装，并规范化为有效 JSON。
    /// </summary>
    [Fact]
    public void Split_deltas_assemble_in_index_order_and_normalize_json()
    {
        var assembler = new AssistantProviderToolCallAssembler();

        assembler.Append(new AssistantProviderToolCallDelta(
            1,
            "call-2",
            "public.schedule.list.read",
            """{"locale":"zh"""));
        assembler.Append(new AssistantProviderToolCallDelta(
            0,
            "call-1",
            "public.event.overview.read",
            """{"eventId":"event-1"}"""));
        assembler.Append(new AssistantProviderToolCallDelta(
            1,
            null,
            null,
            """-CN"}"""));

        var completed = assembler.CompleteAll();

        Assert.Equal(2, completed.Count);
        Assert.Equal(0, completed[0].Index);
        Assert.Equal("call-1", completed[0].ToolCallId);
        Assert.Equal("{\"eventId\":\"event-1\"}", completed[0].ArgumentsJson);
        Assert.Equal(1, completed[1].Index);
        Assert.Equal("{\"locale\":\"zh-CN\"}", completed[1].ArgumentsJson);
    }

    /// <summary>
    /// 验证工具参数不完整或无效时无法完成工具调用。
    /// </summary>
    [Fact]
    public void Incomplete_or_invalid_arguments_fail_closed_at_completion()
    {
        var assembler = new AssistantProviderToolCallAssembler();
        assembler.Append(new AssistantProviderToolCallDelta(
            0,
            "call-1",
            "public.event.overview.read",
            """{"eventId":"""));

        var error = Assert.Throws<BizResultException>(() => assembler.Complete(0));
        Assert.Equal("assistant_tool_call_arguments_invalid_json", error.BusinessMessage);
    }

    /// <summary>
    /// 验证缺少供应商调用标识的工具调用不会被当作已完成结果。
    /// </summary>
    [Fact]
    public void Missing_provider_identity_is_not_accepted_as_a_completed_call()
    {
        var assembler = new AssistantProviderToolCallAssembler();
        assembler.Append(new AssistantProviderToolCallDelta(
            0,
            null,
            null,
            "{}"));

        var error = Assert.Throws<BizResultException>(() => assembler.Complete(0));
        Assert.Equal("assistant_tool_call_id_missing", error.BusinessMessage);
    }
}
