using System.Text.Json.Serialization;
using System.Reflection;
using System.Xml.Linq;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugModelFacingTests
{
    [Fact]
    public void EveryPublicAction_HasAPermissionClassification()
    {
        var actions = typeof(DebugOperation)
            .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false)
            .Cast<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.TypeDiscriminator)
            .OfType<string>()
            .ToArray();

        actions.Should().HaveCount(49).And.OnlyHaveUniqueItems();
        actions.Select(DebugPermissionMiddleware.Classify)
            .Should().HaveCount(49);
    }

    [Theory]
    [InlineData("getVariables", DebugPermissionClass.Inspection)]
    [InlineData("continue", DebugPermissionClass.ExecutionControl)]
    [InlineData("launch", DebugPermissionClass.Launch)]
    [InlineData("attach", DebugPermissionClass.Attach)]
    [InlineData("evaluate", DebugPermissionClass.Evaluation)]
    [InlineData("setVariable", DebugPermissionClass.StateMutation)]
    [InlineData("writeMemory", DebugPermissionClass.MemoryWrite)]
    public void PermissionClassification_DistinguishesRiskFamilies(
        string action,
        DebugPermissionClass expected)
        => DebugPermissionMiddleware.Classify(action).Should().Be(expected);

    [Fact]
    public async Task PermissionDecision_IsInvocationLocalAndRejectsActionOrCallMismatch()
    {
        await using var manager = new DebugSessionManager();
        var authorization = new DebugPermissionAuthorizationService();
        var context = CreateContext(manager, "call-1", "writeMemory");

        authorization.DemandApproved(context, "writeMemory").PermissionClass
            .Should().Be(DebugPermissionClass.MemoryWrite);
        var wrongAction = () => authorization.DemandApproved(context, "evaluate");
        wrongAction.Should().Throw<UnauthorizedAccessException>();

        var wrongCall = CreateContext(manager, "call-2", "writeMemory",
            decisionCallId: "call-1");
        var wrongInvocation = () =>
            authorization.DemandApproved(wrongCall, "writeMemory");
        wrongInvocation.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task PermissionMiddleware_TransfersBoundActionThroughMiddlewareState()
    {
        await using var manager = new DebugSessionManager();
        var function = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions { Name = "Debug", Description = "test" });
        var initial = AgentLoopState.InitialSafe(
            [], "run-1", "conversation-1", "DebugTests");
        var session = new Session("session-1");
        var thread = new Thread("session-1", "debug-tests") { Id = "thread-1" };
        using var coordinator = new EventCoordinator();
        var agent = new AgentContext(
            "DebugTests", "conversation-1", initial, coordinator, session, thread,
            CancellationToken.None);
        agent.RuntimeCapabilities.Set<IDebugSessionManager>(manager);
        agent.RuntimeCapabilities.Set(new DebugRuntimeBindingState());
        var operation = new WriteDebugMemoryOperation(
            "tree-1", MemoryToken: "memory-1", Base64Data: "AA==");
        var arguments = new Dictionary<string, object?> { ["request"] = operation };
        var before = agent.AsBeforeFunction(
            function, "call-state", arguments, new AgentRunConfig(),
            nameof(CodingToolHarness), null);

        await new DebugPermissionMiddleware().BeforeFunctionAsync(
            before, CancellationToken.None);

        var execution = new FunctionExecutionContext(before, new FunctionRequest
        {
            Function = function,
            CallId = "call-state",
            Arguments = arguments,
            State = agent.State,
            RunConfig = new AgentRunConfig(),
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = coordinator
        });
        var decision = new DebugPermissionAuthorizationService()
            .DemandApproved(execution, "writeMemory");

        decision.FunctionCallId.Should().Be("call-state");
        decision.PermissionClass.Should().Be(DebugPermissionClass.MemoryWrite);
        initial.MiddlewareState.GetState<DebugPermissionStateData>(
            typeof(DebugPermissionStateData).FullName!).Should().BeNull(
                "the decision belongs to the middleware-produced state, not a singleton");

        await new DebugPermissionMiddleware().BeforeIterationAsync(
            agent.AsBeforeIteration(
                1, [], new ChatOptions(), new AgentRunConfig()),
            CancellationToken.None);
        agent.State.MiddlewareState.GetState<DebugPermissionStateData>(
            typeof(DebugPermissionStateData).FullName!)!
            .DecisionsByCallId.Should().BeEmpty(
                "invocation authority must not survive an iteration boundary");
    }

    [Fact]
    public void PermissionState_PreservesParallelCallIsolationImmutably()
    {
        var empty = new DebugPermissionStateData();
        var launch = empty.WithDecision(
            "call-launch", "launch", DebugPermissionClass.Launch);
        var combined = launch.WithDecision(
            "call-inspect", "inspectStop", DebugPermissionClass.Inspection);

        empty.DecisionsByCallId.Should().BeEmpty();
        launch.DecisionsByCallId.Keys.Should().Equal("call-launch");
        combined.DecisionsByCallId.Keys.Should()
            .BeEquivalentTo(["call-launch", "call-inspect"]);
        combined.DecisionsByCallId["call-launch"].PermissionClass
            .Should().Be(DebugPermissionClass.Launch);
        combined.DecisionsByCallId["call-inspect"].PermissionClass
            .Should().Be(DebugPermissionClass.Inspection);
    }

    [Fact]
    public void GenericPermissionPersistence_IsScopedToTheConcreteDebugAction()
    {
        HPD.Agent.Permissions.IActionScopedPermission inspect =
            new GetVariablesOperation("tree-1", VariablesToken: "variables-1");
        HPD.Agent.Permissions.IActionScopedPermission mutation =
            new WriteDebugMemoryOperation("tree-1", MemoryToken: "memory-1", Base64Data: "AA==");

        inspect.PermissionScope.Should().Be("getVariables");
        mutation.PermissionScope.Should().Be("writeMemory");
        inspect.PermissionScope.Should().NotBe(mutation.PermissionScope);
    }

    [Fact]
    public void ResultFormatter_EscapesUntrustedTextAndProducesWellFormedXml()
    {
        var formatter = new DebugResultFormatter();
        var xml = formatter.Success(
            "evaluate",
            [KeyValuePair.Create<string, object?>("result", "<value>&\"")],
            ["adapter </item><secret>"]);

        var document = XDocument.Parse(xml);
        document.Root!.Name.LocalName.Should().Be("debug");
        document.Root.Attribute("result")!.Value.Should().Be("<value>&\"");
        document.Descendants("secret").Should().BeEmpty();
        document.Descendants("item").Single().Value.Should().Be("adapter </item><secret>");
    }

    [Fact]
    public void DebuggingRegistration_IncludesTheModelFacingBoundary()
    {
        var services = new ServiceCollection();

        services.AddHPDCodingDebugging();

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(DebugOperationDispatcher));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(DebugPermissionAuthorizationService));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(DebugPermissionMiddleware));
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IDebugEventPublisher) ||
            descriptor.ServiceType == typeof(IDebugLifecycleEventPublisher),
            "debug event publication must bind to the active function runtime, not application DI");
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType.Name.Contains("DecisionStore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dispatcher_UsesTheRuntimeBoundManagerAndPublishesTypedMetadata()
    {
        await using var manager = new DebugSessionManager();
        var formatter = new DebugResultFormatter();
        var starts = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory());
        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(starts),
            formatter,
            new DebugPermissionAuthorizationService());
        var context = CreateContext(manager, "call-list", "listSessions");

        var xml = await dispatcher.ExecuteAsync(
            new ListDebugSessionsOperation(),
            context,
            CancellationToken.None);

        var root = XDocument.Parse(xml).Root!;
        root.Name.LocalName.Should().Be("debug");
        root.Attribute("action")!.Value.Should().Be("listSessions");
        root.Attribute("count")!.Value.Should().Be("0");
        context.ResultMetadata.TryGet<DebugOperationMetadata>(
            CodingToolMetadataKeys.DebugOperation, out var metadata).Should().BeTrue();
        metadata!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task EveryPublicOperation_ReachesAnExplicitDispatcherCase()
    {
        await using var manager = new DebugSessionManager();
        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(
                new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory())),
            new DebugResultFormatter(),
            new DebugPermissionAuthorizationService());
        var operationTypes = typeof(DebugOperation)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.DerivedType)
            .ToArray();

        foreach (var operationType in operationTypes)
        {
            var operation = (DebugOperation)CreateValue(operationType, operationType.Name)!;
            var action = DebugOperationDispatcher.Action(operation);
            var callId = $"dispatch-{action}";
            var context = CreateContext(manager, callId, action);

            var xml = await dispatcher.ExecuteAsync(operation, context, CancellationToken.None);
            var root = XDocument.Parse(xml).Root!;

            root.Attribute("action")!.Value.Should().Be(action);
            root.Attribute("kind")?.Value.Should().NotBe("internal_failure",
                $"{action} must have an explicit, type-correct dispatcher path");
        }
    }

    private static object? CreateValue(Type type, string parameterName)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return null;
        if (type == typeof(string))
            return parameterName switch
            {
                "Base64Data" => "AA==",
                "Path" => "/workspace/app.py",
                _ => parameterName + "_1"
            };
        if (type == typeof(int))
            return 1;
        if (type == typeof(long))
            return 1L;
        if (type == typeof(bool))
            return false;
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(0);
        if (type == typeof(DebugLaunchTarget))
            return new SourceFileDebugLaunchTarget("/workspace/app.py");
        if (type == typeof(DebugAttachTarget))
            return new ProcessDebugAttachTarget(42);
        if (type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ||
             type.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);

        var constructor = type.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => parameter.HasDefaultValue
                ? parameter.DefaultValue
                : CreateValue(parameter.ParameterType, parameter.Name ?? string.Empty))
            .ToArray();
        return constructor.Invoke(arguments);
    }

    private static FunctionExecutionContext CreateContext(
        IDebugSessionManager manager,
        string callId,
        string action,
        string? decisionCallId = null)
    {
        var function = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions { Name = "Debug", Description = "test" });
        var initialState = AgentLoopState.InitialSafe(
            [], "run-1", "conversation-1", "DebugTests");
        var permissionState = new DebugPermissionStateData().WithDecision(
            decisionCallId ?? callId,
            action,
            DebugPermissionMiddleware.Classify(action));
        var state = initialState with
        {
            MiddlewareState = initialState.MiddlewareState.SetState(
                typeof(DebugPermissionStateData).FullName!,
                permissionState)
        };
        var session = new Session("session-1");
        var thread = new Thread("session-1", "debug-tests") { Id = "thread-1" };
        var coordinator = new EventCoordinator();
        var agentContext = new AgentContext(
            "DebugTests", "conversation-1", state, coordinator, session, thread,
            CancellationToken.None);
        agentContext.RuntimeCapabilities.Set(manager);
        agentContext.RuntimeCapabilities.Set(new DebugRuntimeBindingState());
        var before = agentContext.AsBeforeFunction(
            function, callId, new Dictionary<string, object?>(),
            new AgentRunConfig(), nameof(CodingToolHarness), null);
        return new FunctionExecutionContext(before, new FunctionRequest
        {
            Function = function,
            CallId = callId,
            Arguments = new Dictionary<string, object?>(),
            State = state,
            RunConfig = new AgentRunConfig(),
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = coordinator
        });
    }
}
