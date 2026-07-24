using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;
using FluentAssertions;
using HPD.Agent.Providers;
using HPD.Agent;
namespace HPD.Agent.Tests.Phase0_Characterization;

/// <summary>
/// Phase 0: Permission System Characterization Tests
///
/// These tests capture the CURRENT behavior of the permission system before refactoring.
/// They verify that permission requests, approvals, and denials work as expected.
/// </summary>
public class PermissionTests : AgentTestBase
{
    /// <summary>
    /// Helper to create agent with permission filtering enabled.
    /// </summary>
    private Agent CreateAgentWithPermissions(FakeChatClient client, params AIFunction[] tools)
    {
        var config = DefaultConfig();
        config.EnsureChatClientConfig();
        config.EnsureChatClientConfig().ProviderKey = "test";
        config.EnsureChatClientConfig().ModelName = "test-model";
        config.EnsureChatClientConfig().DefaultMicrosoftChatOptions ??= new ChatOptions();
        config.EnsureChatClientConfig().DefaultMicrosoftChatOptions.Tools = tools.Cast<AITool>().ToList();

        var builder = new AgentBuilder(config, new TestProviderRegistry(client));
        builder.WithPermissions(); // Enable permission filtering
        return builder.BuildAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Test 1: Permission denied blocks tool execution.
    /// Verifies that when permission is denied, the tool is not executed and agent receives denial message.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_PermissionDenied_BlocksToolExecution()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // LLM requests a tool that requires permission
        fakeLLM.EnqueueToolCall(
            functionName: "SensitiveTool",
            callId: "call_1",
            args: new Dictionary<string, object?> { ["action"] = "delete" });

        // LLM responds after permission denial
        fakeLLM.EnqueueTextResponse("I understand the permission was denied.");

        // Create a tool that requires permission using HPDAIFunctionFactory
        var options = new HPDAIFunctionFactoryOptions
        {
            Name = "SensitiveTool",
            Description = "A sensitive tool requiring permission",
            RequiresPermission = true
        };

        var sensitiveToolWithPermission = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "Executed: delete",
            options);

        var agent = CreateAgentWithPermissions(fakeLLM, sensitiveToolWithPermission);
        var messages = CreateSimpleConversation("Use the sensitive tool");

