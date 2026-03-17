using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Serialization;
using Xunit;

namespace HPD.Agent.Tests.Serialization;

/// <summary>
/// Unit tests for AgentEventSerializer.
/// Verifies the standard event serialization format.
/// </summary>
public class AgentEventSerializerTests
{
    #region Basic Serialization Tests

    [Fact]
    public void ToJson_TextDeltaEvent_SerializesCorrectly()
    {
        // Arrange
        var evt = new TextDeltaEvent("hello", "msg-123");

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert
        Assert.Contains("\"version\":\"1.0\"", json);
        Assert.Contains("\"type\":\"TEXT_DELTA\"", json);
        Assert.Contains("\"text\":\"hello\"", json);
        Assert.Contains("\"messageId\":\"msg-123\"", json);
    }

    [Fact]
    public void ToJson_VersionField_IsPresent()
    {
        // Arrange
        var evt = new TextDeltaEvent("hello", "msg-123");

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert
        Assert.Contains("\"version\":\"1.0\"", json);
    }

    [Fact]
    public void ToJson_TypeField_UsesScreamingSnakeCase()
    {
        // Arrange
        var events = new AgentEvent[]
        {
            new TextDeltaEvent("text", "msg-1"),
            new ToolCallStartEvent("call-1", "TestTool", "msg-1"),
            new PermissionRequestEvent("perm-1", "Source", "TestFunc", null, "call-1", null),
            new MessageTurnStartedEvent("turn-1", "conv-1", "Agent"),
            new AgentTurnStartedEvent(1),
        };

        var expectedTypes = new[]
        {
            "TEXT_DELTA",
            "TOOL_CALL_START",
            "PERMISSION_REQUEST",
            "MESSAGE_TURN_STARTED",
            "AGENT_TURN_STARTED",
        };

        // Act & Assert
        for (int i = 0; i < events.Length; i++)
        {
            var json = AgentEventSerializer.ToJson(events[i]);
            Assert.Contains($"\"type\":\"{expectedTypes[i]}\"", json);
        }
    }

    #endregion

    #region Property Naming Tests

    [Fact]
    public void ToJson_UsesCamelCase()
    {
        // Arrange
        var evt = new PermissionRequestEvent(
            PermissionId: "perm-123",
            SourceName: "PermissionMiddleware",
            FunctionName: "WriteFile",
            Description: "Test",
            CallId: "call-456",
            Arguments: null);

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert - should use camelCase
        Assert.Contains("permissionId", json);
        Assert.Contains("functionName", json);
        Assert.Contains("sourceName", json);
        Assert.Contains("callId", json);

        // Should NOT use snake_case
        Assert.DoesNotContain("permission_id", json);
        Assert.DoesNotContain("function_name", json);
        Assert.DoesNotContain("source_name", json);
        Assert.DoesNotContain("call_id", json);
    }

    #endregion

    #region Null Handling Tests

    [Fact]
    public void ToJson_NullProperties_AreOmitted()
    {
        // Arrange
        var evt = new PermissionRequestEvent(
            PermissionId: "perm-123",
            SourceName: "PermissionMiddleware",
            FunctionName: "WriteFile",
            Description: null, // Should be omitted
            CallId: "call-456",
            Arguments: null); // Should be omitted

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert
        Assert.DoesNotContain("\"description\"", json);
        Assert.DoesNotContain("\"arguments\"", json);
    }

    [Fact]
    public void ToJson_EmptyString_IsIncluded()
    {
        // Arrange
        var evt = new PermissionRequestEvent(
            PermissionId: "perm-123",
            SourceName: "PermissionMiddleware",
            FunctionName: "WriteFile",
            Description: "", // Empty string should be included
            CallId: "call-456",
            Arguments: null);

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert - empty string should be present
        Assert.Contains("\"description\":\"\"", json);
    }

    #endregion

    #region ExecutionContext Tests

    [Fact]
    public void ToJson_ExecutionContext_SerializesCorrectly()
    {
        // Arrange
        var context = new AgentExecutionContext
        {
            AgentName = "SubAgent A",
            AgentId = "parent-abc-subagent-def",
            Depth = 2,
            AgentChain = new[] { "Root", "Parent", "SubAgent A" }
        };
        var evt = new TextDeltaEvent("hello", "msg-123")
        {
            ExecutionContext = context
        };

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert
        Assert.Contains("\"executionContext\"", json);
        Assert.Contains("\"agentName\":\"SubAgent A\"", json);
        Assert.Contains("\"depth\":2", json);
    }

