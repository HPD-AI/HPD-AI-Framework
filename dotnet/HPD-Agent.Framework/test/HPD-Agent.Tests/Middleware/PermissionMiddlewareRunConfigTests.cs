using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Agent.Tests.Middleware.V2;
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
}
