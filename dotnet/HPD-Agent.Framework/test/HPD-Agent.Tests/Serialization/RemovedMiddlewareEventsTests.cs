using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Serialization;

/// <summary>
/// Area 8 — Regression: removed middleware events.
/// MiddlewareProgressEvent, MiddlewarePipelineStartEvent, MiddlewarePipelineEndEvent
/// were deleted. These tests confirm they are gone from the public API, the
/// serializer event-type constants, and that their removal doesn't break existing
/// serialization of remaining events.
/// </summary>
public class RemovedMiddlewareEventsTests
{
    // ── 8.1  MiddlewareProgressEvent type does not exist ─────────────────────

    [Fact]
    public void MiddlewareProgressEvent_TypeDoesNotExist()
    {
        var assembly = typeof(AgentEvent).Assembly;
        var type = assembly.GetType("HPD.Agent.MiddlewareProgressEvent");
        type.Should().BeNull("MiddlewareProgressEvent was removed and must not exist");
    }

    // ── 8.2  MiddlewarePipelineStartEvent type does not exist ─────────────────

    [Fact]
    public void MiddlewarePipelineStartEvent_TypeDoesNotExist()
    {
        var assembly = typeof(AgentEvent).Assembly;
        var type = assembly.GetType("HPD.Agent.MiddlewarePipelineStartEvent");
        type.Should().BeNull("MiddlewarePipelineStartEvent was removed and must not exist");
    }

    // ── 8.3  MiddlewarePipelineEndEvent type does not exist ───────────────────

    [Fact]
    public void MiddlewarePipelineEndEvent_TypeDoesNotExist()
    {
        var assembly = typeof(AgentEvent).Assembly;
        var type = assembly.GetType("HPD.Agent.MiddlewarePipelineEndEvent");
        type.Should().BeNull("MiddlewarePipelineEndEvent was removed and must not exist");
    }

    // ── 8.4  EventTypes constants for removed events are gone ─────────────────

    [Fact]
    public void EventTypes_DoesNotContain_MiddlewareProgress()
    {
        var type = typeof(EventTypes);
        // Flatten all nested types to search constants
        var allFields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Concat(type.GetNestedTypes().SelectMany(n =>
                n.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)))
            .Where(f => f.IsLiteral)
            .Select(f => f.GetRawConstantValue()?.ToString() ?? "")
            .ToList();

        allFields.Should().NotContain("MIDDLEWARE_PROGRESS",
            "the MIDDLEWARE_PROGRESS constant was removed along with the event type");
        allFields.Should().NotContain("MIDDLEWARE_PIPELINE_START",
            "the MIDDLEWARE_PIPELINE_START constant was removed");
        allFields.Should().NotContain("MIDDLEWARE_PIPELINE_END",
            "the MIDDLEWARE_PIPELINE_END constant was removed");
    }

    // ── 8.5  Serializer still handles remaining events without regression ──────

    [Fact]
    public void Serializer_StillHandles_ToolCallStartEvent()
    {
        var evt = new ToolCallStartEvent("call-1", "MyTool", "msg-1");
        var json = AgentEventSerializer.ToJson(evt);

        json.Should().Contain("TOOL_CALL_START");
        json.Should().Contain("MyTool");
    }

    [Fact]
    public void Serializer_StillHandles_MessageTurnFinishedEvent()
    {
        var evt = new MessageTurnFinishedEvent("t-1", "c-1", "Agent", TimeSpan.FromSeconds(1));
        var json = AgentEventSerializer.ToJson(evt);

        json.Should().Contain("MESSAGE_TURN_FINISHED");
    }
}
