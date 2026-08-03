using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using HPD.Agent.Security;
using Microsoft.Extensions.AI;
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

    [Fact]
    public void ToJson_UserInputEvents_UseInputDiscriminators()
    {
        var messagesJson = AgentEventSerializer.ToJson(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "hello")],
            SessionId = "s1",
            ThreadId = "main"
        });

        Assert.Contains("\"type\":\"USER_MESSAGES_INPUT\"", messagesJson);
        Assert.Contains("\"sessionId\":\"s1\"", messagesJson);
    }

    [Fact]
    public void ThreadExecutionFinishedEvent_RoundTripsExplicitFailureOutcome()
    {
        var finishedAt = DateTimeOffset.Parse("2026-07-21T13:00:00Z");
        var original = new ThreadExecutionFinishedEvent(
            "execution-1",
            "agent-1",
            ThreadExecutionOutcome.Failed,
            finishedAt,
            new ThreadExecutionError("InvalidOperationException", "provider failed"));

        var json = AgentEventSerializer.ToJson(original);
        var rehydrated = Assert.IsType<ThreadExecutionFinishedEvent>(AgentEventSerializer.FromJson(json));

        Assert.Contains("\"type\":\"THREAD_EXECUTION_FINISHED\"", json);
        Assert.Contains("\"threadExecutionId\":\"execution-1\"", json);
        Assert.Contains("\"outcome\":\"Failed\"", json);
        Assert.Equal("execution-1", rehydrated.ThreadExecutionId);
        Assert.Equal(ThreadExecutionOutcome.Failed, rehydrated.Outcome);
        Assert.Equal(finishedAt, rehydrated.FinishedAt);
        Assert.Equal(new ThreadExecutionError("InvalidOperationException", "provider failed"), rehydrated.Error);
    }

    [Fact]
    public void ThreadExecutionFinishedEvent_RejectsContradictoryTerminalState()
    {
        Assert.Throws<ArgumentException>(() => new ThreadExecutionFinishedEvent(
            "execution-1",
            "agent-1",
            ThreadExecutionOutcome.Failed,
            DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentException>(() => new ThreadExecutionFinishedEvent(
            "execution-1",
            "agent-1",
            ThreadExecutionOutcome.Succeeded,
            DateTimeOffset.UtcNow,
            new ThreadExecutionError("Unexpected", "must not be present")));
    }

    [Fact]
    public void FromJson_UserMessagesInputEvent_RoundTrips()
    {
        var json = AgentEventSerializer.ToJson(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "hello")],
            SessionId = "s1",
            ThreadId = "main",
            RunConfig = new AgentRunConfig
            {
                ProviderKey = "openai",
                ModelId = "gpt-5.5",
                CoalesceDeltas = true,
                Chat = new ChatRunConfig
                {
                    Temperature = 0.7,
                    MaxOutputTokens = 123,
                    Seed = 42
                },
                Audio = new AudioRunConfig
                {
                    OutputMode = AudioOutputMode.TextToSpeech,
                    AssistantOutputMode = AssistantOutputSynthesisMode.Progressive,
                    VoiceId = "voice-1",
                    OutputFormat = "mp3_44100_128",
                    EnablePlayback = true
                }
            }
        });

        var result = Assert.IsType<UserMessagesInputEvent>(AgentEventSerializer.FromJson(json));

        Assert.Equal("s1", result.SessionId);
        Assert.Equal("main", result.ThreadId);
        Assert.Single(result.Messages);
        Assert.Equal(ChatRole.User, result.Messages[0].Role);
        Assert.Equal("hello", result.Messages[0].Text);
        Assert.NotNull(result.RunConfig);
        Assert.Equal("openai", result.RunConfig!.ProviderKey);
        Assert.Equal("gpt-5.5", result.RunConfig.ModelId);
        Assert.True(result.RunConfig!.CoalesceDeltas);
        Assert.Equal(0.7, result.RunConfig.Chat!.Temperature);
        Assert.Equal(123, result.RunConfig.Chat.MaxOutputTokens);
        Assert.Equal(42, result.RunConfig.Chat.Seed);
        Assert.NotNull(result.RunConfig.Audio);
        Assert.Equal(AudioOutputMode.TextToSpeech, result.RunConfig.Audio!.OutputMode);
        Assert.Equal(AssistantOutputSynthesisMode.Progressive, result.RunConfig.Audio.AssistantOutputMode);
        Assert.Equal("voice-1", result.RunConfig.Audio.VoiceId);
        Assert.Equal("mp3_44100_128", result.RunConfig.Audio.OutputFormat);
        Assert.True(result.RunConfig.Audio.EnablePlayback);
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

    #region Agent Metadata Tests

    [Fact]
    public void ToJson_Metadata_SerializesCorrectly()
    {
        // Arrange
        var context = new AgentMetadata
        {
            AgentName = "SubAgent A",
            AgentId = "parent-abc-subagent-def",
            Depth = 2,
            AgentChain = new[] { "Root", "Parent", "SubAgent A" }
        };
        var evt = new TextDeltaEvent("hello", "msg-123")
        {
            Metadata = context
        };

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert
        Assert.Contains("\"metadata\"", json);
        Assert.Contains("\"agentName\":\"SubAgent A\"", json);
        Assert.Contains("\"depth\":2", json);
    }

    [Fact]
    public void ToJson_NullMetadata_IsOmitted()
    {
        // Arrange
        var evt = new TextDeltaEvent("hello", "msg-123");

        // Act
        var json = AgentEventSerializer.ToJson(evt);

        // Assert
        Assert.DoesNotContain("metadata", json);
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
        Assert.Contains("\"isError\":true", errorJson);
        Assert.Contains("\"errorMessage\":\"Test error\"", errorJson);
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
        Assert.DoesNotContain("\"toolHarnessName\"", startJson);
        Assert.DoesNotContain("\"callType\"", startJson);

        // ToolCallStartEvent — with toolharness and callType
        var startEvtFull = new ToolCallStartEvent("call-2", "Add", "msg-1", "MathToolHarness", ToolCallType.Function);
        var startJsonFull = AgentEventSerializer.ToJson(startEvtFull);
        Assert.Contains("\"toolHarnessName\":\"MathToolHarness\"", startJsonFull);
        Assert.Contains("\"callType\":\"Function\"", startJsonFull);

        // ToolCallStartEvent — SubAgent type
        var subAgentEvt = new ToolCallStartEvent("call-3", "ResearchAgent", "msg-1", "AgentToolHarness", ToolCallType.SubAgent);
        var subAgentJson = AgentEventSerializer.ToJson(subAgentEvt);
        Assert.Contains("\"callType\":\"SubAgent\"", subAgentJson);

        // ToolCallArgsEvent
        var argsEvt = new ToolCallArgsEvent("call-1", "{\"x\":1,\"y\":2}");
        var argsJson = AgentEventSerializer.ToJson(argsEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_ARGS\"", argsJson);

        // ToolCallEndEvent
        var endEvt = new ToolCallEndEvent("call-1", "msg-1", "Calculator", "{\"x\":1,\"y\":2}");
        var endJson = AgentEventSerializer.ToJson(endEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_END\"", endJson);
        Assert.Contains("\"messageId\":\"msg-1\"", endJson);
        Assert.Contains("\"name\":\"Calculator\"", endJson);
        using (var endDocument = JsonDocument.Parse(endJson))
        {
            Assert.Equal("{\"x\":1,\"y\":2}", endDocument.RootElement.GetProperty("argsJson").GetString());
        }

        // ToolCallResultEvent — minimal
        var resultEvt = new ToolCallResultEvent("call-1", new ToolResultPayload(Text: "3"));
        var resultJson = AgentEventSerializer.ToJson(resultEvt);
        Assert.Contains("\"type\":\"TOOL_CALL_RESULT\"", resultJson);
        Assert.Contains("\"result\":", resultJson);
        Assert.Contains("\"text\":\"3\"", resultJson);
        Assert.DoesNotContain("\"toolHarnessName\"", resultJson);
        Assert.DoesNotContain("\"callType\"", resultJson);

        // ToolCallResultEvent — with toolharness and callType
        var resultEvtFull = new ToolCallResultEvent("call-2", new ToolResultPayload(Text: "42"), "MathToolHarness", ToolCallType.Function);
        var resultJsonFull = AgentEventSerializer.ToJson(resultEvtFull);
        Assert.Contains("\"toolHarnessName\":\"MathToolHarness\"", resultJsonFull);
        Assert.Contains("\"callType\":\"Function\"", resultJsonFull);
        Assert.DoesNotContain("\"name\"", resultJsonFull);

        // ToolCallResultEvent — with name
        var resultEvtNamed = new ToolCallResultEvent(
            "call-3",
            new ToolResultPayload(Text: "read"),
            "CodingToolHarness",
            ToolCallType.Function,
            "ReadFile");
        var resultJsonNamed = AgentEventSerializer.ToJson(resultEvtNamed);
        Assert.Contains("\"name\":\"ReadFile\"", resultJsonNamed);
    }

    [Fact]
    public void ToolCallStartEvent_RoundTrip_PreservesAllFields()
    {
        var evt = new ToolCallStartEvent("call-rt", "Add", "msg-rt", "MathToolHarness", ToolCallType.Function);
        var json = AgentEventSerializer.ToJson(evt);
        var result = Assert.IsType<ToolCallStartEvent>(AgentEventSerializer.FromJson(json));

        Assert.Equal("call-rt", result.CallId);
        Assert.Equal("Add", result.Name);
        Assert.Equal("msg-rt", result.MessageId);
        Assert.Equal("MathToolHarness", result.ToolHarnessName);
        Assert.Equal(ToolCallType.Function, result.CallType);
    }

    [Fact]
    public void ToolCallResultEvent_RoundTrip_PreservesAllFields()
    {
        var evt = new ToolCallResultEvent("call-rt", new ToolResultPayload(Text: "42"), "MathToolHarness", ToolCallType.SubAgent, "Add");
        var json = AgentEventSerializer.ToJson(evt);
        var result = Assert.IsType<ToolCallResultEvent>(AgentEventSerializer.FromJson(json));

        Assert.Equal("call-rt", result.CallId);
        Assert.Equal("42", result.Result.Text);
        Assert.Equal("MathToolHarness", result.ToolHarnessName);
        Assert.Equal(ToolCallType.SubAgent, result.CallType);
        Assert.Equal("Add", result.Name);
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

        // PermissionResponseEvent
        var responseEvt = new PermissionResponseEvent("perm-1", "Source", Approved: false, Reason: "User rejected");
        var responseJson = AgentEventSerializer.ToJson(responseEvt);
        Assert.Contains("\"type\":\"PERMISSION_RESPONSE\"", responseJson);
        Assert.Contains("\"reason\":\"User rejected\"", responseJson);
    }

    [Fact]
    public void ToJson_MiddlewareEvents_SerializeCorrectly()
    {
        // MiddlewareErrorEvent
        var errorEvt = new MiddlewareErrorEvent("TestMiddleware", "Something went wrong");
        var errorJson = AgentEventSerializer.ToJson(errorEvt);
        Assert.Contains("\"type\":\"MIDDLEWARE_ERROR\"", errorJson);
        Assert.Contains("\"isError\":true", errorJson);
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
        Assert.Equal("USER_MESSAGES_INPUT", AgentEventSerializer.GetEventTypeName(typeof(UserMessagesInputEvent)));
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

    }

    [Fact]
    public void ToJson_MiddlewareStateSnapshotEvent_SerializesCorrectly()
    {
        var evt = new MiddlewareStateSnapshotEvent(
            AgentName: "TestAgent",
            SessionId: "session-1",
            ThreadId: "main",
            Iteration: 2,
            Phase: "before_model_call",
            BatchId: null,
            FunctionCallId: null,
            ToolCallIndex: null,
            StateCount: 1,
            States:
            [
                new MiddlewareStateEntrySnapshot(
                    Key: "HPD.Agent.ErrorTrackingStateData",
                    Type: typeof(ErrorTrackingStateData).FullName!,
                    PropertyName: "ErrorTracking",
                    Scope: StateScope.Thread,
                    Persistent: false,
                    Version: 1,
                    Json: JsonSerializer.SerializeToElement(new { ConsecutiveFailures = 3 }),
                    Error: null,
                    Redacted: false)
            ],
            Timestamp: DateTimeOffset.UtcNow);

        var json = AgentEventSerializer.ToJson(evt);

        Assert.Contains("\"type\":\"MIDDLEWARE_STATE_SNAPSHOT\"", json);
        Assert.Contains("\"phase\":\"before_model_call\"", json);
        Assert.Contains("\"stateCount\":1", json);
        Assert.Contains("\"propertyName\":\"ErrorTracking\"", json);
        Assert.Contains("\"consecutiveFailures\":3", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToJson_MiddlewareStateChangedEvent_SerializesCorrectly()
    {
        var evt = new MiddlewareStateChangedEvent(
            AgentName: "TestAgent",
            SessionId: "session-1",
            ThreadId: "main",
            Iteration: 2,
            Phase: "after_parallel_batch",
            BatchId: "batch-1",
            FunctionCallId: null,
            ToolCallIndex: null,
            ChangeCount: 1,
            Changes:
            [
                new MiddlewareStateChange(
                    Key: "HPD.Agent.ErrorTrackingStateData",
                    Type: typeof(ErrorTrackingStateData).FullName!,
                    PropertyName: "ErrorTracking",
                    Scope: StateScope.Thread,
                    Persistent: false,
                    Version: 1,
                    ChangeType: "updated",
                    Before: JsonSerializer.SerializeToElement(new { ConsecutiveFailures = 1 }),
                    After: JsonSerializer.SerializeToElement(new { ConsecutiveFailures = 2 }),
                    Error: null,
                    Redacted: false)
            ],
            Timestamp: DateTimeOffset.UtcNow);

        var json = AgentEventSerializer.ToJson(evt);

        Assert.Contains("\"type\":\"MIDDLEWARE_STATE_CHANGED\"", json);
        Assert.Contains("\"phase\":\"after_parallel_batch\"", json);
        Assert.Contains("\"changeCount\":1", json);
        Assert.Contains("\"changeType\":\"updated\"", json);
        Assert.Contains("\"batchId\":\"batch-1\"", json);
    }

    [Fact]
    public void BackgroundTaskEvents_UseBackgroundTaskDiscriminators()
    {
        var invocation = CreateInvocationSnapshot();
        var events = new AgentEvent[]
        {
            new BackgroundTaskStartedEvent
            {
                TaskId = "task-1",
                Name = "work",
                SourceKind = BackgroundTaskSourceKind.ToolCall,
                Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true),
                Invocation = invocation,
                StartedAt = DateTimeOffset.UnixEpoch
            },
            new BackgroundTaskCompletedEvent
            {
                TaskId = "task-1",
                Name = "work",
                SourceKind = BackgroundTaskSourceKind.ToolCall,
                Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true),
                Invocation = invocation,
                CompletedAt = DateTimeOffset.UnixEpoch.AddMilliseconds(12),
                DurationMilliseconds = 12
            },
            new BackgroundTaskCancelledEvent
            {
                TaskId = "task-1",
                Name = "work",
                SourceKind = BackgroundTaskSourceKind.ToolCall,
                Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true),
                Invocation = invocation,
                CancelledAt = DateTimeOffset.UnixEpoch
            },
            new BackgroundTaskFaultedEvent
            {
                TaskId = "task-1",
                Name = "work",
                SourceKind = BackgroundTaskSourceKind.ToolCall,
                Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true),
                Invocation = invocation,
                FaultedAt = DateTimeOffset.UnixEpoch,
                ExceptionType = "System.InvalidOperationException",
                ErrorMessage = "boom"
            }
        };

        var expectedTypes = new[]
        {
            EventTypes.BackgroundTask.BACKGROUND_TASK_STARTED,
            EventTypes.BackgroundTask.BACKGROUND_TASK_COMPLETED,
            EventTypes.BackgroundTask.BACKGROUND_TASK_CANCELLED,
            EventTypes.BackgroundTask.BACKGROUND_TASK_FAULTED
        };

        for (var i = 0; i < events.Length; i++)
        {
            var json = AgentEventSerializer.ToJson(events[i]);
            Assert.Contains($"\"type\":\"{expectedTypes[i]}\"", json);
            Assert.DoesNotContain("\"type\":\"BACKGROUND_OPERATION_", json);
        }
    }

    [Fact]
    public void BackgroundTaskStartedEvent_RoundTrips()
    {
        var evt = new BackgroundTaskStartedEvent
        {
            TaskId = "task-1",
            Name = "work",
            SourceKind = BackgroundTaskSourceKind.ToolCall,
            Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true),
            Invocation = CreateInvocationSnapshot(),
            StartedAt = DateTimeOffset.UnixEpoch
        };

        var json = AgentEventSerializer.ToJson(evt);
        var result = Assert.IsType<BackgroundTaskStartedEvent>(
            AgentEventSerializer.FromEventJson(json));

        Assert.Equal(evt.TaskId, result.TaskId);
        Assert.Equal(evt.Name, result.Name);
        Assert.Equal(evt.SourceKind, result.SourceKind);
        Assert.Equal(evt.Notification, result.Notification);
        Assert.NotNull(result.Invocation);
        Assert.Equal(evt.Invocation.BatchId, result.Invocation!.BatchId);
        Assert.Equal(evt.Invocation.ToolCallIndex, result.Invocation.ToolCallIndex);
    }

    [Fact]
    public void ModelCallRetryEvent_RehydratedEvent_CanBeSerializedAgain()
    {
        var original = new ModelCallRetryEvent(
            Attempt: 1,
            MaxRetries: 3,
            Delay: TimeSpan.FromSeconds(2),
            ExceptionType: typeof(HttpRequestException).FullName!,
            ErrorMessage: "Request failed (429)")
        {
            Exception = new HttpRequestException("Request failed", null, System.Net.HttpStatusCode.TooManyRequests)
        };

        var persisted = AgentEventSerializer.ToJson(original);
        var rehydrated = Assert.IsType<ModelCallRetryEvent>(AgentEventSerializer.FromJson(persisted));

        Assert.Null(rehydrated.Exception);
        Assert.Equal(ErrorHandling.ErrorCategory.RateLimitRetryable, rehydrated.Category);
        var replayed = AgentEventSerializer.ToJson(rehydrated);
        Assert.Contains("\"type\":\"MODEL_CALL_RETRY\"", replayed);
    }

    [Fact]
    public void FunctionRetryEvent_RehydratedEvent_CanBeSerializedAgain()
    {
        var original = new FunctionRetryEvent(
            FunctionName: "FlakyFunction",
            Attempt: 2,
            MaxRetries: 4,
            Delay: TimeSpan.FromMilliseconds(500),
            ExceptionType: typeof(HttpRequestException).FullName!,
            ErrorMessage: "Provider returned Status: 503")
        {
            Exception = new HttpRequestException("Provider returned Status: 503")
        };

        var persisted = AgentEventSerializer.ToJson(original);
        var rehydrated = Assert.IsType<FunctionRetryEvent>(AgentEventSerializer.FromJson(persisted));

        Assert.Null(rehydrated.Exception);
        Assert.Equal(ErrorHandling.ErrorCategory.ServerError, rehydrated.Category);
        var replayed = AgentEventSerializer.ToJson(rehydrated);
        Assert.Contains("\"type\":\"FUNCTION_RETRY\"", replayed);
    }

    #endregion

    [Fact]
    public void CapabilityEvents_RoundTripThroughTheGeneralEventSerializer()
    {
        var original = new AgentCapabilityRequestEvent(
            "request-1",
            "test-harness",
            "call-1",
            "read",
            AgentCapabilityKind.FilesystemRead,
            new AgentCapabilityResource
            {
                Value = "/outside/file.txt",
                DisplayName = "file.txt"
            },
            "Read the selected file.");

        var json = AgentEventSerializer.ToJson(original);
        var rehydrated = Assert.IsType<AgentCapabilityRequestEvent>(
            AgentEventSerializer.FromJson(json));

        Assert.Equal(original.RequestId, rehydrated.RequestId);
        Assert.Equal(original.Capability, rehydrated.Capability);
        Assert.Equal(original.Resource, rehydrated.Resource);
        Assert.Equal("AGENT_CAPABILITY_REQUEST", AgentEventSerializer.GetEventTypeName(original));
    }

    private static FunctionInvocationSnapshot CreateInvocationSnapshot()
        => new()
        {
            AgentName = "TestAgent",
            FunctionCallId = "call-1",
            FunctionName = "TestFunction",
            ConversationId = "conversation-1",
            SessionId = "session-1",
            ThreadId = "thread-1",
            TraceId = "trace-1",
            Invocation = new ToolInvocationInfo("batch-1", "call-1", "TestFunction", 2)
        };
}
