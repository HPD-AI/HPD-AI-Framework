using System.Text.Json;
using FluentAssertions;
using HPD.Agent.MCP;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpToolInvocationRuntimeTests
{
    [Fact]
    public void CreateMcpToolSchema_ModelChoice_AddsInvocationModeAndPreservesOriginalSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string" }
              },
              "required": [ "path" ],
              "additionalProperties": false
            }
            """);

        var schema = MCPClientManager.CreateMcpToolSchema(
            document.RootElement,
            AgentInvocationModePolicy.ModelChoice);

        schema.GetProperty("properties").TryGetProperty("path", out _).Should().BeTrue();
        var invocationMode = schema.GetProperty("properties").GetProperty("invocationMode");
        invocationMode.GetProperty("type").GetString().Should().Be("string");
        invocationMode.GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal("synchronous", "background");
        schema.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal("path");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void CreateMcpToolSchema_SynchronousOnly_ReturnsOriginalSchema()
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""");

        var schema = MCPClientManager.CreateMcpToolSchema(
            document.RootElement,
            AgentInvocationModePolicy.SynchronousOnly);

        schema.GetRawText().Should().Be(document.RootElement.GetRawText());
    }

    [Fact]
    public void ResolveInvocationModePolicy_ToolSpecificModelChoice_CanDriveSchemaAugmentation()
    {
        var config = CreateServerConfig(AgentInvocationModePolicy.SynchronousOnly);
        config.ToolInvocationModePolicies["slow_search"] = AgentInvocationModePolicy.ModelChoice;
        using var document = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""");

        var policy = McpToolInvocationRuntime.ResolveInvocationModePolicy(config, "slow_search");
        var schema = MCPClientManager.CreateMcpToolSchema(document.RootElement, policy);

        schema.GetProperty("properties").TryGetProperty("invocationMode", out _).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Synchronous_StripsInvocationModeBeforeCallingTool()
    {
        var arguments = CreateArguments("""{"path":"/tmp/file.txt","invocationMode":"synchronous"}""");
        JsonElement observedJson = default;

        var result = await McpToolInvocationRuntime.InvokeAsync(
            new McpToolInvocationRuntime.McpToolInvocationRequest
            {
                ServerConfig = CreateServerConfig(AgentInvocationModePolicy.ModelChoice),
                ToolName = "read_file",
                Arguments = arguments,
                ParentContext = null,
                InvokeToolAsync = (args, _, _) =>
                {
                    observedJson = args.GetJson().Clone();
                    return Task.FromResult<object?>("file contents");
                }
            },
            CancellationToken.None);

        result.Mode.Should().Be(AgentInvocationMode.Synchronous);
        result.Text.Should().Be("file contents");
        result.ToToolResult().Should().Be("file contents");
        observedJson.TryGetProperty("path", out _).Should().BeTrue();
        observedJson.TryGetProperty("invocationMode", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_BackgroundOnly_WithoutRuntime_ReturnsUnavailableReceipt()
    {
        var result = await McpToolInvocationRuntime.InvokeAsync(
            new McpToolInvocationRuntime.McpToolInvocationRequest
            {
                ServerConfig = CreateServerConfig(AgentInvocationModePolicy.BackgroundOnly),
                ToolName = "read_file",
                Arguments = CreateArguments("""{"path":"/tmp/file.txt"}"""),
                ParentContext = null,
                InvokeToolAsync = (_, _, _) => Task.FromResult<object?>("unused")
            },
            CancellationToken.None);

        result.Mode.Should().Be(AgentInvocationMode.Background);
        result.Background.Should().NotBeNull();
        result.Background!.Status.Should().Be("background_unavailable");
        result.Background.SourceKind.Should().Be(BackgroundTaskSourceKind.McpTool);
        result.Background.Name.Should().Be("read_file");
    }

    [Fact]
    public async Task InvokeAsync_ToolSpecificPolicy_OverridesServerPolicy()
    {
        var config = CreateServerConfig(AgentInvocationModePolicy.SynchronousOnly);
        config.ToolInvocationModePolicies["slow_search"] = AgentInvocationModePolicy.BackgroundOnly;

        var result = await McpToolInvocationRuntime.InvokeAsync(
            new McpToolInvocationRuntime.McpToolInvocationRequest
            {
                ServerConfig = config,
                ToolName = "slow_search",
                Arguments = CreateArguments("""{"query":"hello"}"""),
                ParentContext = null,
                InvokeToolAsync = (_, _, _) => Task.FromResult<object?>("unused")
            },
            CancellationToken.None);

        result.Mode.Should().Be(AgentInvocationMode.Background);
        result.Background!.Status.Should().Be("background_unavailable");
    }

    [Fact]
    public void ResolveInvocationModePolicy_UnknownTool_UsesServerPolicy()
    {
        var config = CreateServerConfig(AgentInvocationModePolicy.ModelChoice);
        config.ToolInvocationModePolicies["slow_search"] = AgentInvocationModePolicy.BackgroundOnly;

        var policy = McpToolInvocationRuntime.ResolveInvocationModePolicy(config, "read_file");

        policy.Should().Be(AgentInvocationModePolicy.ModelChoice);
    }

    [Fact]
    public async Task InvokeAsync_Background_RegistersTaskAndSetsCompletionMetadata()
    {
        var registry = new CapturingBackgroundTaskRegistry();
        var function = AIFunctionFactory.Create(() => "ok", "read_file", "Reads a file.");
        var context = CreateFunctionContext(function, registry);

        var result = await McpToolInvocationRuntime.InvokeAsync(
            new McpToolInvocationRuntime.McpToolInvocationRequest
            {
                ServerConfig = CreateServerConfig(AgentInvocationModePolicy.BackgroundOnly),
                ToolName = "read_file",
                Arguments = CreateArguments("""{"path":"/tmp/file.txt"}"""),
                ParentContext = context,
                InvokeToolAsync = (_, _, _) => Task.FromResult<object?>(
                    new global::HPD.Agent.ClientTools.TextContent("done reading"))
            },
            CancellationToken.None);

        result.Background!.TaskId.Should().Be("task-1");
        registry.Descriptor.Should().NotBeNull();
        registry.Descriptor!.SourceKind.Should().Be(BackgroundTaskSourceKind.McpTool);
        registry.Descriptor.Metadata.Should().ContainKey("mcp.serverName").WhoseValue.Should().Be("filesystem");
        registry.Descriptor.Metadata.Should().ContainKey("mcp.toolName").WhoseValue.Should().Be("read_file");

        var backgroundContext = new BackgroundTaskContext
        {
            TaskId = "task-1",
            Descriptor = registry.Descriptor
        };
        await registry.TaskFactory!(backgroundContext, CancellationToken.None);

        backgroundContext.Completion.Should().NotBeNull();
        backgroundContext.Completion!.Summary.Should().Be("done reading");
        backgroundContext.Completion.Metadata.Should().ContainKey("mcp.serverName").WhoseValue.Should().Be("filesystem");
        backgroundContext.Completion.Metadata.Should().ContainKey("mcp.toolName").WhoseValue.Should().Be("read_file");
    }

    private static MCPServerConfig CreateServerConfig(AgentInvocationModePolicy policy) => new()
    {
        Name = "filesystem",
        Transport = "stdio",
        Command = "npx",
        InvocationModePolicy = policy
    };

    private static AIFunctionArguments CreateArguments(string json)
    {
        var arguments = new AIFunctionArguments();
        using var document = JsonDocument.Parse(json);
        arguments.SetJson(document.RootElement.Clone());
        return arguments;
    }

    private static FunctionExecutionContext CreateFunctionContext(
        AIFunction function,
        IAgentBackgroundTaskRegistry backgroundTasks)
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new global::HPD.Agent.Session("session-1");
        var thread = new global::HPD.Agent.Thread("session-1", "test-agent") { Id = "thread-1" };
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            new EventCoordinator(),
            session,
            thread,
            CancellationToken.None);
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            toolharnessName: null,
            skillName: null);

        return new FunctionExecutionContext(
            beforeContext,
            new FunctionRequest
            {
                Function = function,
                CallId = "call-1",
                Arguments = new Dictionary<string, object?>(),
                State = state,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = agentContext.EventCoordinator,
                BackgroundTasks = backgroundTasks
            });
    }

    private sealed class CapturingBackgroundTaskRegistry : IAgentBackgroundTaskRegistry
    {
        public BackgroundTaskDescriptor? Descriptor { get; private set; }

        public Func<BackgroundTaskContext, CancellationToken, Task>? TaskFactory { get; private set; }

        public BackgroundTaskRegistration RegisterBackgroundTask(
            BackgroundTaskDescriptor descriptor,
            Func<BackgroundTaskContext, CancellationToken, Task> taskFactory)
        {
            Descriptor = descriptor;
            TaskFactory = taskFactory;
            return new BackgroundTaskRegistration("task-1", descriptor.Name, descriptor.SourceKind);
        }
    }
}
