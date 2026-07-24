using System.Text.Json.Serialization;
using System.Reflection;
using System.Xml.Linq;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugModelFacingTests
{
    [Fact]
    public void BasicStepOperations_OmitOptionalGranularityByDefault()
    {
        var stepOver = new StepOverDebugOperation("tree");
        var stepIn = new StepInDebugOperation("tree");
        var stepOut = new StepOutDebugOperation("tree");
        var stepBack = new StepBackDebugOperation("tree");
        stepOver.ThreadId.Should().BeNull();
        stepOver.Granularity.Should().BeNull();
        stepIn.ThreadId.Should().BeNull();
        stepIn.Granularity.Should().BeNull();
        stepOut.ThreadId.Should().BeNull();
        stepOut.Granularity.Should().BeNull();
        stepBack.ThreadId.Should().BeNull();
        stepBack.Granularity.Should().BeNull();
        new ContinueDebugOperation("tree").ThreadId.Should().BeNull();
        new ReverseContinueDebugOperation("tree").ThreadId.Should().BeNull();
        new GetStackTraceOperation("tree").ThreadId.Should().BeNull();
    }

    [Fact]
    public void ProtocolFailures_AreClassifiedAtThePublicBoundary()
    {
        var adapterFailure = DebugOperationDispatcher.ClassifyProtocolFailure(
            "evaluate",
            new DebugAdapterRequestException(new DebugAdapterError
            {
                Command = "evaluate",
                ResponseMessage = "Evaluation failed."
            }));
        var protocolFailure = DebugOperationDispatcher.ClassifyProtocolFailure(
            "evaluate",
            new DebugProtocolException("TRANSPORT_EOF", "closed"));

        adapterFailure.Kind.Should().Be("adapter_request_failed");
        protocolFailure.Kind.Should().Be("adapter_protocol_failed");
    }

    [Fact]
    public void Capability_projection_is_bounded_deterministic_and_uses_public_actions()
    {
        var summary = DebugCapabilityProjection.Project(new Capabilities
        {
            SupportsSetVariable = true,
            SupportsSetExpression = false,
            SupportsStepBack = true,
            SupportsSteppingGranularity = true,
            ExceptionBreakpointFilters =
            [
                new ExceptionBreakpointsFilter { Filter = "user-unhandled", Label = "User unhandled", SupportsCondition = true },
                new ExceptionBreakpointsFilter { Filter = "all", Label = "All", Default = true }
            ]
        });

        summary.SupportedOptionalActions.Should().Contain(["setVariable", "stepBack", "reverseContinue"]);
        summary.UnsupportedOptionalActions.Should().Contain("setExpression");
        summary.ExecutionOptions.Should().Contain("steppingGranularity");
        summary.ExceptionFilters.Select(item => item.FilterId).Should().Equal("all", "user-unhandled");

        var eventBacked = DebugCapabilityProjection.Project(
            new Capabilities(),
            hasProjectedModules: true);
        eventBacked.SupportedOptionalActions.Should().Contain("getModules");
        eventBacked.UnsupportedOptionalActions.Should().NotContain("getModules");
    }

    [Fact]
    public void Mutation_rejection_is_distinct_from_missing_capability()
    {
        var failure = DebugOperationDispatcher.ClassifyProtocolFailure(
            "setVariable",
            new DebugAdapterRequestException(new DebugAdapterError
            {
                Command = "setVariable",
                ResponseMessage = "not writable"
            }));

        failure.Kind.Should().Be("mutation_rejected");
        failure.Message.Should().Contain("advertises 'setVariable' support");
        failure.Message.Should().Contain("writable location");
    }

    [Fact]
    public void Formatter_includes_actionable_exception_filter_recovery()
    {
        var xml = new DebugResultFormatter().Failure(
            "setExceptionBreakpoints",
            "invalid_exception_filter",
            "Unsupported filter.",
            [KeyValuePair.Create<string, object?>("availableFilterIds", "all,user-unhandled")],
            ["all: All; default=True; supportsCondition=False"]);

        var element = XElement.Parse(xml);
        element.Attribute("available_filter_ids")!.Value.Should().Be("all,user-unhandled");
        element.Elements("item").Single().Value.Should().Contain("default=True");
    }

    [Fact]
    public void Mutation_result_describes_exact_token_invalidation_boundary()
    {
        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(),
            new DebugResultFormatter(),
            new DebugPermissionAuthorizationService());

        var xml = dispatcher.MutationResult(
            "setExpression",
            new DebugSemanticMutationResult(
                "3", "int", null, null, null, null, null,
                PriorVariableDerivedTokensInvalidated: true));

        var element = XElement.Parse(xml);
        element.Attribute("prior_variable_tokens_invalidated")!.Value.Should().Be("true");
        element.Attribute("frame_tokens_remain_valid")!.Value.Should().Be("true");
        element.Attribute("next_action")!.Value.Should().Be("inspectStop");
        element.Elements("item").Single().Value.Should()
            .Contain("variable, memory, and value-location tokens");
    }

    [Fact]
    public void Thread_projection_identifies_only_the_adapter_designated_focal_thread()
    {
        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(),
            new DebugResultFormatter(),
            new DebugPermissionAuthorizationService());

        var xml = dispatcher.ProjectThreads(
        [
            new(10, "worker", true, false, null, 1, 0),
            new(20, "breakpoint", true, true, "breakpoint", 1, 0)
        ]);

        var root = XDocument.Parse(xml).Root!;
        root.Attribute("primary_stopped_thread_id")!.Value.Should().Be("20");
        root.Elements("item").Select(item => item.Value).Should().Equal(
            "10 worker stopped=True primary=False reason=",
            "20 breakpoint stopped=True primary=True reason=breakpoint");
    }

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
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
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
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
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
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        var formatter = new DebugResultFormatter();
        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(),
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
    public void Launch_notice_requires_absence_of_every_initial_stopping_strategy()
    {
        DebugStartResultProjector.NeedsStoppingStrategyNotice(
            "launch",
            new DebugInitialConfiguration()).Should().BeTrue();
        DebugStartResultProjector.NeedsStoppingStrategyNotice(
            "attach",
            new DebugInitialConfiguration()).Should().BeFalse();
        DebugStartResultProjector.NeedsStoppingStrategyNotice(
            "launch",
            new DebugInitialConfiguration { StopOnEntry = true }).Should().BeFalse();
        DebugStartResultProjector.NeedsStoppingStrategyNotice(
            "launch",
            new DebugInitialConfiguration
            {
                SourceBreakpoints = [new("/workspace/app.cs", 10)]
            }).Should().BeFalse();
        DebugStartResultProjector.NeedsStoppingStrategyNotice(
            "launch",
            new DebugInitialConfiguration
            {
                ExceptionFilters = [new("all")]
            }).Should().BeFalse();
    }

    [Fact]
    public async Task Launch_notice_is_projected_into_xml_and_typed_metadata()
    {
        await using var manager = new DebugSessionManager(
            new DebugTerminalRecordStore(
                new DebugTerminalRecordStoreOptions()));
        var context = CreateContext(manager, "call-launch", "launch");
        var plan = new ModelFacingStubPlan
        {
            PlannerId = "fixture",
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = "environment",
            EnvironmentRevision = 1,
            CanonicalWorkingDirectory = "/workspace",
            InitialConfiguration = new()
        };
        var result = new DebugSessionStartResult(
            "tree",
            "session",
            new BackgroundHandleSnapshot
            {
                HandleId = "handle",
                Name = "debug",
                Kind = BackgroundHandleKind.DebugSession,
                SourceKind = BackgroundTaskSourceKind.Runtime,
                Status = "Running"
            },
            DebugSessionStatus.Running,
            1,
            new(0, 0, 0, 0));

        var xml = new DebugStartResultProjector(
            new DebugResultFormatter()).Project(
                "launch",
                plan,
                result,
                context);

        XDocument.Parse(xml).Root!.Attribute("warning")!.Value
            .Should().Be("no_initial_stop_strategy");
        context.ResultMetadata.TryGet<
            DebugLaunchNoticeMetadata[]>(
            CodingToolMetadataKeys.DebugLaunchNotices,
            out var notices).Should().BeTrue();
        notices.Should().ContainSingle()
            .Which.Code.Should().Be("no_initial_stop_strategy");
    }

    [Fact]
    public async Task Terminate_is_an_evidence_preserving_noop_for_a_terminal_tree()
    {
        var store = new DebugTerminalRecordStore(
            new DebugTerminalRecordStoreOptions());
        await using var manager = new DebugSessionManager(store);
        store.Retain(Terminal(manager.RuntimeId, "terminal-tree"));
        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(),
            new DebugResultFormatter(),
            new DebugPermissionAuthorizationService());
        var context = CreateContext(
            manager,
            "call-terminate",
            "terminate");

        var xml = await dispatcher.ExecuteAsync(
            new TerminateDebugOperation("terminal-tree"),
            context,
            CancellationToken.None);

        var root = XDocument.Parse(xml).Root!;
        root.Attribute("success")!.Value.Should().Be("true");
        root.Attribute("already_terminated")!.Value.Should().Be("true");
        root.Attribute("terminal_record_retained")!.Value.Should().Be("true");
        manager.TryResolveTerminal(
            new(manager.RuntimeId, "session-1", "thread-1"),
            "terminal-tree",
            out _).Should().BeTrue();
        context.ResultMetadata.TryGet<DebugTerminalRecordMetadata>(
            CodingToolMetadataKeys.DebugTerminalRecord,
            out _).Should().BeTrue();
    }

    [Fact]
    public async Task EveryPublicOperation_ReachesAnExplicitDispatcherCase()
    {
        await using var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(),
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
        if (type == typeof(DebugTarget))
            return new SourceFileDebugTarget("/workspace/app.py");
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

    private static DebugTerminalRecord Terminal(
        string runtimeId,
        string treeId)
    {
        var completed = DateTimeOffset.UtcNow;
        return new DebugTerminalRecord
        {
            Ownership = new(
                runtimeId,
                "session-1",
                "thread-1",
                treeId,
                "environment",
                1),
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            AdapterStartMethod = DebugAdapterStartMethod.Launch,
            AdapterId = "fixture",
            FinalStatus = "Terminated",
            ExitCode = 0,
            StartedAt = completed.AddSeconds(-1),
            CompletedAt = completed,
            Breakpoints = new(0, 0, 0, 0),
            Snapshot = new(
                treeId,
                null,
                "Terminated",
                [],
                0,
                false,
                0,
                0,
                0,
                0,
                0),
            Output = new([], 1, 0, 0, 0, 0),
            Artifacts = []
        };
    }

    private sealed record ModelFacingStubPlan : DebugExecutionPlan;

}