    [Fact]
    public void ToJson_NullExecutionContext_IsOmitted()
    {
        // Arrange
        var evt = new TextDeltaEvent("hello", "msg-123");

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert
        Assert.DoesNotContain("executionContext", json);
    }

    #endregion

    #region All Event Types Tests

    [Fact]
    public void ToJson_MessageTurnEvents_SerializeCorrectly()
    {
        // MessageTurnStartedEvent
        var startEvt = new MessageTurnStartedEvent("turn-1", "conv-1", "Agent");
        var startJson = AgentEventSerializer.ToJson(startEvt);
        Assert.Contains("\"type\":\"MESSAGE_TURN_STARTED\"", startJson);
        Assert.Contains("\"messageTurnId\":\"turn-1\"", startJson);

        // MessageTurnFinishedEvent
        var finishEvt = new MessageTurnFinishedEvent("turn-1", "conv-1", "Agent", TimeSpan.FromSeconds(5));
        var finishJson = AgentEventSerializer.ToJson(finishEvt);
        Assert.Contains("\"type\":\"MESSAGE_TURN_FINISHED\"", finishJson);

        // MessageTurnErrorEvent
        var errorEvt = new MessageTurnErrorEvent("Test error");
        var errorJson = AgentEventSerializer.ToJson(errorEvt);
        Assert.Contains("\"type\":\"MESSAGE_TURN_ERROR\"", errorJson);
        Assert.Contains("\"message\":\"Test error\"", errorJson);
    }

    [Fact]
    public void ToJson_AgentTurnEvents_SerializeCorrectly()
    {
        // AgentTurnStartedEvent
        var startEvt = new AgentTurnStartedEvent(1);
        var startJson = AgentEventSerializer.ToJson(startEvt);
        Assert.Contains("\"type\":\"AGENT_TURN_STARTED\"", startJson);
        Assert.Contains("\"iteration\":1", startJson);

        // AgentTurnFinishedEvent
        var finishEvt = new AgentTurnFinishedEvent(1);
        var finishJson = AgentEventSerializer.ToJson(finishEvt);
        Assert.Contains("\"type\":\"AGENT_TURN_FINISHED\"", finishJson);
    }

    [Fact]
    public void ToJson_ToolEvents_SerializeCorrectly()
    {
        // ToolCallStartEvent — minimal (no optional fields)
        var startEvt = new ToolCallStartEvent("call-1", "Calculator", "msg-1");
        var startJson = AgentEventSerializer.ToJson(startEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_START\"", startJson);
        Assert.Contains("\"callId\":\"call-1\"", startJson);
        Assert.Contains("\"name\":\"Calculator\"", startJson);
        // Null optional fields omitted
        Assert.DoesNotContain("\"toolkitName\"", startJson);
        Assert.DoesNotContain("\"callType\"", startJson);

        // ToolCallStartEvent — with toolkit and callType
        var startEvtFull = new ToolCallStartEvent("call-2", "Add", "msg-1", "MathToolkit", ToolCallType.Function);
        var startJsonFull = AgentEventSerializer.ToJson(startEvtFull);
        Assert.Contains("\"toolkitName\":\"MathToolkit\"", startJsonFull);
        Assert.Contains("\"callType\":\"Function\"", startJsonFull);

        // ToolCallStartEvent — SubAgent type
        var subAgentEvt = new ToolCallStartEvent("call-3", "ResearchAgent", "msg-1", "AgentToolkit", ToolCallType.SubAgent);
        var subAgentJson = AgentEventSerializer.ToJson(subAgentEvt);
        Assert.Contains("\"callType\":\"SubAgent\"", subAgentJson);

        // ToolCallArgsEvent
        var argsEvt = new ToolCallArgsEvent("call-1", "{\"x\":1,\"y\":2}");
        var argsJson = AgentEventSerializer.ToJson(argsEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_ARGS\"", argsJson);

        // ToolCallEndEvent
        var endEvt = new ToolCallEndEvent("call-1");
        var endJson = AgentEventSerializer.ToJson(endEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_END\"", endJson);

        // ToolCallResultEvent — minimal
        var resultEvt = new ToolCallResultEvent("call-1", "3");
        var resultJson = AgentEventSerializer.ToJson(resultEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_RESULT\"", resultJson);
        Assert.Contains("\"result\":\"3\"", resultJson);
        Assert.DoesNotContain("\"toolkitName\"", resultJson);
        Assert.DoesNotContain("\"callType\"", resultJson);

        // ToolCallResultEvent — with toolkit and callType
        var resultEvtFull = new ToolCallResultEvent("call-2", "42", "MathToolkit", ToolCallType.Function);
        var resultJsonFull = AgentEventSerializer.ToJson(resultEvtFull);
        Assert.Contains("\"toolkitName\":\"MathToolkit\"", resultJsonFull);
        Assert.Contains("\"callType\":\"Function\"", resultJsonFull);
    }

