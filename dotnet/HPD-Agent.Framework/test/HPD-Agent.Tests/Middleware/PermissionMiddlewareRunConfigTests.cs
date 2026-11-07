using FluentAssertions;
using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Agent.Tests.Middleware.V2;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

public class PermissionMiddlewareRunConfigTests
{
    [Fact]
    public async Task BeforeFunction_AutoApproveStillRequiresDurableJournal()
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

        context.ResolvedFunction = middleware.ResolveFunction(function, null, context.RunConfig);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.BeforeFunctionAsync(context, CancellationToken.None));

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

        context.ResolvedFunction = middleware.ResolveFunction(function, null, context.RunConfig);
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

        context.ResolvedFunction = middleware.ResolveFunction(function, null, context.RunConfig);
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

        context.ResolvedFunction = middleware.ResolveFunction(function, null, context.RunConfig);
        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
    }

    [PermissionPolicyDefinition(ImmediatePermissionPolicy.Id)]
    internal sealed class ImmediatePermissionPolicy : IPermissionPolicy
    {
        internal const string Id = "test.immediate-policy";
        public ValueTask<PermissionPolicyResult> EvaluateAsync(ValidatedPermissionInput input,
            PermissionEvaluationContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PermissionPolicyResult { Outcome = new PermissionAllow() });
    }

    private static AIFunction CreateFunction(string name, bool requiresPermission) =>
        HPDAIFunctionFactory.Create((_, _, _) => Task.FromResult<object?>("ok"), new()
        {
            Name = name,
            FunctionPermission = requiresPermission ? AIFunctionPermissionDeclaration.Required(name) : null
        });
    private static BeforeFunctionContext CreateBeforeFunctionContext(AIFunction function, AgentRunConfig config) =>
        MiddlewareTestHelpers.CreateAgentContext().AsBeforeFunction(function, "call", new Dictionary<string, object?>(), config);
}
