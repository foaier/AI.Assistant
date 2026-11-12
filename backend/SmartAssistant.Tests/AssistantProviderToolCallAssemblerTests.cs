// Spec: specs/136-public-assistant/requests/REQ-20260925-004-public-assistant-s2-chat-runtime-admission.md,
// S2-C-02 deterministic streamed tool-call argument assembly.
#nullable enable
using System;
using SmartAssistant.Modules.Infrastructure.AI;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantProviderToolCallAssemblerTests
{
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
