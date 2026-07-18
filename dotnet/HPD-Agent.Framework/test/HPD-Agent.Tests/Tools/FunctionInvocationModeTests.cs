using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Tools;

public sealed class FunctionInvocationModeTests
{
    [Fact]
    public void Create_ModelChoiceFunction_AddsInvocationModeToSchema()
    {
        var function = CreateFunction(
            AgentInvocationModePolicy.ModelChoice,
            (_, _, _) => Task.FromResult<object?>("ok"));

        function.JsonSchema.GetProperty("properties")
            .TryGetProperty("invocationMode", out var invocationMode)
            .Should().BeTrue();
        invocationMode.GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal("synchronous", "background");
    }

    [Fact]
    public async Task InvokeAsync_Synchronous_StripsInvocationModeBeforeCallingFunction()
    {
        JsonElement observedJson = default;
        var function = CreateFunction(
            AgentInvocationModePolicy.ModelChoice,
            (args, _, _) =>
            {
                observedJson = args.GetJson().Clone();
                return Task.FromResult<object?>("done");
            });
        var context = CreateFunctionContext(function, new CapturingBackgroundTaskRegistry());

        var result = await ((HPDAIFunctionFactory.HPDAIFunction)function).InvokeAsync(
            CreateArguments("""{"input":"hello","invocationMode":"synchronous"}"""),
            context,
            CancellationToken.None);

        result.Should().Be("done");
        observedJson.TryGetProperty("input", out _).Should().BeTrue();
        observedJson.TryGetProperty("invocationMode", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Background_RegistersFunctionTask()
    {
        var registry = new CapturingBackgroundTaskRegistry();
        var function = CreateFunction(
            AgentInvocationModePolicy.ModelChoice,
            (_, _, _) => Task.FromResult<object?>("background done"));
        var context = CreateFunctionContext(function, registry);

        var result = await ((HPDAIFunctionFactory.HPDAIFunction)function).InvokeAsync(
            CreateArguments("""{"input":"hello","invocationMode":"background"}"""),
            context,
            CancellationToken.None);

        result.Should().BeOfType<ToolResultPayload>();
        registry.Descriptor.Should().NotBeNull();
        registry.Descriptor!.SourceKind.Should().Be(BackgroundTaskSourceKind.Function);
        registry.Descriptor.Metadata.Should().ContainKey("function.name").WhoseValue.Should().Be("long_task");

        var backgroundContext = new BackgroundTaskContext
        {
            TaskId = "task-1",
            Descriptor = registry.Descriptor
        };
        await registry.TaskFactory!(backgroundContext, CancellationToken.None);

        backgroundContext.Completion.Should().NotBeNull();
        backgroundContext.Completion!.Summary.Should().Be("background done");
        backgroundContext.Completion.Metadata.Should().ContainKey("function.name").WhoseValue.Should().Be("long_task");
    }

    private static AIFunction CreateFunction(
        AgentInvocationModePolicy policy,
        Func<AIFunctionArguments, FunctionExecutionContext, CancellationToken, Task<object?>> invoke)
        => HPDAIFunctionFactory.Create(
            invoke,
            new HPDAIFunctionFactoryOptions
            {
                Name = "long_task",
                Description = "Runs a long task.",
                InvocationModePolicy = policy,
                ResultType = typeof(string),
                SchemaProvider = () =>
                {
                    using var document = JsonDocument.Parse(
                        """{"type":"object","properties":{"input":{"type":"string"}},"required":["input"],"additionalProperties":false}""");
                    return document.RootElement.Clone();
                },
                Validator = (json, options) =>
                {
                    return json.TryGetProperty("input", out var _)
                        ? []
                        : [new ValidationError { Property = "input", ErrorMessage = "input is required", ErrorCode = "missing_required_property" }];
                }
            });

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
