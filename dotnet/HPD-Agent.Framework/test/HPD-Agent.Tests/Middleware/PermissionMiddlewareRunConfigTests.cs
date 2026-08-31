using FluentAssertions;
using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Agent.Tests.Middleware.V2;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using System.Text.Json;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

public class PermissionMiddlewareRunConfigTests
{
    [Fact]
    public async Task BeforeFunction_AutoApprove_BypassesRequiredPermission()
    {
        var middleware = new PermissionMiddleware();
        var function = CreateFunction("SensitiveTool", requiresPermission: true);
        var context = CreateBeforeFunctionContext(
            function,
            new AgentRunConfig
            {
                Security = new AgentSecurityRunConfig
                {
                    Approval = AgentApprovalPolicy.AutoApprove
                }
            });

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
    }

    [Fact]
    public async Task BeforeFunction_RunConfigFalseOverride_AllowsFunctionWithRequiresPermission()
    {
        var middleware = new PermissionMiddleware();
        var function = CreateFunction("SensitiveTool", requiresPermission: true);
        var context = CreateBeforeFunctionContext(
            function,
            new AgentRunConfig
            {
                Security = new AgentSecurityRunConfig { PermissionOverrides =
                    [new(new("SensitiveTool"), RequiresPermission: false)] }
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
                Security = new AgentSecurityRunConfig { PermissionOverrides =
                    [new(new("NormallyRequiredByBuilder"), RequiresPermission: false)] }
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
                Security = new AgentSecurityRunConfig { PermissionOverrides =
                    [new(new("MissingTool"), RequiresPermission: true)] }
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
        var interruptions = new List<InterruptionHandledEvent>();
        using var interruptionSubscription = coordinator.Subscribe<InterruptionHandledEvent>(handled =>
        {
            interruptions.Add(handled);
            return ValueTask.CompletedTask;
        });
        using var subscription = coordinator.Subscribe<PermissionRequestEvent>(request =>
        {
            coordinator.Respond(new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                ChoiceId: "deny_once",
                Feedback: "Use the read-only status tool instead."));
            return ValueTask.CompletedTask;
        });
        var agentContext = CreateAgentContext(coordinator);
        var context = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            new AgentRunConfig());

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeTrue();
        var result = context.OverrideResult.Should().BeOfType<string>().Subject;
        result.Should().Contain("<tool_permission");
        result.Should().Contain("outcome=\"denied\"");
        result.Should().Contain("executed=\"false\"");
        result.Should().Contain("Use the read-only status tool instead.");
        interruptions.Should().BeEmpty();
    }

    [Fact]
    public async Task BeforeFunction_UnknownCompoundAction_ReturnsValidationRejection()
    {
        var definition = new ClientToolDefinition
        {
            Name = "penpot",
            Description = "Penpot operations.",
            ParametersSchema = JsonDocument.Parse(
                """
                {
                  "type": "object",
                  "oneOf": [{
                    "type": "object",
                    "properties": { "action": { "const": "inspect" } },
                    "required": ["action"],
                    "additionalProperties": false
                  }]
                }
                """).RootElement.Clone(),
            OperationContract = new ClientToolOperationContract
            {
                Discriminator = "action",
                Actions = new Dictionary<string, ClientToolPolicy>
                {
                    ["inspect"] = new()
                }
            }
        };
        var function = HPDAIFunctionFactory.Create(
            async (_, _, _) => "ok",
            new HPDAIFunctionFactoryOptions
            {
                Name = "penpot_design_penpot",
                Description = "Penpot operations.",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["ClientToolDefinition"] = definition
                }
            });
        var context = MiddlewareTestHelpers.CreateAgentContext().AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?> { ["action"] = "unknown" },
            new AgentRunConfig());

        await new PermissionMiddleware().BeforeFunctionAsync(
            context,
            CancellationToken.None);

        context.BlockExecution.Should().BeTrue();
        context.OverrideResult.Should().BeOfType<string>().Which.Should()
            .Contain("Client tool request rejected: Unknown compound tool action 'unknown'.");
    }

    private static AIFunction CreateFunction(string name, bool requiresPermission)
        => HPDAIFunctionFactory.Create(
            async (_, _, _) => "ok",
            new HPDAIFunctionFactoryOptions
            {
                Name = name,
                Description = $"{name} test function",
                FunctionPermission = requiresPermission
                    ? AIFunctionPermissionDeclaration.Required(name)
                    : null
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

    private static AgentContext CreateAgentContext(EventCoordinator coordinator)
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
            new HPD.Agent.Thread("test-session", "test-agent") { Id = "test-thread" },
            CancellationToken.None);
    }
}