        // Set up mock permission handler to deny permission
        var eventStream = agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken);

        using var permissionHandler = new MockPermissionHandler(agent, eventStream)
            .AutoDenyAll("User denied permission");

        // Act - Wait for handler to complete (it consumes the event stream)
        await permissionHandler.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        // Get captured events from handler
        var capturedEvents = permissionHandler.CapturedEvents;

        // Assert - Permission request should be captured
        permissionHandler.CapturedRequests.Should().ContainSingle("permission should be requested once");

        var permissionRequest = permissionHandler.CapturedRequests[0];
        permissionRequest.FunctionName.Should().Be("SensitiveTool", "correct function should request permission");

        var toolResults = capturedEvents.OfType<ToolCallResultEvent>().ToList();
        toolResults.Should().ContainSingle(
            "a blocked function must still produce an explicit model-facing result");
        var result = toolResults[0].Result.Text
            ?? toolResults[0].Result.Json?.GetRawText()
            ?? string.Empty;
        result.Should().Contain("<tool_permission");
        result.Should().Contain("outcome=\"denied\"");
        result.Should().Contain("executed=\"false\"");
        result.Should().Contain("Denied by test");

        // Final response should exist
        var textDeltas = capturedEvents.OfType<TextDeltaEvent>().ToList();
        var finalText = string.Concat(textDeltas.Select(e => e.Text));
        finalText.Should().NotBeEmpty("agent should respond after permission denial");
    }

    /// <summary>
    /// Test 2: Permission approved allows tool execution.
    /// Verifies that when permission is approved, the tool executes successfully.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_PermissionApproved_AllowsToolExecution()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // LLM requests a tool that requires permission
        fakeLLM.EnqueueToolCall(
            functionName: "SensitiveTool",
            callId: "call_1",
            args: new Dictionary<string, object?> { ["action"] = "read" });

        // LLM responds after successful tool execution
        fakeLLM.EnqueueTextResponse("The tool executed successfully.");

        // Create a tool that requires permission
        var options = new HPDAIFunctionFactoryOptions
        {
            Name = "SensitiveTool",
            Description = "A sensitive tool requiring permission",
            RequiresPermission = true
        };

        var sensitiveToolWithPermission = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "Successfully read data",
            options);

        var agent = CreateAgentWithPermissions(fakeLLM, sensitiveToolWithPermission);
        var messages = CreateSimpleConversation("Use the sensitive tool to read data");

        // Set up mock permission handler to approve permission
        var eventStream = agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken);

        using var permissionHandler = new MockPermissionHandler(agent, eventStream)
            .AutoApproveAll();

        // Act - Wait for handler to complete (it consumes the event stream)
        await permissionHandler.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        // Get captured events from handler
        var capturedEvents = permissionHandler.CapturedEvents;

        // Assert - Permission request should be captured
        permissionHandler.CapturedRequests.Should().ContainSingle("permission should be requested once");

        // Tool SHOULD be executed successfully
        var toolResults = capturedEvents.OfType<ToolCallResultEvent>().ToList();
        toolResults.Should().ContainSingle("tool should execute after approval");
        toolResults[0].Result.Text.Should().Contain("Successfully", "tool should return successful result");

        // Final response should acknowledge success
        var textDeltas = capturedEvents.OfType<TextDeltaEvent>().ToList();
        var finalText = string.Concat(textDeltas.Select(e => e.Text));
        finalText.Should().Contain("successfully", "agent should acknowledge successful execution");
    }

    /// <summary>
    /// Test 3: Multiple permission requests handled sequentially.
    /// Verifies that multiple tools requiring permission are handled correctly.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_MultiplePermissions_HandledSequentially()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // LLM requests two tools requiring permission
        fakeLLM.EnqueueToolCall("Tool1", "call_1", new Dictionary<string, object?> { ["data"] = "a" });
        fakeLLM.EnqueueToolCall("Tool2", "call_2", new Dictionary<string, object?> { ["data"] = "b" });

        // Final response
        fakeLLM.EnqueueTextResponse("Both tools executed");

        // Create two tools requiring permission
        var tool1 = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "Tool1 result",
            new HPDAIFunctionFactoryOptions
            {
                Name = "Tool1",
                Description = "First sensitive tool",
                RequiresPermission = true
            });

        var tool2 = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "Tool2 result",
            new HPDAIFunctionFactoryOptions
            {
                Name = "Tool2",
                Description = "Second sensitive tool",
                RequiresPermission = true
            });

        var agent = CreateAgentWithPermissions(fakeLLM, tool1, tool2);
        var messages = CreateSimpleConversation("Use both tools");

        // Set up mock permission handler - approve first, deny second
        var eventStream = agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken);

        using var permissionHandler = new MockPermissionHandler(agent, eventStream);
        permissionHandler.EnqueueResponse(approved: true); // Approve Tool1
        permissionHandler.EnqueueResponse(approved: false, denialReason: "User denied Tool2"); // Deny Tool2

        // Act - Wait for handler to complete (it consumes the event stream)
        await permissionHandler.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        // Get captured events from handler
        var capturedEvents = permissionHandler.CapturedEvents;

        // Assert - Two permission requests
        permissionHandler.CapturedRequests.Should().HaveCount(2, "both tools should request permission");

        // Tool results should reflect approval/denial
        var toolResults = capturedEvents.OfType<ToolCallResultEvent>().ToList();
        toolResults.Should().HaveCountGreaterThanOrEqualTo(1, "at least approved tool should have result");
    }

    /// <summary>
    /// Test 4: Parallel function execution with batch permission checking.
    /// Verifies that BeforeParallelFunctionsAsync is called and permissions are checked once upfront.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_ParallelFunctions_BatchPermissionCheck()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // LLM requests multiple tools at once (parallel execution)
        fakeLLM.EnqueueToolCalls(
            ("ParallelTool1", "call_1", new Dictionary<string, object?> { ["data"] = "a" }),
            ("ParallelTool2", "call_2", new Dictionary<string, object?> { ["data"] = "b" }),
            ("ParallelTool3", "call_3", new Dictionary<string, object?> { ["data"] = "c" }));

        // Final response
        fakeLLM.EnqueueTextResponse("All tools executed in parallel");

        // Create three tools requiring permission
        var tool1 = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "ParallelTool1 result",
            new HPDAIFunctionFactoryOptions
            {
                Name = "ParallelTool1",
                Description = "First parallel tool",
                RequiresPermission = true
            });

        var tool2 = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "ParallelTool2 result",
            new HPDAIFunctionFactoryOptions
            {
                Name = "ParallelTool2",
                Description = "Second parallel tool",
                RequiresPermission = true
            });

        var tool3 = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "ParallelTool3 result",
            new HPDAIFunctionFactoryOptions
            {
                Name = "ParallelTool3",
                Description = "Third parallel tool",
                RequiresPermission = true
            });

        var agent = CreateAgentWithPermissions(fakeLLM, tool1, tool2, tool3);
        var messages = CreateSimpleConversation("Use all three tools in parallel");

        // Set up mock permission handler - approve all
        var eventStream = agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken);

        using var permissionHandler = new MockPermissionHandler(agent, eventStream)
            .AutoApproveAll();

        // Act - Wait for handler to complete (it consumes the event stream)
        await permissionHandler.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        // Get captured events from handler
        var capturedEvents = permissionHandler.CapturedEvents;

        // Assert - Three permission requests (one per tool in batch check)
        permissionHandler.CapturedRequests.Should().HaveCount(3,
            "BeforeParallelFunctionsAsync should check permission for each tool sequentially");

        // All tools should execute successfully
        var toolResults = capturedEvents.OfType<ToolCallResultEvent>().ToList();
        toolResults.Should().HaveCount(3, "all three tools should execute");

        toolResults.Should().Contain(t => t.Result.Text != null && t.Result.Text.Contains("ParallelTool1 result"), "tool1 should execute");
        toolResults.Should().Contain(t => t.Result.Text != null && t.Result.Text.Contains("ParallelTool2 result"), "tool2 should execute");
        toolResults.Should().Contain(t => t.Result.Text != null && t.Result.Text.Contains("ParallelTool3 result"), "tool3 should execute");
    }

    /// <summary>
    /// Test 5: Parallel function execution with mixed approvals/denials.
    /// Verifies that BeforeParallelFunctionsAsync correctly handles some approvals and some denials.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_ParallelFunctions_MixedApprovalsDenials()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // LLM requests multiple tools at once (parallel execution)
        fakeLLM.EnqueueToolCalls(
            ("ParallelTool1", "call_1", new Dictionary<string, object?> { ["data"] = "a" }),
            ("ParallelTool2", "call_2", new Dictionary<string, object?> { ["data"] = "b" }),
            ("ParallelTool3", "call_3", new Dictionary<string, object?> { ["data"] = "c" }));

        // Final response
        fakeLLM.EnqueueTextResponse("Some tools executed, some were denied");

        // Create three tools requiring permission
        var tool1 = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "ParallelTool1 result",
            new HPDAIFunctionFactoryOptions
            {
                Name = "ParallelTool1",
                Description = "First parallel tool",
                RequiresPermission = true
            });

        var tool2 = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "ParallelTool2 result",
            new HPDAIFunctionFactoryOptions
            {
                Name = "ParallelTool2",
                Description = "Second parallel tool",
                RequiresPermission = true
            });

        var tool3 = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "ParallelTool3 result",
            new HPDAIFunctionFactoryOptions
            {
                Name = "ParallelTool3",
                Description = "Third parallel tool",
                RequiresPermission = true
            });

        var agent = CreateAgentWithPermissions(fakeLLM, tool1, tool2, tool3);
        var messages = CreateSimpleConversation("Use all three tools in parallel");

        // Set up mock permission handler - approve some, deny others
        var eventStream = agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken);

        using var permissionHandler = new MockPermissionHandler(agent, eventStream);
        permissionHandler.EnqueueResponse(approved: true);  // Approve Tool1
        permissionHandler.EnqueueResponse(approved: false, denialReason: "User denied Tool2"); // Deny Tool2
        permissionHandler.EnqueueResponse(approved: true);  // Approve Tool3

        // Act - Wait for handler to complete (it consumes the event stream)
        await permissionHandler.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        // Get captured events from handler
        var capturedEvents = permissionHandler.CapturedEvents;

        // Assert - Three permission requests
        permissionHandler.CapturedRequests.Should().HaveCount(3,
            "BeforeParallelFunctionsAsync should check permission for each tool");

        // Tool results should reflect mixed outcomes
        var toolResults = capturedEvents.OfType<ToolCallResultEvent>().ToList();
        toolResults.Should().HaveCount(3, "all three tools should have results (approved execute, denied get denial message)");

        // Tool1 and Tool3 should have successful results
        toolResults.Should().Contain(t => t.Result.Text != null && t.Result.Text.Contains("ParallelTool1 result"), "tool1 should execute");
        toolResults.Should().Contain(t => t.Result.Text != null && t.Result.Text.Contains("ParallelTool3 result"), "tool3 should execute");

        // Tool2 should have denial message
        toolResults.Should().Contain(t => t.Result.Text != null && t.Result.Text.Contains("denied", StringComparison.OrdinalIgnoreCase),
            "tool2 should have denial message");
    }
}
