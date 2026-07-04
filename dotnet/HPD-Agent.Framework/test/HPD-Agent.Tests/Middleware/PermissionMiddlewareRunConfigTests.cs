using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Agent.Tests.Middleware.V2;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

public class PermissionMiddlewareRunConfigTests
{
    [Fact]
    public async Task BeforeFunction_RunConfigFalseOverride_AllowsFunctionWithRequiresPermission()
    {
        var middleware = new PermissionMiddleware();
        var function = CreateFunction("SensitiveTool", requiresPermission: true);
        var context = CreateBeforeFunctionContext(
            function,
            new AgentRunConfig
            {
                PermissionOverrides = new Dictionary<string, bool>
                {
                    ["SensitiveTool"] = false
                }
            });

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
    }

    [Fact]
    public async Task BeforeFunction_RunConfigOverride_WinsOverBuilderOverride()
    {
        var registry = new PermissionOverrideRegistry();
        registry.RequirePermission("NormallyRequiredByBuilder");
        var middleware = new PermissionMiddleware(overrideRegistry: registry);
        var function = CreateFunction("NormallyRequiredByBuilder", requiresPermission: false);
        var context = CreateBeforeFunctionContext(
            function,
            new AgentRunConfig
            {
                PermissionOverrides = new Dictionary<string, bool>
                {
                    ["NormallyRequiredByBuilder"] = false
                }
            });

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
    }

    [Fact]
    public async Task BeforeFunction_RunConfigUnknownFunctionOverride_IsIgnored()
    {
        var middleware = new PermissionMiddleware();
        var function = CreateFunction("ActualTool", requiresPermission: false);
        var context = CreateBeforeFunctionContext(
            function,
            new AgentRunConfig
            {
                PermissionOverrides = new Dictionary<string, bool>
                {
                    ["MissingTool"] = true
                }
            });

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
    }

    [Fact]
    public async Task BeforeFunction_ReturnToModelDenial_DoesNotInterruptTurn()
    {
        var middleware = new PermissionMiddleware();
        var function = CreateFunction("SensitiveTool", requiresPermission: true);
        var coordinator = new EventCoordinator();
        var interruptions = new List<InterruptionRequestEvent>();
        using var subscription = coordinator.Subscribe<PermissionRequestEvent>(request =>
        {
            coordinator.Respond(new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                Approved: false,
                Reason: "Use the read-only status tool instead.",
                Choice: PermissionChoice.Ask,
                DeniedBehavior: PermissionDeniedBehavior.ReturnToModel));
            return ValueTask.CompletedTask;
        });
        var agentContext = CreateAgentContext(coordinator, interruptions);
        var context = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            new AgentRunConfig());

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeTrue();
        context.OverrideResult.Should().Be("Use the read-only status tool instead.");
        interruptions.Should().BeEmpty();
    }

    private static AIFunction CreateFunction(string name, bool requiresPermission)
        => HPDAIFunctionFactory.Create(
            async (_, _, _) => "ok",
            new HPDAIFunctionFactoryOptions
            {
                Name = name,
                Description = $"{name} test function",
                RequiresPermission = requiresPermission
            });

    private static BeforeFunctionContext CreateBeforeFunctionContext(
        AIFunction function,
        AgentRunConfig runConfig)
    {
        var agentContext = MiddlewareTestHelpers.CreateAgentContext();
        return agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            runConfig);
    }

    private static AgentContext CreateAgentContext(
        EventCoordinator coordinator,
        List<InterruptionRequestEvent> interruptions)
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            "test-conv",
            "TestAgent");

        return new AgentContext(
            "TestAgent",
            "test-conv",
            state,
            coordinator,
            new HPD.Agent.Session("test-session"),
            new HPD.Agent.Thread("test-session") { Id = "test-thread" },
            CancellationToken.None,
            interruptionHandler: (interruption, _) =>
            {
                interruptions.Add(interruption);
                return ValueTask.CompletedTask;
            });
    }
}