    [Fact]
    public void ToolCallStartEvent_RoundTrip_PreservesAllFields()
    {
        var evt = new ToolCallStartEvent("call-rt", "Add", "msg-rt", "MathToolkit", ToolCallType.Function);
        var json = AgentEventSerializer.ToJson(evt);
        var result = Assert.IsType<ToolCallStartEvent>(AgentEventSerializer.FromJson(json));

        Assert.Equal("call-rt", result.CallId);
        Assert.Equal("Add", result.Name);
        Assert.Equal("msg-rt", result.MessageId);
        Assert.Equal("MathToolkit", result.ToolkitName);
        Assert.Equal(ToolCallType.Function, result.CallType);
    }

    [Fact]
    public void ToolCallResultEvent_RoundTrip_PreservesAllFields()
    {
        var evt = new ToolCallResultEvent("call-rt", "42", "MathToolkit", ToolCallType.SubAgent);
        var json = AgentEventSerializer.ToJson(evt);
        var result = Assert.IsType<ToolCallResultEvent>(AgentEventSerializer.FromJson(json));

        Assert.Equal("call-rt", result.CallId);
        Assert.Equal("42", result.Result);
        Assert.Equal("MathToolkit", result.ToolkitName);
        Assert.Equal(ToolCallType.SubAgent, result.CallType);
    }

    [Theory]
    [InlineData(ToolCallType.Function)]
    [InlineData(ToolCallType.Skill)]
    [InlineData(ToolCallType.SubAgent)]
    [InlineData(ToolCallType.MultiAgent)]
    [InlineData(ToolCallType.MCPServer)]
    [InlineData(ToolCallType.OpenApi)]
    public void ToolCallStartEvent_RoundTrip_AllCallTypes(ToolCallType callType)
    {
        var evt = new ToolCallStartEvent("call-1", "Tool", "msg-1", null, callType);
        var json = AgentEventSerializer.ToJson(evt);
        var result = Assert.IsType<ToolCallStartEvent>(AgentEventSerializer.FromJson(json));
        Assert.Equal(callType, result.CallType);
    }

    [Fact]
    public void ToJson_PermissionEvents_SerializeCorrectly()
    {
        // PermissionRequestEvent
        var reqEvt = new PermissionRequestEvent("perm-1", "Source", "WriteFile", "Write to disk", "call-1", null);
        var reqJson = AgentEventSerializer.ToJson(reqEvt);
        Assert.Contains("\"type\":\"PERMISSION_REQUEST\"", reqJson);
        Assert.Contains("\"permissionId\":\"perm-1\"", reqJson);

        // PermissionApprovedEvent
        var approvedEvt = new PermissionApprovedEvent("perm-1", "Source");
        var approvedJson = AgentEventSerializer.ToJson(approvedEvt);
        Assert.Contains("\"type\":\"PERMISSION_APPROVED\"", approvedJson);

        // PermissionDeniedEvent
        var deniedEvt = new PermissionDeniedEvent("perm-1", "Source", "call-1", "User rejected");
        var deniedJson = AgentEventSerializer.ToJson(deniedEvt);
        Assert.Contains("\"type\":\"PERMISSION_DENIED\"", deniedJson);
        Assert.Contains("\"reason\":\"User rejected\"", deniedJson);
    }

    [Fact]
    public void ToJson_MiddlewareEvents_SerializeCorrectly()
    {
        // MiddlewareErrorEvent
        var errorEvt = new MiddlewareErrorEvent("TestMiddleware", "Something went wrong");
        var errorJson = AgentEventSerializer.ToJson(errorEvt);
        Assert.Contains("\"type\":\"MIDDLEWARE_ERROR\"", errorJson);
        Assert.Contains("\"errorMessage\":\"Something went wrong\"", errorJson);
    }

    [Fact]
    public void ToJson_ReasoningDeltaEvent_SerializesCorrectly()
    {
        // Reasoning delta event
        var evt = new ReasoningDeltaEvent("Let me think about this...", "msg-1");
        var json = AgentEventSerializer.ToJson(evt);
        Assert.Contains("\"type\":\"REASONING_DELTA\"", json);
        Assert.Contains("\"text\":\"Let me think about this...\"", json);
    }

