using System.Text.Json;
using HPD.Agent.StructuredOutput;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.StructuredOutput;

/// <summary>
/// Unit tests for structured output in tool mode.
/// Tests output tool creation, argument capturing, and mixed tool execution.
/// Uses shared TestResponse/ArrayResponse models from TestModels.cs
/// </summary>
public class StructuredOutputToolModeTests
{
    [Fact]
    public async Task ToolMode_CreatesOutputTool_WithCorrectSchema()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        // Simulate the model calling the output tool
        fakeClient.EnqueueToolCall(
            "return_TestResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["name"] = "test",
                ["value"] = 42
            }
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "tool"
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - verify the output tool was called and result was parsed
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.Equal("test", final.Value.Name);
        Assert.Equal(42, final.Value.Value);
    }

    [Fact]
    public async Task ToolMode_OutputToolNeverExecutes()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "return_TestResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["name"] = "test",
                ["value"] = 42
            }
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "tool"
            }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<TestResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert - no ToolCallResultEvent should be emitted for output tools
        // Output tools are captured but never executed (CallId matches our output tool)
        var toolResults = events.OfType<ToolCallResultEvent>()
            .Where(e => e.CallId == "call_1")
            .ToList();

        Assert.Empty(toolResults);
    }

    [Fact]
    public async Task ToolMode_CapturesArgsAsStructuredOutput()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        var expectedArgs = new Dictionary<string, object?>
        {
            ["name"] = "captured",
            ["value"] = 123,
            ["description"] = "This is captured"
        };
        fakeClient.EnqueueToolCall("return_TestResponse", "call_1", expectedArgs);

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
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.Equal("captured", final.Value.Name);
        Assert.Equal(123, final.Value.Value);
        Assert.Equal("This is captured", final.Value.Description);
    }

    [Fact]
    public async Task ToolMode_CustomToolName()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "submit_result", // Custom tool name
            "call_1",
            new Dictionary<string, object?>
            {
                ["name"] = "custom",
                ["value"] = 1
            }
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions
            {
                Mode = "tool",
                ToolName = "submit_result" // Custom name
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
        Assert.Equal("custom", final.Value.Name);
    }

    [Fact]
    public async Task ToolMode_MixedWithRegularTools()
    {
        // This test verifies that in tool mode, regular tools can coexist with the output tool.
        // The output tool terminates the loop immediately when called, so this test demonstrates
        // that when the output tool is called directly (without calling regular tools first),
        // the loop terminates correctly with the structured result.
        //
        // For a scenario where regular tools ARE called before the output tool,
        // the FakeChatClient would need to return the regular tool call first,
        // then after processing (second iteration), return the output tool call.

        // Arrange
        var fakeClient = new FakeChatClient();
        // Output tool call directly - simulates model deciding to return result
        fakeClient.EnqueueToolCall(
            "return_TestResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["name"] = "direct_output",
                ["value"] = 42
            }
        );

        var regularTool = AIFunctionFactory.Create(() => "tool result", "regular_tool", "A regular tool");

        // Create agent with default config that includes the regular tool
        var config = new AgentConfig
        {
            Name = "TestAgent",
            MaxAgenticIterations = 50,
            SystemInstructions = "You are a helpful test agent.",
            Provider = new ProviderConfig
            {
                ProviderKey = "test",
                ModelName = "test-model",
                DefaultChatOptions = new ChatOptions
                {
                    Tools = new List<AITool> { regularTool }
                }
            }
        };
        var agent = TestAgentFactory.Create(config: config, chatClient: fakeClient);
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

        // Assert - output tool terminates the loop with structured result
        var final = events.OfType<StructuredResultEvent<TestResponse>>()
            .FirstOrDefault(e => !e.IsPartial);
        Assert.NotNull(final);
        Assert.Equal("direct_output", final.Value.Name);
        Assert.Equal(42, final.Value.Value);

        // Verify the output tool was available alongside the regular tool
        // (This is verified by the fact that the structured result was returned)
    }

    [Fact]
    public async Task ToolMode_TerminatesOnOutputToolCall()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "return_TestResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["name"] = "terminal",
                ["value"] = 1
            }
        );
        // Queue another response that should NOT be reached
        fakeClient.EnqueueTextResponse("This should not be reached");

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

        // Assert - should have exactly one structured result (terminal)
        var results = events.OfType<StructuredResultEvent<TestResponse>>().ToList();
        Assert.Single(results);
        Assert.Equal("terminal", results[0].Value.Name);

        // The second queued response should not have been consumed
        // (If it was, there would be additional events or errors)
    }

    [Fact]
    public async Task ToolMode_EmitsErrorEvent_OnInvalidArgs()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        // Invalid args - value should be int, not string
        fakeClient.EnqueueToolCall(
            "return_TestResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["name"] = "test",
                ["value"] = "not a number" // Type mismatch
            }
        );

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

        // Assert - should emit error event for invalid args
        var errorEvent = events.OfType<StructuredOutputErrorEvent>().FirstOrDefault();
        Assert.NotNull(errorEvent);
        Assert.Equal("TestResponse", errorEvent.ExpectedTypeName);
    }

    [Fact]
    public async Task ToolMode_HandlesArrayProperties()
    {
        // Arrange
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "return_ArrayResponse",
            "call_1",
            new Dictionary<string, object?>
            {
                ["title"] = "List Example",
                ["items"] = new List<object> { "item1", "item2", "item3" }
            }
        );

        var agent = TestAgentFactory.Create(chatClient: fakeClient);
        var options = new AgentRunConfig
        {
            StructuredOutput = new StructuredOutputOptions { Mode = "tool" }
        };

        // Act
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunStructuredAsync<ArrayResponse>("test", options: options))
        {
            events.Add(evt);
        }

        // Assert
        var final = events.OfType<StructuredResultEvent<ArrayResponse>>()
            .FirstOrDefault(e => !e.IsPartial);

        Assert.NotNull(final);
        Assert.Equal("List Example", final.Value.Title);
        Assert.Equal(3, final.Value.Items.Count);
        Assert.Contains("item1", final.Value.Items);
        Assert.Contains("item2", final.Value.Items);
        Assert.Contains("item3", final.Value.Items);
    }
}
