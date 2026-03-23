using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;
using FluentAssertions;
using HPD.Agent;
namespace HPD.Agent.Tests.Phase0_Characterization;

/// <summary>
/// Phase 0: Container Expansion Characterization Tests
///
/// These tests capture the CURRENT behavior of container expansion before refactoring.
/// Container expansion is the mechanism where Toolkits are lazy-loaded via two-turn flow:
/// - Turn 1: Container function is visible and called
/// - Turn 2: Container is hidden, individual functions become visible
///
/// Note: These tests validate high-level behavior without deep introspection
/// into tool visibility at each iteration. Tool visibility is internal to the agent loop.
/// </summary>
public class ContainerExpansionTests : AgentTestBase
{
    /// <summary>
    /// Test 1: Basic container expansion flow.
    /// Verifies that container is called, then member functions become available.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_ContainerExpansion_TwoTurnFlow()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // Turn 1: LLM calls the container
        fakeLLM.EnqueueToolCall(
            functionName: "MathTools",
            callId: "call_container",
            args: new Dictionary<string, object?>());

        // Turn 2: LLM calls an individual function from the expanded Toolkit
        fakeLLM.EnqueueToolCall(
            functionName: "Add",
            callId: "call_add",
            args: new Dictionary<string, object?>());

        // Turn 3: LLM provides final response
        fakeLLM.EnqueueTextResponse("Math operation completed successfully");

        // Create Collapsed Toolkit with container and members
        var (container, members) = CollapsedToolkitTestHelper.CreateCollapsedToolkit(
            "MathTools",
            "Mathematical operations",
            CollapsedToolkitTestHelper.MemberFunc("Add", "Adds numbers", () => "Sum: 42"),
            CollapsedToolkitTestHelper.MemberFunc("Multiply", "Multiplies numbers", () => "Product: 100"));

        // Note: We need Collapsing enabled for container expansion to work
        var config = DefaultConfig();
        config.Collapsing = new CollapsingConfig
        {
            Enabled = true
        };
        config.Provider ??= new ProviderConfig();
        config.Provider.ProviderKey = "test";
        config.Provider.ModelName = "test-model";

        // Register both container and member functions
        var allFunctions = new List<AIFunction> { container };
        allFunctions.AddRange(members);

        var agent = CreateAgent(
            config: config,
            client: fakeLLM,
            tools: allFunctions.ToArray());

        var messages = CreateSimpleConversation("Use math operations");