    [Fact]
    public void ToJson_ReasoningMessageStartEvent_SerializesCorrectly()
    {
        // Reasoning message start event
        var evt = new ReasoningMessageStartEvent("msg-1", "assistant");
        var json = AgentEventSerializer.ToJson(evt);
        Assert.Contains("\"type\":\"REASONING_MESSAGE_START\"", json);
        Assert.Contains("\"role\":\"assistant\"", json);
    }

    [Fact]
    public void ToJson_ReasoningMessageEndEvent_SerializesCorrectly()
    {
        // Reasoning message end event
        var evt = new ReasoningMessageEndEvent("msg-1");
        var json = AgentEventSerializer.ToJson(evt);
        Assert.Contains("\"type\":\"REASONING_MESSAGE_END\"", json);
        Assert.Contains("\"messageId\":\"msg-1\"", json);
    }

    #endregion

    #region GetEventTypeName Tests

    [Fact]
    public void GetEventTypeName_ReturnsCorrectDiscriminator()
    {
        // Known event types
        Assert.Equal("TEXT_DELTA", AgentEventSerializer.GetEventTypeName(typeof(TextDeltaEvent)));
        Assert.Equal("TOOL_CALL_START", AgentEventSerializer.GetEventTypeName(typeof(ToolCallStartEvent)));
        Assert.Equal("PERMISSION_REQUEST", AgentEventSerializer.GetEventTypeName(typeof(PermissionRequestEvent)));
        Assert.Equal("MESSAGE_TURN_STARTED", AgentEventSerializer.GetEventTypeName(typeof(MessageTurnStartedEvent)));
    }

    [Fact]
    public void GetEventTypeName_Instance_ReturnsCorrectDiscriminator()
    {
        // Test with instance
        var evt = new TextDeltaEvent("hello", "msg-123");
        Assert.Equal("TEXT_DELTA", AgentEventSerializer.GetEventTypeName(evt));
    }

    #endregion

    #region JSON Validity Tests

    [Fact]
    public void ToJson_ProducesValidJson()
    {
        // Arrange
        var events = new AgentEvent[]
        {
            new TextDeltaEvent("hello", "msg-1"),
            new ToolCallStartEvent("call-1", "TestTool", "msg-1"),
            new PermissionRequestEvent("perm-1", "Source", "TestFunc", "desc", "call-1", new Dictionary<string, object?> { ["arg1"] = "value1" }),
            new MessageTurnStartedEvent("turn-1", "conv-1", "Agent"),
            new AgentTurnStartedEvent(1),
        };

        // Act & Assert
        foreach (var evt in events)
        {
            var json = AgentEventSerializer.ToJson(evt);

            // Should be valid JSON
            var exception = Record.Exception(() => JsonDocument.Parse(json));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void ToJson_SpecialCharacters_AreEscaped()
    {
        // Arrange
        var evt = new TextDeltaEvent("Hello \"World\"\nNew line\ttab", "msg-123");

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert - should be valid JSON
        var exception = Record.Exception(() => JsonDocument.Parse(json));
        Assert.Null(exception);
    }

    #endregion

    #region Version Parameter Tests

    [Fact]
    public void ToJson_WithCustomVersion_UsesSpecifiedVersion()
    {
        // Arrange
        var evt = new TextDeltaEvent("hello", "msg-123");

        // Act
        var json = AgentEventSerializer.ToJson(evt, "2.0");

        // Assert
        Assert.Contains("\"version\":\"2.0\"", json);
    }

    #endregion

    #region Observability Event Tests

    [Fact]
    public void ToJson_ObservabilityEvents_SerializeCorrectly()
    {
        // CircuitBreakerTriggeredEvent
        var cbEvt = new CircuitBreakerTriggeredEvent("TestAgent", "TestFunc", 3, 5, DateTimeOffset.Now);
        var cbJson = AgentEventSerializer.ToJson(cbEvt);
        Assert.Contains("\"type\":\"CIRCUIT_BREAKER_TRIGGERED\"", cbJson);
        Assert.Contains("\"consecutiveCount\":3", cbJson);

        // IterationStartEvent
        var iterEvt = new IterationStartEvent("TestAgent", 1, 10, 5, 2, 3, 1);
        var iterJson = AgentEventSerializer.ToJson(iterEvt);
        Assert.Contains("\"type\":\"ITERATION_START\"", iterJson);

        // CheckpointEvent
        var checkEvt = new CheckpointEvent(CheckpointOperation.Saved, "thread-1", DateTimeOffset.Now);
        var checkJson = AgentEventSerializer.ToJson(checkEvt);
        Assert.Contains("\"type\":\"CHECKPOINT\"", checkJson);
    }

    #endregion
}
