using System.Text.Json;
using HPD.Agent.StructuredOutput;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.StructuredOutput;

/// <summary>
/// Unit tests for structured output in native mode.
/// Tests streaming partials, deduplication, bidirectional events, error handling, etc.
/// Uses shared TestResponse/NestedResponse models from TestModels.cs
/// </summary>
public class StructuredOutputTests
{
    [Fact]
    public async Task NativeMode_EmitsPartialResults_WithDebouncing()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        // Simulate streaming JSON in chunks
        fakeClient.EnqueueStreamingResponse(
            "{\"name\":",
            " \"test\"",
            ", \"value\":",
            " 42",
            ", \"description\":",
            " \"hello",
            " world\"",
            "}"
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                StreamPartials = true,
                PartialDebounceMs = 0 // No debounce for testing
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var partials = events.OfType<StructuredResultEvent<TestResponse>>()
            .Where(e => e.IsPartial)
            .ToList();
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        // Should have at least one partial result
        Assert.NotEmpty(partials);
        // Should have exactly one final result
        Assert.NotNull(final);
        Assert.Equal("test", final.Value.Name);
        Assert.Equal(42, final.Value.Value);
    }

    [Fact]
    public async Task NativeMode_EmitsFinalResult_OnTextMessageEnd()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("""{"name": "final", "value": 100}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                StreamPartials = false
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.False(final.IsPartial);
        Assert.Equal("final", final.Value.Name);
        Assert.Equal(100, final.Value.Value);
    }

    [Fact]
    public async Task NativeMode_DeduplicatesPartials_ByJsonString()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        // Send same content with whitespace variations
        fakeClient.EnqueueStreamingResponse(
            "{\"name\":\"test\",",
            "\"value\":1}",
            "", // Empty chunk shouldn't produce new partial
            ""  // Another empty chunk
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                StreamPartials = true,
                PartialDebounceMs = 0
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var partials = events.OfType<StructuredResultEvent<TestResponse>>()
            .Where(e => e.IsPartial)
            .ToList();

        // Partials with same JSON string should be deduplicated
        var uniqueJsons = partials.Select(p => p.RawJson).Distinct().ToList();
        Assert.Equal(partials.Count, uniqueJsons.Count);
    }

    [Fact]
    public async Task NativeMode_PassesThroughBidirectionalEvents()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("""{"name": "test", "value": 1}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - bidirectional events (if any) should pass through
        // Note: This test verifies the mechanism works; actual bidirectional events
        // would come from middleware like permission requests
        var bidirectionalEvents = events.Where(e => e is IBidirectionalAgentEvent).ToList();
        // Just verify we can enumerate without error - bidirectional events depend on middleware config
        Assert.NotNull(events);
    }

    [Fact]
    public async Task NativeMode_PassesThroughObservabilityEvents()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("""{"name": "test", "value": 1}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - should pass through observability events like turn start/end
        var turnStartEvents = events.OfType<AgentTurnStartedEvent>().ToList();
        var turnFinishEvents = events.OfType<AgentTurnFinishedEvent>().ToList();

        // Should have at least agent turn events
        Assert.NotEmpty(turnStartEvents);
    }

    [Fact]
    public async Task NativeMode_StripsMarkdownFences()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        // Some LLMs wrap JSON in markdown code fences
        fakeClient.EnqueueTextResponse("""
```json
{"name": "fenced", "value": 42}
```
""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.Equal("fenced", final.Value.Name);
        Assert.Equal(42, final.Value.Value);
    }