        var capturedEvents = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            capturedEvents.Add(evt);
        }

        // Assert - CURRENT behavior
        // Should have multiple iterations (container call + member function call + final response)
        var agentTurnStarts = capturedEvents.OfType<AgentTurnStartedEvent>().ToList();
        agentTurnStarts.Should().HaveCountGreaterThanOrEqualTo(2, "should have at least 2 iterations for two-turn expansion");

        // Should have tool call events for both container and member
        var toolCalls = capturedEvents.OfType<ToolCallStartEvent>().ToList();
        toolCalls.Should().HaveCountGreaterThanOrEqualTo(2, "should call container and at least one member function");

        // Container should be called
        toolCalls.Should().Contain(e => e.Name == "MathTools", "container should be invoked");

        // Member function should be called
        toolCalls.Should().Contain(e => e.Name == "Add", "member function should be invoked after expansion");

        // Tool results should exist
        var toolResults = capturedEvents.OfType<ToolCallResultEvent>().ToList();
        toolResults.Should().HaveCountGreaterThanOrEqualTo(2, "should have results for both tool calls");

        // Final text response
        var textDeltas = capturedEvents.OfType<TextDeltaEvent>().ToList();
        var finalText = string.Concat(textDeltas.Select(e => e.Text));
        finalText.Should().NotBeEmpty("should have final text response");
    }

    /// <summary>
    /// Test 2: Multiple member function calls after expansion.
    /// Verifies that once expanded, multiple member functions can be called.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_ContainerExpansion_MultipleMembers()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // Turn 1: Expand container
        fakeLLM.EnqueueToolCall(
            functionName: "StringToolkit",
            callId: "call_container",
            args: new Dictionary<string, object?>());

        // Turn 2: Call first member
        fakeLLM.EnqueueToolCall(
            functionName: "Uppercase",
            callId: "call_upper",
            args: new Dictionary<string, object?>());

        // Turn 3: Call second member
        fakeLLM.EnqueueToolCall(
            functionName: "Lowercase",
            callId: "call_lower",
            args: new Dictionary<string, object?>());

        // Turn 4: Final response
        fakeLLM.EnqueueTextResponse("String operations completed");

        var (container, members) = CollapsedToolkitTestHelper.CreateCollapsedToolkit(
            "StringToolkit",
            "String operations",
            CollapsedToolkitTestHelper.MemberFunc("Uppercase", "Convert to uppercase", () => "HELLO"),
            CollapsedToolkitTestHelper.MemberFunc("Lowercase", "Convert to lowercase", () => "hello"));

        var config = DefaultConfig();
        config.Collapsing = new CollapsingConfig { Enabled = true };
        config.Provider ??= new ProviderConfig();
        config.Provider.ProviderKey = "test";
        config.Provider.ModelName = "test-model";

        var allFunctions = new List<AIFunction> { container };
        allFunctions.AddRange(members);

        var agent = CreateAgent(
            config: config,
            client: fakeLLM,
            tools: allFunctions.ToArray());

        var messages = CreateSimpleConversation("Transform text");

        var capturedEvents = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            capturedEvents.Add(evt);
        }

        // Assert - Multiple member calls after single expansion
        var toolCalls = capturedEvents.OfType<ToolCallStartEvent>().ToList();
        toolCalls.Should().HaveCountGreaterThanOrEqualTo(3, "container + 2 member functions");

        // Container called once
        toolCalls.Where(e => e.Name == "StringToolkit").Should().ContainSingle("container should be called once");

        // Both members called
        toolCalls.Should().Contain(e => e.Name == "Uppercase", "Uppercase member should be called");
        toolCalls.Should().Contain(e => e.Name == "Lowercase", "Lowercase member should be called");
    }

    /// <summary>
    /// Test 3: Non-Collapsed functions remain visible alongside containers.
    /// Verifies that regular (non-Collapsed) functions coexist with Collapsed Toolkits.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_ContainerExpansion_MixedCollapsedAndNonCollapsed()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // Call a non-Collapsed function (should work immediately)
        fakeLLM.EnqueueToolCall(
            functionName: "GetTime",
            callId: "call_time",
            args: new Dictionary<string, object?>());

        // Then expand a container
        fakeLLM.EnqueueToolCall(
            functionName: "UtilsToolkit",
            callId: "call_container",
            args: new Dictionary<string, object?>());

        // Then call a member from the expanded Toolkit
        fakeLLM.EnqueueToolCall(
            functionName: "Echo",
            callId: "call_echo",
            args: new Dictionary<string, object?>());

        // Final response
        fakeLLM.EnqueueTextResponse("Operations completed");

        // Create non-Collapsed function
        var getTime = CollapsedToolkitTestHelper.CreateSimpleFunction(
            "GetTime",
            "Gets current time",
            () => DateTime.UtcNow.ToString("O"));

        // Create Collapsed Toolkit
        var (container, members) = CollapsedToolkitTestHelper.CreateCollapsedToolkit(
            "UtilsToolkit",
            "Utility functions",
            CollapsedToolkitTestHelper.MemberFunc("Echo", "Echoes input", () => "Echo: test"));

        var config = DefaultConfig();
        config.Collapsing = new CollapsingConfig { Enabled = true };
        config.Provider ??= new ProviderConfig();
        config.Provider.ProviderKey = "test";
        config.Provider.ModelName = "test-model";

        var allFunctions = new List<AIFunction> { getTime, container };
        allFunctions.AddRange(members);

        var agent = CreateAgent(
            config: config,
            client: fakeLLM,
            tools: allFunctions.ToArray());

        var messages = CreateSimpleConversation("Use utilities");

        var capturedEvents = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            capturedEvents.Add(evt);
        }

        // Assert - All three tools should be called
        var toolCalls = capturedEvents.OfType<ToolCallStartEvent>().ToList();
        toolCalls.Should().HaveCountGreaterThanOrEqualTo(3, "non-Collapsed + container + member");

        toolCalls.Should().Contain(e => e.Name == "GetTime", "non-Collapsed function should be callable");
        toolCalls.Should().Contain(e => e.Name == "UtilsToolkit", "container should be callable");
        toolCalls.Should().Contain(e => e.Name == "Echo", "member should be callable after expansion");
    }
}
