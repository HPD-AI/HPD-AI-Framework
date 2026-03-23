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
        // ToolCallStartEvent
        var startEvt = new ToolCallStartEvent("call-1", "Calculator", "msg-1");
        var startJson = AgentEventSerializer.ToJson(startEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_START\"", startJson);
        Assert.Contains("\"callId\":\"call-1\"", startJson);
        Assert.Contains("\"name\":\"Calculator\"", startJson);

        // ToolCallArgsEvent
        var argsEvt = new ToolCallArgsEvent("call-1", "{\"x\":1,\"y\":2}");
        var argsJson = AgentEventSerializer.ToJson(argsEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_ARGS\"", argsJson);

        // ToolCallEndEvent
        var endEvt = new ToolCallEndEvent("call-1");
        var endJson = AgentEventSerializer.ToJson(endEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_END\"", endJson);

        // ToolCallResultEvent
        var resultEvt = new ToolCallResultEvent("call-1", "3");
        var resultJson = AgentEventSerializer.ToJson(resultEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_RESULT\"", resultJson);
        Assert.Contains("\"result\":\"3\"", resultJson);
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

/// <summary>
/// Tests for AgentEventSerializer.FromJson — the deserialization path.
/// Covers round-trips, discriminator lookup, null/optional fields, malformed
/// input, SSE wire format, bidirectional events, and hierarchical execution context.
/// </summary>
public class AgentEventSerializerFromJsonTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static T RoundTrip<T>(AgentEvent evt) where T : AgentEvent
    {
        var json = AgentEventSerializer.ToJson(evt);
        var result = AgentEventSerializer.FromJson(json);
        Assert.NotNull(result);
        return Assert.IsType<T>(result);
    }

    // -------------------------------------------------------------------------
    // Basic round-trip
    // -------------------------------------------------------------------------

    #region Basic Round-Trip Tests

    [Fact]
    public void FromJson_TextDeltaEvent_ReturnsCorrectConcreteType()
    {
        // Arrange
        var evt = new TextDeltaEvent("hello", "msg-123");

        // Act
        var result = RoundTrip<TextDeltaEvent>(evt);

        // Assert
        Assert.IsType<TextDeltaEvent>(result);
    }

    [Fact]
    public void FromJson_TextDeltaEvent_PreservesAllFields()
    {
        // Arrange
        var evt = new TextDeltaEvent("hello", "msg-123");

        // Act
        var result = RoundTrip<TextDeltaEvent>(evt);

        // Assert
        Assert.Equal("hello", result.Text);
        Assert.Equal("msg-123", result.MessageId);
    }

    [Fact]
    public void FromJson_TextMessageStartEvent_RoundTrips()
    {
        // Arrange
        var evt = new TextMessageStartEvent("msg-1", "assistant");

        // Act
        var result = RoundTrip<TextMessageStartEvent>(evt);

        // Assert
        Assert.Equal("msg-1", result.MessageId);
        Assert.Equal("assistant", result.Role);
    }

    [Fact]
    public void FromJson_TextMessageEndEvent_RoundTrips()
    {
        // Arrange
        var evt = new TextMessageEndEvent("msg-1");

        // Act
        var result = RoundTrip<TextMessageEndEvent>(evt);

        // Assert
        Assert.Equal("msg-1", result.MessageId);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Every event family
    // -------------------------------------------------------------------------

    #region Event Family Round-Trip Tests

    [Fact]
    public void FromJson_MessageTurnStartedEvent_RoundTrips()
    {
        // Arrange
        var evt = new MessageTurnStartedEvent("turn-1", "conv-1", "MyAgent");

        // Act
        var result = RoundTrip<MessageTurnStartedEvent>(evt);

        // Assert
        Assert.Equal("turn-1", result.MessageTurnId);
        Assert.Equal("conv-1", result.ConversationId);
        Assert.Equal("MyAgent", result.AgentName);
    }

    [Fact]
    public void FromJson_MessageTurnFinishedEvent_RoundTrips()
    {
        // Arrange
        var duration = TimeSpan.FromSeconds(3.5);
        var evt = new MessageTurnFinishedEvent("turn-1", "conv-1", "MyAgent", duration);

        // Act
        var result = RoundTrip<MessageTurnFinishedEvent>(evt);

        // Assert
        Assert.Equal("turn-1", result.MessageTurnId);
        Assert.Equal(duration, result.Duration);
    }

    [Fact]
    public void FromJson_MessageTurnErrorEvent_RoundTrips()
    {
        // Arrange
        var evt = new MessageTurnErrorEvent("Something went wrong");

        // Act
        var result = RoundTrip<MessageTurnErrorEvent>(evt);

        // Assert
        Assert.Equal("Something went wrong", result.Message);
    }

    [Fact]
    public void FromJson_AgentTurnStartedEvent_RoundTrips()
    {
        // Arrange
        var evt = new AgentTurnStartedEvent(4);

        // Act
        var result = RoundTrip<AgentTurnStartedEvent>(evt);

        // Assert
        Assert.Equal(4, result.Iteration);
    }

    [Fact]
    public void FromJson_AgentTurnFinishedEvent_RoundTrips()
    {
        // Arrange
        var evt = new AgentTurnFinishedEvent(4);

        // Act
        var result = RoundTrip<AgentTurnFinishedEvent>(evt);

        // Assert
        Assert.Equal(4, result.Iteration);
    }

    [Fact]
    public void FromJson_ReasoningDeltaEvent_RoundTrips()
    {
        // Arrange
        var evt = new ReasoningDeltaEvent("Let me think...", "msg-2");

        // Act
        var result = RoundTrip<ReasoningDeltaEvent>(evt);

        // Assert
        Assert.Equal("Let me think...", result.Text);
        Assert.Equal("msg-2", result.MessageId);
    }

    [Fact]
    public void FromJson_ReasoningMessageStartEvent_RoundTrips()
    {
        // Arrange
        var evt = new ReasoningMessageStartEvent("msg-2", "assistant");

        // Act
        var result = RoundTrip<ReasoningMessageStartEvent>(evt);

        // Assert
        Assert.Equal("msg-2", result.MessageId);
        Assert.Equal("assistant", result.Role);
    }

    [Fact]
    public void FromJson_ReasoningMessageEndEvent_RoundTrips()
    {
        // Arrange
        var evt = new ReasoningMessageEndEvent("msg-2");

        // Act
        var result = RoundTrip<ReasoningMessageEndEvent>(evt);

        // Assert
        Assert.Equal("msg-2", result.MessageId);
    }

    [Fact]
    public void FromJson_ToolCallStartEvent_RoundTrips()
    {
        // Arrange
        var evt = new ToolCallStartEvent("call-1", "Calculator", "msg-1");

        // Act
        var result = RoundTrip<ToolCallStartEvent>(evt);

        // Assert
        Assert.Equal("call-1", result.CallId);
        Assert.Equal("Calculator", result.Name);
        Assert.Equal("msg-1", result.MessageId);
    }

    [Fact]
    public void FromJson_ToolCallArgsEvent_RoundTrips()
    {
        // Arrange
        var evt = new ToolCallArgsEvent("call-1", "{\"x\":1}");

        // Act
        var result = RoundTrip<ToolCallArgsEvent>(evt);

        // Assert
        Assert.Equal("call-1", result.CallId);
        Assert.Equal("{\"x\":1}", result.ArgsJson);
    }

    [Fact]
    public void FromJson_ToolCallEndEvent_RoundTrips()
    {
        // Arrange
        var evt = new ToolCallEndEvent("call-1");

        // Act
        var result = RoundTrip<ToolCallEndEvent>(evt);

        // Assert
        Assert.Equal("call-1", result.CallId);
    }

    [Fact]
    public void FromJson_ToolCallResultEvent_RoundTrips()
    {
        // Arrange
        var evt = new ToolCallResultEvent("call-1", "42");

        // Act
        var result = RoundTrip<ToolCallResultEvent>(evt);

        // Assert
        Assert.Equal("call-1", result.CallId);
        Assert.Equal("42", result.Result);
    }

    [Fact]
    public void FromJson_PermissionRequestEvent_RoundTrips()
    {
        // Arrange
        var evt = new PermissionRequestEvent("perm-1", "Source", "WriteFile", "Write to disk", "call-1", null);

        // Act
        var result = RoundTrip<PermissionRequestEvent>(evt);

        // Assert
        Assert.Equal("perm-1", result.PermissionId);
        Assert.Equal("Source", result.SourceName);
        Assert.Equal("WriteFile", result.FunctionName);
        Assert.Equal("Write to disk", result.Description);
        Assert.Equal("call-1", result.CallId);
    }

    [Fact]
    public void FromJson_PermissionApprovedEvent_RoundTrips()
    {
        // Arrange
        var evt = new PermissionApprovedEvent("perm-1", "Source");

        // Act
        var result = RoundTrip<PermissionApprovedEvent>(evt);

        // Assert
        Assert.Equal("perm-1", result.PermissionId);
        Assert.Equal("Source", result.SourceName);
    }

    [Fact]
    public void FromJson_PermissionDeniedEvent_RoundTrips()
    {
        // Arrange
        var evt = new PermissionDeniedEvent("perm-1", "Source", "call-1", "User rejected");

        // Act
        var result = RoundTrip<PermissionDeniedEvent>(evt);

        // Assert
        Assert.Equal("perm-1", result.PermissionId);
        Assert.Equal("User rejected", result.Reason);
    }

    [Fact]
    public void FromJson_ClarificationRequestEvent_RoundTrips()
    {
        // Arrange
        var evt = new ClarificationRequestEvent("req-1", "Source", "What do you mean?", "MyAgent", ["Option A", "Option B"]);

        // Act
        var result = RoundTrip<ClarificationRequestEvent>(evt);

        // Assert
        Assert.Equal("req-1", result.RequestId);
        Assert.Equal("What do you mean?", result.Question);
        Assert.Equal("MyAgent", result.AgentName);
        Assert.Equal(2, result.Options?.Length);
        Assert.Equal("Option A", result.Options![0]);
    }

    [Fact]
    public void FromJson_MiddlewareErrorEvent_RoundTrips()
    {
        // Arrange
        var evt = new MiddlewareErrorEvent("TestMiddleware", "Something went wrong");

        // Act
        var result = RoundTrip<MiddlewareErrorEvent>(evt);

        // Assert
        Assert.Equal("TestMiddleware", result.SourceName);
        Assert.Equal("Something went wrong", result.ErrorMessage);
    }

    [Fact]
    public void FromJson_CircuitBreakerTriggeredEvent_RoundTrips()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow;
        var evt = new CircuitBreakerTriggeredEvent("TestAgent", "TestFunc", 3, 1, ts);

        // Act
        var result = RoundTrip<CircuitBreakerTriggeredEvent>(evt);

        // Assert
        Assert.Equal(3, result.ConsecutiveCount);
    }

    [Fact]
    public void FromJson_CheckpointEvent_RoundTrips()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow;
        var evt = new CheckpointEvent(CheckpointOperation.Saved, "thread-1", ts);

        // Act
        var result = RoundTrip<CheckpointEvent>(evt);

        // Assert
        Assert.Equal(CheckpointOperation.Saved, result.Operation);
        Assert.Equal("thread-1", result.SessionId);
    }

    [Fact]
    public void FromJson_IterationStartEvent_RoundTrips()
    {
        // Arrange
        var evt = new IterationStartEvent("TestAgent", 2, 10, 5, 3, 2, 1);

        // Act
        var result = RoundTrip<IterationStartEvent>(evt);

        // Assert
        Assert.Equal(2, result.Iteration);
        Assert.Equal(10, result.MaxIterations);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Null / optional fields
    // -------------------------------------------------------------------------

    #region Null and Optional Field Tests

    [Fact]
    public void FromJson_PermissionRequest_NullDescription_IsNull()
    {
        // Arrange
        var evt = new PermissionRequestEvent("perm-1", "Source", "WriteFile", null, "call-1", null);

        // Act
        var result = RoundTrip<PermissionRequestEvent>(evt);

        // Assert
        Assert.Null(result.Description);
    }

    [Fact]
    public void FromJson_PermissionRequest_NullArguments_IsNull()
    {
        // Arrange
        var evt = new PermissionRequestEvent("perm-1", "Source", "WriteFile", null, "call-1", null);

        // Act
        var result = RoundTrip<PermissionRequestEvent>(evt);

        // Assert
        Assert.Null(result.Arguments);
    }

    [Fact]
    public void FromJson_PermissionRequest_WithArguments_PreservesDict()
    {
        // Arrange
        var args = new Dictionary<string, object?> { ["path"] = "/tmp/file.txt", ["mode"] = "write" };
        var evt = new PermissionRequestEvent("perm-1", "Source", "WriteFile", null, "call-1", args);

        // Act
        var result = RoundTrip<PermissionRequestEvent>(evt);

        // Assert
        Assert.NotNull(result.Arguments);
        Assert.True(result.Arguments.ContainsKey("path"));
        Assert.True(result.Arguments.ContainsKey("mode"));
    }

    [Fact]
    public void FromJson_ExecutionContext_WhenPresent_IsDeserialized()
    {
        // Arrange
        var evt = new TextDeltaEvent("hi", "msg-1")
        {
            ExecutionContext = new AgentExecutionContext
            {
                AgentName = "Sub",
                AgentId = "sub-id",
                Depth = 1
            }
        };

        // Act
        var result = RoundTrip<TextDeltaEvent>(evt);

        // Assert
        Assert.NotNull(result.ExecutionContext);
        Assert.Equal("Sub", result.ExecutionContext.AgentName);
        Assert.Equal(1, result.ExecutionContext.Depth);
    }

    [Fact]
    public void FromJson_ExecutionContext_WhenAbsent_IsNull()
    {
        // Arrange
        var evt = new TextDeltaEvent("hi", "msg-1"); // no ExecutionContext set

        // Act
        var result = RoundTrip<TextDeltaEvent>(evt);

        // Assert
        Assert.Null(result.ExecutionContext);
    }

    [Fact]
    public void FromJson_ExecutionContext_AgentChain_PreservesOrder()
    {
        // Arrange
        var evt = new TextDeltaEvent("hi", "msg-1")
        {
            ExecutionContext = new AgentExecutionContext
            {
                AgentName = "Leaf",
                AgentId = "leaf-id",
                AgentChain = ["Root", "Mid", "Leaf"],
                Depth = 2
            }
        };

        // Act
        var result = RoundTrip<TextDeltaEvent>(evt);

        // Assert
        Assert.NotNull(result.ExecutionContext);
        Assert.Equal(3, result.ExecutionContext.AgentChain.Count);
        Assert.Equal("Root", result.ExecutionContext.AgentChain[0]);
        Assert.Equal("Mid", result.ExecutionContext.AgentChain[1]);
        Assert.Equal("Leaf", result.ExecutionContext.AgentChain[2]);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Discriminator lookup
    // -------------------------------------------------------------------------

    #region Discriminator Lookup Tests

    [Fact]
    public void FromJson_DiscriminatorCaseInsensitive_LowercaseWorks()
    {
        // Arrange — build wire JSON manually with lowercase discriminator
        var json = "{\"version\":\"1.0\",\"type\":\"text_delta\",\"text\":\"hi\",\"messageId\":\"m1\"}";

        // Act
        var result = AgentEventSerializer.FromJson(json);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TextDeltaEvent>(result);
    }

    [Fact]
    public void FromJson_UnknownDiscriminator_ReturnsNull()
    {
        // Arrange
        var json = "{\"version\":\"1.0\",\"type\":\"MADE_UP_EVENT\",\"foo\":\"bar\"}";

        // Act
        var result = AgentEventSerializer.FromJson(json);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FromJson_MissingTypeField_ReturnsNull()
    {
        // Arrange
        var json = "{\"version\":\"1.0\",\"text\":\"hi\",\"messageId\":\"m1\"}";

        // Act
        var result = AgentEventSerializer.FromJson(json);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FromJson_NullTypeValue_ReturnsNull()
    {
        // Arrange
        var json = "{\"version\":\"1.0\",\"type\":null,\"text\":\"hi\"}";

        // Act
        var result = AgentEventSerializer.FromJson(json);

        // Assert
        Assert.Null(result);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Malformed input
    // -------------------------------------------------------------------------

    #region Malformed Input Tests

    [Fact]
    public void FromJson_EmptyString_ReturnsNull()
    {
        // Act
        var result = AgentEventSerializer.FromJson("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FromJson_InvalidJson_ReturnsNull()
    {
        // Act
        var result = AgentEventSerializer.FromJson("not json at all");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FromJson_EmptyObject_ReturnsNull()
    {
        // Act
        var result = AgentEventSerializer.FromJson("{}");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FromJson_PartialJson_ReturnsNull()
    {
        // Arrange — truncated mid-string
        var json = "{\"version\":\"1.0\",\"type\":\"TEXT_DELTA\",\"text\":\"hel";

        // Act
        var result = AgentEventSerializer.FromJson(json);

        // Assert
        Assert.Null(result);
    }

    #endregion

    // -------------------------------------------------------------------------
    // SSE wire format (exact bytes the server emits)
    // -------------------------------------------------------------------------

    #region SSE Wire Format Tests

    [Fact]
    public void FromJson_WireFormat_TextDelta_Deserializes()
    {
        // Arrange — exact string SseEventHandler writes
        var json = "{\"version\":\"1.0\",\"type\":\"TEXT_DELTA\",\"text\":\"hi\",\"messageId\":\"m1\"}";

        // Act
        var result = AgentEventSerializer.FromJson(json);

        // Assert
        var typed = Assert.IsType<TextDeltaEvent>(result);
        Assert.Equal("hi", typed.Text);
        Assert.Equal("m1", typed.MessageId);
    }

    [Fact]
    public void FromJson_WireFormat_ToolCallStart_Deserializes()
    {
        // Arrange
        var json = "{\"version\":\"1.0\",\"type\":\"TOOL_CALL_START\",\"callId\":\"c1\",\"name\":\"Calc\",\"messageId\":\"m1\"}";

        // Act
        var result = AgentEventSerializer.FromJson(json);

        // Assert
        var typed = Assert.IsType<ToolCallStartEvent>(result);
        Assert.Equal("c1", typed.CallId);
        Assert.Equal("Calc", typed.Name);
    }

    [Fact]
    public void FromJson_WireFormat_PermissionRequest_Deserializes()
    {
        // Arrange — includes nested arguments dict
        var json = "{\"version\":\"1.0\",\"type\":\"PERMISSION_REQUEST\",\"permissionId\":\"p1\",\"sourceName\":\"S\",\"functionName\":\"F\",\"callId\":\"c1\",\"arguments\":{\"path\":\"/tmp\"}}";

        // Act
        var result = AgentEventSerializer.FromJson(json);

        // Assert
        var typed = Assert.IsType<PermissionRequestEvent>(result);
        Assert.Equal("p1", typed.PermissionId);
        Assert.NotNull(typed.Arguments);
        Assert.True(typed.Arguments.ContainsKey("path"));
    }

    [Fact]
    public void FromJson_WireFormat_WithExtraFields_Ignored()
    {
        // Arrange — server may add forward-compat fields
        var json = "{\"version\":\"1.0\",\"type\":\"TEXT_DELTA\",\"text\":\"hi\",\"messageId\":\"m1\",\"unknownFutureField\":\"value\"}";

        // Act
        var result = AgentEventSerializer.FromJson(json);

        // Assert — extra field silently ignored, event still parsed
        Assert.NotNull(result);
        Assert.IsType<TextDeltaEvent>(result);
    }

    #endregion

    // -------------------------------------------------------------------------
    // ToJson ↔ FromJson symmetry across all registered types
    // -------------------------------------------------------------------------

    #region Full Round-Trip Theory

    public static TheoryData<AgentEvent> AllRegisteredEvents() => new()
    {
        new TextDeltaEvent("text", "msg-1"),
        new TextMessageStartEvent("msg-1", "assistant"),
        new TextMessageEndEvent("msg-1"),
        new ReasoningDeltaEvent("think", "msg-1"),
        new ReasoningMessageStartEvent("msg-1", "assistant"),
        new ReasoningMessageEndEvent("msg-1"),
        new MessageTurnStartedEvent("turn-1", "conv-1", "Agent"),
        new MessageTurnFinishedEvent("turn-1", "conv-1", "Agent", TimeSpan.FromSeconds(1)),
        new MessageTurnErrorEvent("err"),
        new AgentTurnStartedEvent(1),
        new AgentTurnFinishedEvent(1),
        new ToolCallStartEvent("c1", "Tool", "msg-1"),
        new ToolCallArgsEvent("c1", "{}"),
        new ToolCallEndEvent("c1"),
        new ToolCallResultEvent("c1", "ok"),
        new PermissionRequestEvent("p1", "S", "F", null, "c1", null),
        new PermissionApprovedEvent("p1", "S"),
        new PermissionDeniedEvent("p1", "S", "c1", "no"),
        new PermissionResponseEvent("p1", "S", true),
        new ClarificationRequestEvent("r1", "S", "Q?"),
        new ClarificationResponseEvent("r1", "S", "Q?", "A"),
        new ContinuationRequestEvent("cont-1", "S", 5, 10),
        new ContinuationResponseEvent("cont-1", "S", true),
        new MiddlewareErrorEvent("MW", "oops"),
        new CircuitBreakerTriggeredEvent("A", "F", 3, 1, DateTimeOffset.UtcNow),
        new CheckpointEvent(CheckpointOperation.Saved, "t1", DateTimeOffset.UtcNow),
        new IterationStartEvent("A", 1, 10, 5, 3, 2, 1),
        new NestedAgentInvokedEvent("Orchestrator", "Child", 1, DateTimeOffset.UtcNow),
    };

    [Theory]
    [MemberData(nameof(AllRegisteredEvents))]
    public void FromJson_AllRegisteredEventTypes_ConcreteTypeMatchesAfterRoundTrip(AgentEvent original)
    {
        // Act
        var json = AgentEventSerializer.ToJson(original);
        var result = AgentEventSerializer.FromJson(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.GetType(), result.GetType());
    }

    #endregion

    // -------------------------------------------------------------------------
    // Bidirectional events (client → server)
    // -------------------------------------------------------------------------

    #region Bidirectional Event Tests

    [Fact]
    public void FromJson_PermissionResponseEvent_RoundTrips()
    {
        // Arrange
        var evt = new PermissionResponseEvent("perm-1", "Source", true, "User approved", PermissionChoice.AlwaysAllow);

        // Act
        var result = RoundTrip<PermissionResponseEvent>(evt);

        // Assert
        Assert.Equal("perm-1", result.PermissionId);
        Assert.True(result.Approved);
        Assert.Equal("User approved", result.Reason);
        Assert.Equal(PermissionChoice.AlwaysAllow, result.Choice);
    }

    [Fact]
    public void FromJson_ClarificationResponseEvent_RoundTrips()
    {
        // Arrange
        var evt = new ClarificationResponseEvent("req-1", "Source", "What do you mean?", "I mean this");

        // Act
        var result = RoundTrip<ClarificationResponseEvent>(evt);

        // Assert
        Assert.Equal("req-1", result.RequestId);
        Assert.Equal("I mean this", result.Answer);
    }

    [Fact]
    public void FromJson_ContinuationRequestEvent_RoundTrips()
    {
        // Arrange
        var evt = new ContinuationRequestEvent("cont-1", "CircuitBreaker", 8, 10);

        // Act
        var result = RoundTrip<ContinuationRequestEvent>(evt);

        // Assert
        Assert.Equal("cont-1", result.ContinuationId);
        Assert.Equal(8, result.CurrentIteration);
        Assert.Equal(10, result.MaxIterations);
    }

    [Fact]
    public void FromJson_ContinuationResponseEvent_RoundTrips()
    {
        // Arrange
        var evt = new ContinuationResponseEvent("cont-1", "CircuitBreaker", true, ExtensionAmount: 5);

        // Act
        var result = RoundTrip<ContinuationResponseEvent>(evt);

        // Assert
        Assert.Equal("cont-1", result.ContinuationId);
        Assert.True(result.Approved);
        Assert.Equal(5, result.ExtensionAmount);
    }

    #endregion

    // -------------------------------------------------------------------------
    // Hierarchical / nested-agent (ExecutionContext depth)
    // -------------------------------------------------------------------------

    #region Hierarchical Execution Context Tests

    [Fact]
    public void FromJson_NestedAgent_Depth2_ExecutionContextPreserved()
    {
        // Arrange
        var evt = new TextDeltaEvent("deep thought", "msg-1")
        {
            ExecutionContext = new AgentExecutionContext
            {
                AgentName = "Leaf",
                AgentId = "leaf-id",
                ParentAgentId = "mid-id",
                AgentChain = ["Root", "Mid", "Leaf"],
                Depth = 2
            }
        };

        // Act
        var result = RoundTrip<TextDeltaEvent>(evt);

        // Assert
        Assert.NotNull(result.ExecutionContext);
        Assert.Equal("Leaf", result.ExecutionContext.AgentName);
        Assert.Equal("leaf-id", result.ExecutionContext.AgentId);
        Assert.Equal("mid-id", result.ExecutionContext.ParentAgentId);
        Assert.Equal(2, result.ExecutionContext.Depth);
        Assert.Equal(3, result.ExecutionContext.AgentChain.Count);
        Assert.Equal("Root", result.ExecutionContext.AgentChain[0]);
        Assert.Equal("Mid", result.ExecutionContext.AgentChain[1]);
        Assert.Equal("Leaf", result.ExecutionContext.AgentChain[2]);
    }

    [Fact]
    public void FromJson_NestedAgentInvokedEvent_RoundTrips()
    {
        // Arrange
        var ts = DateTimeOffset.UtcNow;
        var evt = new NestedAgentInvokedEvent("Orchestrator", "Child", 1, ts);

        // Act
        var result = RoundTrip<NestedAgentInvokedEvent>(evt);

        // Assert
        Assert.Equal("Orchestrator", result.OrchestratorName);
        Assert.Equal("Child", result.ChildAgentName);
        Assert.Equal(1, result.NestingDepth);
    }

    [Fact]
    public void FromJson_TextDelta_FromNestedAgent_ExecutionContextDepthSurvives()
    {
        // Arrange
        var evt = new TextDeltaEvent("answer", "msg-1")
        {
            ExecutionContext = new AgentExecutionContext
            {
                AgentName = "DeepAgent",
                AgentId = "deep-id",
                AgentChain = ["Root", "L1", "L2", "DeepAgent"],
                Depth = 3
            }
        };

        // Act
        var result = RoundTrip<TextDeltaEvent>(evt);

        // Assert
        Assert.NotNull(result.ExecutionContext);
        Assert.Equal(3, result.ExecutionContext.Depth);
        Assert.True(result.ExecutionContext.IsSubAgent);
        Assert.Equal(4, result.ExecutionContext.AgentChain.Count);
    }

    #endregion
}