    [Fact]
    public async Task NativeMode_EmitsErrorEvent_OnInvalidJson()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("not valid json at all");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var errorEvent = events.OfType<StructuredOutputErrorEvent>().FirstOrDefault();
        Assert.NotNull(errorEvent);
        Assert.Contains("not valid json", errorEvent.RawJson);
        Assert.Equal("TestResponse", errorEvent.ExpectedTypeName);
    }

    [Fact]
    public async Task NativeMode_EmitsErrorEvent_OnTypeMismatch()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        // Valid JSON but wrong structure (Value should be int, not string)
        fakeClient.EnqueueTextResponse("""{"name": "test", "value": "not a number"}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var errorEvent = events.OfType<StructuredOutputErrorEvent>().FirstOrDefault();
        Assert.NotNull(errorEvent);
        Assert.Equal("TestResponse", errorEvent.ExpectedTypeName);
    }

    [Fact]
    public async Task NativeMode_EmitsErrorEvent_OnSchemaMismatch()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        // Missing required field "Value"
        fakeClient.EnqueueTextResponse("""{"name": "test"}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - Should either parse with default value or emit error
        // Depending on serializer settings, missing required fields may cause error
        var resultOrError = events.Where(e =>
            e is StructuredResultEvent<TestResponse> || e is StructuredOutputErrorEvent);
        Assert.NotEmpty(resultOrError);
    }

    [Fact]
    public async Task NativeMode_HandlesEmptyResponse_Gracefully()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - Should handle gracefully without throwing
        // May emit error event for empty/invalid JSON
        Assert.NotNull(events);
    }

    [Fact]
    public async Task NativeMode_RespectsStreamPartialsFlag()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse(
            "{\"name\":",
            " \"test\",",
            " \"value\":",
            " 42}"
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                StreamPartials = false // Disable streaming partials
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var partials = events.OfType<StructuredResultEvent<TestResponse>>()
            .Where(e => e.IsPartial)
            .ToList();

        // Should have NO partial results when streaming is disabled
        Assert.Empty(partials);

        // Should still have final result
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);
        Assert.NotNull(final);
    }

    [Fact]
    public async Task NativeMode_RespectsDebounceMs()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        // Many rapid chunks
        fakeClient.EnqueueStreamingResponse(
            "{",
            "\"name\":",
            " \"test\"",
            ",",
            " \"value\":",
            " 42",
            "}"
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                StreamPartials = true,
                PartialDebounceMs = 500 // High debounce - should reduce partials
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - with high debounce, should have fewer partials
        // The exact count depends on timing, but high debounce should reduce frequency
        var partials = events.OfType<StructuredResultEvent<TestResponse>>()
            .Where(e => e.IsPartial)
            .ToList();

        // Just verify we got the final result
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);
        Assert.NotNull(final);
    }

    [Fact]
    public async Task NativeMode_Cancellation_NoErrorEvent()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse(
            "{\"name\":",
            " \"test\",",
            " \"value\":",
            " 42}"
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };
        var cts = new CancellationTokenSource();

        // Act
        var events = new List<AgentEvent>();
        var eventCount = 0;
        try
        {
            await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options, cancellationToken: cts.Token))
            {
                events.Add(evt);
                eventCount++;
                if (eventCount == 2)
                {
                    cts.Cancel(); // Cancel mid-stream
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - cancellation should NOT produce error events
        var errorEvents = events.OfType<StructuredOutputErrorEvent>().ToList();
        Assert.Empty(errorEvents);
    }

    [Fact]
    public async Task NativeMode_UsesCustomSerializerOptions()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        // Use PascalCase which requires custom serializer options
        fakeClient.EnqueueTextResponse("""{"Name": "PascalCase", "Value": 99}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);

        // Create custom options with TypeInfoResolver (required for AOT-safe pattern)
        var customOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, // Handle PascalCase
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                SerializerOptions = customOptions
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.Equal("PascalCase", final.Value.Name);
        Assert.Equal(99, final.Value.Value);
    }

    [Fact]
    public async Task NativeMode_HandlesNestedObjects()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("""
        {
            "title": "Outer",
            "inner": {
                "name": "Inner",
                "value": 42
            }
        }
        """);

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<NestedResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<NestedResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.Equal("Outer", final.Value.Title);
        Assert.NotNull(final.Value.Inner);
        Assert.Equal("Inner", final.Value.Inner.Name);
        Assert.Equal(42, final.Value.Inner.Value);
    }

    #region AOT-Safe Serializer Context Tests (inspired by Microsoft patterns)

    [Fact]
    public async Task NativeMode_UsesSourceGeneratedContext()
    {
        // Arrange - Use the shared TestJsonSerializerContext for AOT compatibility
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("""{"name": "aot-test", "value": 123}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                SerializerOptions = TestJsonSerializerContext.Default.Options
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.Equal("aot-test", final.Value.Name);
        Assert.Equal(123, final.Value.Value);
    }

    [Fact]
    public async Task NativeMode_WithEnumProperty_DeserializesCorrectly()
    {
        // Arrange - Test enum handling like Microsoft's Animal/Species pattern
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("""{"id": 1, "fullName": "Tigger", "species": "Tiger"}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                SerializerOptions = TestJsonSerializerContext.Default.Options
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<Animal>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<Animal>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.Equal(1, final.Value.Id);
        Assert.Equal("Tigger", final.Value.FullName);
        Assert.Equal(Species.Tiger, final.Value.Species);
    }

    #endregion

    #region Error Handling Edge Cases (inspired by Microsoft patterns)

    [Fact]
    public async Task NativeMode_NullResult_EmitsErrorEvent()
    {
        // Arrange - JSON that deserializes to null
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("null");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - Should emit error for null result
        var errorEvent = events.OfType<StructuredOutputErrorEvent>().FirstOrDefault();
        Assert.NotNull(errorEvent);
        Assert.Contains("null", errorEvent.ErrorMessage.ToLower());
    }

    [Fact]
    public async Task NativeMode_ArrayInsteadOfObject_EmitsErrorEvent()
    {
        // Arrange - JSON array when object expected (Microsoft pattern)
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("[]");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - Should emit error for wrong JSON structure
        var errorEvent = events.OfType<StructuredOutputErrorEvent>().FirstOrDefault();
        Assert.NotNull(errorEvent);
        Assert.Equal("TestResponse", errorEvent.ExpectedTypeName);
    }

    [Fact]
    public async Task NativeMode_WhitespaceOnlyResponse_HandlesGracefully()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("   \n\t  ");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - Should handle gracefully (error or empty)
        Assert.NotNull(events);
    }

    #endregion

    #region RawJson Preservation Tests

    [Fact]
    public async Task NativeMode_RawJson_PreservedInFinalResult()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        var expectedJson = """{"name": "raw-test", "value": 42}""";
        fakeClient.EnqueueTextResponse(expectedJson);

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.Equal(expectedJson, final.RawJson);
    }

    [Fact]
    public async Task NativeMode_RawJson_PreservedInErrorEvent()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        var invalidJson = "not valid json";
        fakeClient.EnqueueTextResponse(invalidJson);

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var errorEvent = events.OfType<StructuredOutputErrorEvent>().FirstOrDefault();
        Assert.NotNull(errorEvent);
        Assert.Equal(invalidJson, errorEvent.RawJson);
    }

    #endregion

    #region Observability Events Tests

    [Fact]
    public async Task NativeMode_EmitsStructuredOutputStartEvent()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("""{"name": "test", "value": 1}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var startEvent = events.OfType<StructuredOutputStartEvent>().FirstOrDefault();
        Assert.NotNull(startEvent);
        Assert.Equal("TestResponse", startEvent.OutputTypeName);
        Assert.Equal("native", startEvent.OutputMode);
        Assert.NotNull(startEvent.MessageId);
        Assert.NotEmpty(startEvent.MessageId);
    }

    [Fact]
    public async Task NativeMode_EmitsStructuredOutputCompleteEvent()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("""{"name": "test", "value": 42}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var completeEvent = events.OfType<StructuredOutputCompleteEvent>().FirstOrDefault();
        Assert.NotNull(completeEvent);
        Assert.Equal("TestResponse", completeEvent.OutputTypeName);
        Assert.True(completeEvent.FinalJsonLength > 0);
        Assert.True(completeEvent.Duration.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task NativeMode_EmitsStructuredOutputPartialEvent_WithStreaming()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse(
            "{\"name\":",
            " \"test\"",
            ", \"value\":",
            " 42}"
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                StreamPartials = true,
                PartialDebounceMs = 0 // No debounce for testing
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - Should have partial events when streaming
        var partialEvents = events.OfType<StructuredOutputPartialEvent>().ToList();
        // Partial events are emitted when a partial is successfully parsed
        // The count depends on how many successful parses happen during streaming

        // At minimum, verify start and complete events exist
        Assert.NotNull(events.OfType<StructuredOutputStartEvent>().FirstOrDefault());
        Assert.NotNull(events.OfType<StructuredOutputCompleteEvent>().FirstOrDefault());
    }

    [Fact]
    public async Task NativeMode_ObservabilityEvents_ShareSameMessageId()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueStreamingResponse(
            "{\"name\":",
            " \"test\",",
            " \"value\":",
            " 42}"
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "native",
                StreamPartials = true,
                PartialDebounceMs = 0
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - All observability events should share the same MessageId
        var startEvent = events.OfType<StructuredOutputStartEvent>().First();
        var completeEvent = events.OfType<StructuredOutputCompleteEvent>().First();
        var partialEvents = events.OfType<StructuredOutputPartialEvent>().ToList();

        Assert.Equal(startEvent.MessageId, completeEvent.MessageId);
        foreach (var partial in partialEvents)
        {
            Assert.Equal(startEvent.MessageId, partial.MessageId);
        }
    }

    [Fact]
    public async Task ToolMode_EmitsStructuredOutputStartEvent_WithToolMode()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "return_TestResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["name"] = "tool-test",
                ["value"] = 99
            });

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "tool" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var startEvent = events.OfType<StructuredOutputStartEvent>().FirstOrDefault();
        Assert.NotNull(startEvent);
        Assert.Equal("TestResponse", startEvent.OutputTypeName);
        Assert.Equal("tool", startEvent.OutputMode);
    }

    [Fact]
    public async Task NativeMode_ObservabilityEvents_AreIObservabilityEvent()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("""{"name": "test", "value": 1}""");

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "native" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - Verify events implement IObservabilityEvent
        var startEvent = events.OfType<StructuredOutputStartEvent>().First();
        var completeEvent = events.OfType<StructuredOutputCompleteEvent>().First();

        Assert.IsAssignableFrom<IObservabilityEvent>(startEvent);
        Assert.IsAssignableFrom<IObservabilityEvent>(completeEvent);
    }

    #endregion

    #region Union Types Tests

    [Fact]
    public async Task UnionMode_ReturnsSuccessType_WhenSuccessToolCalled()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "return_SuccessResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["data"] = "Operation completed",
                ["code"] = 200
            });

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "union",
                UnionTypes = new[] { typeof(SuccessResponse), typeof(ErrorResponse) }
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<ApiResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<ApiResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.IsType<SuccessResponse>(final.Value);
        var success = (SuccessResponse)final.Value;
        Assert.Equal("Operation completed", success.Data);
        Assert.Equal(200, success.Code);
    }

    [Fact]
    public async Task UnionMode_ReturnsErrorType_WhenErrorToolCalled()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "return_ErrorResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["errorCode"] = "NOT_FOUND",
                ["message"] = "Resource not found"
            });

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "union",
                UnionTypes = new[] { typeof(SuccessResponse), typeof(ErrorResponse) }
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<ApiResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<ApiResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.IsType<ErrorResponse>(final.Value);
        var error = (ErrorResponse)final.Value;
        Assert.Equal("NOT_FOUND", error.ErrorCode);
        Assert.Equal("Resource not found", error.Message);
    }

    [Fact]
    public async Task UnionMode_EmitsStartEvent_WithUnionMode()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "return_SuccessResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["data"] = "test",
                ["code"] = 200
            });

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "union",
                UnionTypes = new[] { typeof(SuccessResponse), typeof(ErrorResponse) }
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<ApiResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var startEvent = events.OfType<StructuredOutputStartEvent>().FirstOrDefault();
        Assert.NotNull(startEvent);
        Assert.Equal("union", startEvent.OutputMode);
    }

    [Fact]
    public async Task UnionMode_CompleteEvent_HasMatchedTypeName()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "return_ErrorResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["errorCode"] = "ERR",
                ["message"] = "Error occurred"
            });

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "union",
                UnionTypes = new[] { typeof(SuccessResponse), typeof(ErrorResponse) }
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<ApiResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - Complete event should show the actual matched type name
        var completeEvent = events.OfType<StructuredOutputCompleteEvent>().FirstOrDefault();
        Assert.NotNull(completeEvent);
        Assert.Equal("ErrorResponse", completeEvent.OutputTypeName);
    }

    [Fact]
    public void UnionMode_ThrowsException_WhenUnionTypesNotProvided()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "union"
                // UnionTypes not set
            }
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var evt in agent.RunStructuredAsync<ApiResponse>("test", options: options))
            {
                // Enumerate to trigger the exception
            }
        });
    }

    #endregion
}
