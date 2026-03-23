// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Observability;

/// <summary>
/// Unit tests for TraceId / SpanId / ParentSpanId fields on the AgentEvent base.
/// Verifies that span identity fields are correctly propagated via record `with` expressions
/// and that defaults are null so existing code is not broken.
/// </summary>
public class AgentEventSpanFieldsTests
{
    // ── Defaults are null (no breaking change) ────────────────────────────────

    [Fact]
    public void AgentEvent_DefaultConstruction_TraceIdIsNull()
    {
        var evt = new MessageTurnStartedEvent("turn-1", "conv-1", "TestAgent");
        evt.TraceId.Should().BeNull();
    }

    [Fact]
    public void AgentEvent_DefaultConstruction_SpanIdIsNull()
    {
        var evt = new MessageTurnStartedEvent("turn-1", "conv-1", "TestAgent");
        evt.SpanId.Should().BeNull();
    }

    [Fact]
    public void AgentEvent_DefaultConstruction_ParentSpanIdIsNull()
    {
        var evt = new MessageTurnStartedEvent("turn-1", "conv-1", "TestAgent");
        evt.ParentSpanId.Should().BeNull();
    }

    // ── With expression preserves values ──────────────────────────────────────

    [Fact]
    public void AgentEvent_WithTraceId_TraceIdPreserved()
    {
        var original = new MessageTurnStartedEvent("turn-1", "conv-1", "TestAgent");
        var stamped = original with { TraceId = "deadbeefdeadbeefdeadbeefdeadbeef" };

        stamped.TraceId.Should().Be("deadbeefdeadbeefdeadbeefdeadbeef");
    }

    [Fact]
    public void AgentEvent_WithSpanId_SpanIdPreserved()
    {
        var original = new AgentTurnStartedEvent(1);
        var stamped = original with { SpanId = "abcd1234abcd1234" };

        stamped.SpanId.Should().Be("abcd1234abcd1234");
    }

    [Fact]
    public void AgentEvent_WithParentSpanId_ParentSpanIdPreserved()
    {
        var original = new ToolCallStartEvent("call-1", "MyTool", "msg-1", null);
        var stamped = original with { ParentSpanId = "aabbccdd11223344" };

        stamped.ParentSpanId.Should().Be("aabbccdd11223344");
    }

    [Fact]
    public void AgentEvent_WithAllSpanFields_AllPreserved()
    {
        var original = new AgentTurnStartedEvent(2);
        var stamped = original with
        {
            TraceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1",
            SpanId = "bbbbbbbbbbbbbbbb",
            ParentSpanId = "cccccccccccccccc"
        };

        stamped.TraceId.Should().Be("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1");
        stamped.SpanId.Should().Be("bbbbbbbbbbbbbbbb");
        stamped.ParentSpanId.Should().Be("cccccccccccccccc");
    }

    // ── with expression is non-mutating ──────────────────────────────────────

    [Fact]
    public void AgentEvent_WithTraceId_OriginalUnchanged()
    {
        var original = new MessageTurnStartedEvent("turn-1", "conv-1", "TestAgent");
        _ = original with { TraceId = "ffffffffffffffffffffffffffffffff" };

        original.TraceId.Should().BeNull();
    }

    // ── Works on arbitrary subclasses ─────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SampleEvents))]
    public void AgentEventSubclass_CanCarryTraceId(AgentEvent evt)
    {
        var stamped = evt with { TraceId = "1234567890abcdef1234567890abcdef" };
        stamped.TraceId.Should().Be("1234567890abcdef1234567890abcdef");
    }

    [Theory]
    [MemberData(nameof(SampleEvents))]
    public void AgentEventSubclass_DefaultTraceIdIsNull(AgentEvent evt)
    {
        evt.TraceId.Should().BeNull();
    }

    public static TheoryData<AgentEvent> SampleEvents() => new()
    {
        new MessageTurnStartedEvent("t", "c", "A"),
        new MessageTurnFinishedEvent("t", "c", "A", TimeSpan.FromMilliseconds(100)),
        new AgentTurnStartedEvent(1),
        new AgentTurnFinishedEvent(1),
        new ToolCallStartEvent("call-1", "SomeTool", "msg-1", null),
        new ToolCallEndEvent("call-1"),
        new ToolCallResultEvent("call-1", "result", null),
        new TextDeltaEvent("hello", "msg-1"),
        new AgentDecisionEvent("A", "Continue", 1, 0, 0),
    };
}
