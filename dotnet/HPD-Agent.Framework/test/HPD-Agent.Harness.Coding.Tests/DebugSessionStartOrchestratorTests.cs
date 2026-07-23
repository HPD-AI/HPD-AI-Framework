using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;
using HPDOS.ToolHarnesses.Middleware;
using HPD.Agent;
using HPD.Events;
using HPD.Events.Core;
using HPD.Environment.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugSessionStartOrchestratorTests
{
    [RealAdapterFact("HPD_DEBUGPY_PYTHON")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_debug_function_qualifies_debugpy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hpd-public-debugpy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var program = Path.Combine(directory, "smoke.py");
        await File.WriteAllTextAsync(program,
            "value = 40\nvalue += 2\nprint(value)\nprint('x' * 20000)\n");
        try
        {
            await QualifyPublicRealAdapterAsync(
                DebugpyDescriptor(),
                System.Environment.GetEnvironmentVariable("HPD_DEBUGPY_PYTHON")!,
                ["-m", "debugpy.adapter"],
                program,
                "sourceFile");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [RealAdapterFact("HPD_NETCOREDBG", "HPD_DOTNET")]
    [Trait("Category", "RealAdapter")]
    public async Task Public_debug_function_qualifies_netcoredbg()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hpd-public-netcoredbg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "Smoke.csproj");
        await File.WriteAllTextAsync(project,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"),
            "var value = 42; System.Console.WriteLine(value);\n");
        try
        {
            using (var compiler = Process.Start(new ProcessStartInfo(
                System.Environment.GetEnvironmentVariable("HPD_DOTNET")!)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                ArgumentList = { "build", project, "--nologo", "--verbosity", "quiet" }
            }) ?? throw new InvalidOperationException("Failed to start dotnet."))
            {
                await compiler.WaitForExitAsync();
                compiler.ExitCode.Should().Be(0, await compiler.StandardError.ReadToEndAsync());
            }
            await QualifyPublicRealAdapterAsync(
                NetcoredbgDescriptor(),
                System.Environment.GetEnvironmentVariable("HPD_NETCOREDBG")!,
                ["--interpreter=vscode"],
                Path.Combine(directory, "bin", "Debug", "net8.0", "Smoke.dll"),
                "executable");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static async Task QualifyPublicRealAdapterAsync(
        DebugAdapterDescriptor descriptor,
        string command,
        IReadOnlyList<string> commandArguments,
        string target,
        string targetKind)
    {
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        var orchestrator = new DebugSessionStartOrchestrator(
            new DebugProtocolTransportFactory(new RealProcessConnector(command, commandArguments)));
        var factory = new PlannerFixtureFactory();
        using var catalogServices = new ServiceCollection().BuildServiceProvider();
        var catalog = new DebugAdapterCatalog(
            [new PlannerCatalogProvider(descriptor, factory)], catalogServices);
        var trust = new PlannerTrustPolicy();
        var formatter = new DebugResultFormatter();
        var planner = new DebugStartPlanningService(
            new DebugAdapterSelector(catalog, new DebugAdapterAvailabilityCache(), trust,
                new LexicalDebugWorkspaceCanonicalizer()),
            new BuiltInDebugAdapterConfigurationComposer(), trust, orchestrator, formatter);
        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(orchestrator), formatter,
            new DebugPermissionAuthorizationService(), planner);
        using var services = new ServiceCollection().AddSingleton(dispatcher).BuildServiceProvider();
        var function = (HPDAIFunctionFactory.HPDAIFunction)
            CodingToolHarnessRegistration.CreateToolHarness(
                new CodingToolHarness(),
                serialization: new HPDToolSerializationOptions(CodingToolHarnessJsonContext.Default.Options))
            .Single(candidate => candidate.Name == "Debug");
        FunctionExecutionContext Context(string action) => FunctionContext(
            manager, function, services, PlannerExecution(), registry,
            Path.GetDirectoryName(target), action);
        var initialConfiguration = targetKind == "sourceFile"
            ? $$$""","initialConfiguration":{"sourceBreakpoints":[{"path":{{{JsonSerializer.Serialize(target)}}},"line":2}]}"""
            : string.Empty;
        var launchXml = await InvokeDebugAsync(function, Context("launch"),
            $$$"""{"request":{"action":"launch","adapterId":"{{{descriptor.Id}}}","target":{"targetKind":"{{{targetKind}}}","path":{{{JsonSerializer.Serialize(target)}}}},"stopOnEntry":true{{{initialConfiguration}}}}}""");
        var launchRoot = System.Xml.Linq.XDocument.Parse(launchXml).Root!;
        launchRoot.Attribute("success")?.Value.Should().Be("true", launchXml);
        var treeId = launchRoot.Attribute("debug_tree_id")?.Value
            ?? throw new InvalidOperationException($"Launch did not return a debug tree: {launchXml}");
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");
        await WaitUntilAsync(() =>
            manager.ResolveTree(owner, treeId).SelectSession().State.Threads.Any(thread => thread.IsStopped));
        var threadId = manager.ResolveTree(owner, treeId).SelectSession().State.Threads
            .Single(thread => thread.IsStopped).ThreadId;

        var inspection = await InvokeDebugAsync(function, Context("inspectStop"),
            $$$"""{"request":{"action":"inspectStop","debugTreeId":"{{{treeId}}}","threadId":{{{threadId}}},"maximumFrames":5,"maximumVariablesPerScope":30}}""");
        var inspectionRoot = System.Xml.Linq.XDocument.Parse(inspection).Root!;
        inspectionRoot.Attribute("success")!.Value.Should().Be("true");
        int.Parse(inspectionRoot.Attribute("frame_count")!.Value).Should().BeGreaterThan(0);

        var snapshot = await InvokeDebugAsync(function, Context("snapshot"),
            $$$"""{"request":{"action":"snapshot","debugTreeId":"{{{treeId}}}","maximumOutputBytes":4096}}""");
        var snapshotRoot = System.Xml.Linq.XDocument.Parse(snapshot).Root!;
        snapshotRoot.Attribute("success")!.Value.Should().Be("true", snapshot);
        snapshot.Length.Should().BeLessThan(16_384);

        var continued = await InvokeDebugAsync(function, Context("continue"),
            $$$"""{"request":{"action":"continue","debugTreeId":"{{{treeId}}}","threadId":{{{threadId}}}}}""");
        System.Xml.Linq.XDocument.Parse(continued).Root!.Attribute("success")!.Value
            .Should().Be("true", continued);
        await registry.Handle!.StopAsync(new() { Reason = "qualification complete" }, CancellationToken.None);
    }

    private static async Task<string> InvokeDebugAsync(
        HPDAIFunctionFactory.HPDAIFunction function,
        FunctionExecutionContext context,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        var arguments = new AIFunctionArguments();
        arguments.SetJson(document.RootElement.Clone());
        var result = await function.InvokeAsync(arguments, context, CancellationToken.None);
        return result is JsonElement element ? element.GetString()! : (string)result!;
    }

    [Fact]
    public async Task Public_debug_function_launches_through_the_trusted_semantic_planner()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var childTransport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        var connector = new QueueConnector(transport, childTransport);
        var orchestrator = new DebugSessionStartOrchestrator(
            new DebugProtocolTransportFactory(connector));
        var descriptor = new DebugAdapterDescriptor
        {
            Id = "debugpy",
            Languages = ["python"],
            FileExtensions = [".py"],
            RootMarkers = [],
            TargetKinds = DebugTargetKind.SourceFile,
            Provenance = new()
            {
                PackageId = "debugpy",
                PackageVersion = "1",
                AssemblyName = "fixture"
            }
        };
        var factory = new PlannerFixtureFactory();
        using var catalogServices = new ServiceCollection().BuildServiceProvider();
        var catalog = new DebugAdapterCatalog(
            [new PlannerCatalogProvider(descriptor, factory)],
            catalogServices);
        var trust = new PlannerTrustPolicy();
        var selector = new DebugAdapterSelector(
            catalog,
            new DebugAdapterAvailabilityCache(),
            trust,
            new LexicalDebugWorkspaceCanonicalizer());
        var formatter = new DebugResultFormatter();
        var hostRequests = new RecordingHostRequestBroker();
        var planner = new DebugStartPlanningService(
            selector,
            new BuiltInDebugAdapterConfigurationComposer(),
            trust,
            orchestrator,
            formatter,
            hostRequestBroker: hostRequests,
            childSessionPlanFactory: new ChildPlanFactory());
        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(orchestrator),
            formatter,
            new DebugPermissionAuthorizationService(),
            planner);
        using var services = new ServiceCollection()
            .AddSingleton(dispatcher)
            .BuildServiceProvider();
        var debugFunction = (HPDAIFunctionFactory.HPDAIFunction)
            CodingToolHarnessRegistration.CreateToolHarness(
                new CodingToolHarness(),
                serialization: new HPDToolSerializationOptions(
                    CodingToolHarnessJsonContext.Default.Options))
                .Single(function => function.Name == "Debug");
        var execution = PlannerExecution();
        var context = FunctionContext(
            manager, debugFunction, services, execution, registry,
            permissionAction: "launch");
        var target = Path.Combine(Directory.GetCurrentDirectory(), "fixture.py")
            .Replace("\\", "\\\\", StringComparison.Ordinal);
        using var argumentsDocument = JsonDocument.Parse(
            $"{{\"request\":{{\"action\":\"launch\",\"adapterId\":\"debugpy\",\"target\":{{\"targetKind\":\"sourceFile\",\"path\":\"{target}\"}},\"stopOnEntry\":true}}}}");
        var arguments = new AIFunctionArguments();
        arguments.SetJson(argumentsDocument.RootElement.Clone());

        var invocation = debugFunction.InvokeAsync(
            arguments, context, CancellationToken.None).AsTask();
        var initializeRead = ReadRequestAsync(transport);
        var first = await Task.WhenAny(invocation, initializeRead);
        if (first == invocation)
        {
            var early = await invocation;
            throw new InvalidOperationException(
                $"Launch returned before adapter initialization: {early}");
        }
        var initialize = await initializeRead;
        await RespondAsync(transport, initialize,
            "{\"supportsConfigurationDoneRequest\":false}");
        var launch = await ReadRequestAsync(transport);
        launch.Command.Should().Be("launch");
        launch.Arguments.GetProperty("program").GetString()
            .Should().EndWith("fixture.py");
        launch.Arguments.GetProperty("stopOnEntry").GetBoolean().Should().BeTrue();
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        var result = await invocation;
        var xml = result is JsonElement element ? element.GetString()! : (string)result!;
        var root = System.Xml.Linq.XDocument.Parse(xml).Root!;

        root.Attribute("success")!.Value.Should().Be("true");
        root.Attribute("adapter")!.Value.Should().Be("debugpy");
        var owner = new DebugTreeLookupScope(
            manager.RuntimeId, "owner-session", "owner-thread");
        manager.ListTrees(owner).Should().ContainSingle();
        var publicTreeId = root.Attribute("debug_tree_id")!.Value;

        await FeedAsync(transport,
            """{"seq":49,"type":"request","command":"runInTerminal","arguments":{"kind":"integrated","title":"fixture terminal","cwd":"/workspace","args":["python","child.py"],"env":{"FIXTURE":"1"}}}""");
        var terminalResponse = await ReadMessageAsync(transport);
        terminalResponse.GetProperty("type").GetString().Should().Be("response");
        terminalResponse.GetProperty("command").GetString().Should().Be("runInTerminal");
        terminalResponse.GetProperty("success").GetBoolean().Should().BeTrue();
        terminalResponse.GetProperty("body").GetProperty("processId").GetInt32()
            .Should().Be(4242);
        hostRequests.Requests.Should().ContainSingle();
        hostRequests.Requests[0].Arguments.Should().Equal("python", "child.py");
        hostRequests.Requests[0].EnvironmentDelta["FIXTURE"].Should().Be("1");

        await FeedAsync(transport,
            """{"seq":50,"type":"request","command":"startDebugging","arguments":{"configuration":{"program":"child.py"},"outputPresentation":"separate","request":"launch"}}""");
        var childInitialize = await ReadRequestAsync(childTransport);
        await RespondAsync(childTransport, childInitialize,
            "{\"supportsConfigurationDoneRequest\":false}");
        var childLaunch = await ReadRequestAsync(childTransport);
        childLaunch.Arguments.GetProperty("program").GetString().Should().Be("child.py");
        await FeedAsync(childTransport,
            """{"seq":51,"type":"event","event":"initialized"}""");
        await RespondAsync(childTransport, childLaunch, "{}");
        var childResponse = await ReadMessageAsync(transport);
        childResponse.GetProperty("type").GetString().Should().Be("response");
        childResponse.GetProperty("command").GetString().Should().Be("startDebugging");
        childResponse.GetProperty("success").GetBoolean().Should().BeTrue();
        await WaitUntilAsync(() => manager.ResolveTree(owner, publicTreeId).Sessions.Count == 2);
        connector.ConnectCount.Should().Be(2);

        await registry.Handle!.StopAsync(new() { Reason = "test" }, CancellationToken.None);
        transport.IsAlive.Should().BeFalse();
        childTransport.IsAlive.Should().BeFalse();
    }

    [Fact]
    public async Task Model_facing_dispatcher_snapshots_and_terminates_a_published_tree()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        using var document = JsonDocument.Parse("{}");
        var orchestrator = new DebugSessionStartOrchestrator(
            new DebugProtocolTransportFactory(new FixedConnector(transport)));
        var start = orchestrator.StartAsync(new()
        {
            Runtime = Runtime(manager),
            LaunchPlan = Plan(document.RootElement),
            BackgroundHandles = registry,
            InitializeFeatures = new()
        }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false}");
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        var started = await start;

        var dispatcher = new DebugOperationDispatcher(
            new DebugRuntimeServiceFactory(orchestrator),
            new DebugResultFormatter(),
            new DebugPermissionAuthorizationService());
        using var services = new ServiceCollection()
            .AddSingleton(dispatcher)
            .BuildServiceProvider();
        var debugFunction = (HPDAIFunctionFactory.HPDAIFunction)
            CodingToolHarnessRegistration.CreateToolHarness(
                new CodingToolHarness(),
                serialization: new HPDToolSerializationOptions(
                    CodingToolHarnessJsonContext.Default.Options))
                .Single(function => function.Name == "Debug");

        var owner = new DebugTreeLookupScope(
            manager.RuntimeId, "owner-session", "owner-thread");
        await FeedAsync(transport,
            """{"seq":21,"type":"event","event":"thread","body":{"reason":"started","threadId":1}}""");
        await FeedAsync(transport,
            """{"seq":22,"type":"event","event":"stopped","body":{"reason":"breakpoint","threadId":1}}""");
        var projectedStack = await ReadRequestAsync(transport);
        projectedStack.Command.Should().Be("stackTrace");
        await RespondAsync(transport, projectedStack,
            "{\"stackFrames\":[{\"id\":10,\"name\":\"main\",\"line\":7,\"column\":3}],\"totalFrames\":1}");
        await WaitUntilAsync(() => manager.ResolveTree(owner, started.DebugTreeId)
            .SelectSession().State.Status == DebugSessionStatus.Stopped);

        var inspectionContext = FunctionContext(
            manager, debugFunction, services, permissionAction: "inspectStop");
        using var inspectionArgumentsDocument = JsonDocument.Parse(
            $"{{\"request\":{{\"action\":\"inspectStop\",\"debugTreeId\":\"{started.DebugTreeId}\",\"threadId\":1}}}}");
        var inspectionArguments = new AIFunctionArguments();
        inspectionArguments.SetJson(inspectionArgumentsDocument.RootElement.Clone());
        var inspectionInvocation = debugFunction.InvokeAsync(
            inspectionArguments, inspectionContext, CancellationToken.None).AsTask();
        var threads = await ReadRequestAsync(transport);
        threads.Command.Should().Be("threads");
        await RespondAsync(transport, threads,
            "{\"threads\":[{\"id\":1,\"name\":\"main\"}]}");
        var stack = await ReadRequestAsync(transport);
        stack.Command.Should().Be("stackTrace");
        await RespondAsync(transport, stack,
            "{\"stackFrames\":[{\"id\":10,\"name\":\"main\",\"line\":7,\"column\":3}],\"totalFrames\":1}");
        var scopes = await ReadRequestAsync(transport);
        scopes.Command.Should().Be("scopes");
        await RespondAsync(transport, scopes,
            "{\"scopes\":[{\"name\":\"Locals\",\"variablesReference\":20,\"expensive\":false}]}");
        var variables = await ReadRequestAsync(transport);
        variables.Command.Should().Be("variables");
        await RespondAsync(transport, variables,
            "{\"variables\":[{\"name\":\"answer\",\"value\":\"42\",\"type\":\"int\",\"variablesReference\":0}]}");
        var inspectionResult = await inspectionInvocation;
        var inspectionXml = inspectionResult is JsonElement inspectionElement
            ? inspectionElement.GetString()!
            : (string)inspectionResult!;
        var inspectionRoot = System.Xml.Linq.XDocument.Parse(inspectionXml).Root!;
        inspectionRoot.Attribute("success")!.Value.Should().Be("true");
        inspectionRoot.Attribute("frame_count")!.Value.Should().Be("1");
        inspectionRoot.Attribute("scope_count")!.Value.Should().Be("1");
        inspectionRoot.Attribute("variable_count")!.Value.Should().Be("1");
        inspectionRoot.Descendants("item").Should()
            .Contain(item => item.Value.Contains("Locals.answer=42", StringComparison.Ordinal));
        inspectionContext.ResultMetadata.TryGet<DebugStopInspectionMetadata>(
            CodingToolMetadataKeys.DebugStopSnapshot, out _).Should().BeTrue();

        var snapshotContext = FunctionContext(
            manager, debugFunction, services, permissionAction: "snapshot");
        using var snapshotArgumentsDocument = JsonDocument.Parse(
            $"{{\"request\":{{\"action\":\"snapshot\",\"debugTreeId\":\"{started.DebugTreeId}\"}}}}");
        var snapshotArguments = new AIFunctionArguments();
        snapshotArguments.SetJson(snapshotArgumentsDocument.RootElement.Clone());
        var snapshotResult = await debugFunction.InvokeAsync(
            snapshotArguments, snapshotContext, CancellationToken.None);
        var snapshotXml = snapshotResult is JsonElement element
            ? element.GetString()!
            : (string)snapshotResult!;

        var snapshot = System.Xml.Linq.XDocument.Parse(snapshotXml).Root!;
        snapshot.Attribute("success")!.Value.Should().Be("true");
        snapshot.Attribute("debug_tree_id")!.Value.Should().Be(started.DebugTreeId);
        snapshotContext.ResultMetadata.TryGet<DebugTreeSnapshot>(
            CodingToolMetadataKeys.DebugSessionSnapshot, out _).Should().BeTrue();

        var termination = dispatcher.ExecuteAsync(
            new TerminateDebugOperation(started.DebugTreeId),
            FunctionContext(manager, permissionAction: "terminate"),
            CancellationToken.None);
        var disconnect = await ReadRequestAsync(transport);
        disconnect.Command.Should().Be("disconnect");
        await RespondAsync(transport, disconnect, "{}");
        var terminationXml = await termination;

        System.Xml.Linq.XDocument.Parse(terminationXml).Root!
            .Attribute("success")!.Value.Should().Be("true");
        manager.ListTrees(owner)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Repeated_start_and_tree_termination_leave_no_live_trees_or_transports()
    {
        await using var manager = new DebugSessionManager();
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");

        for (var iteration = 0; iteration < 32; iteration++)
        {
            await using var transport = new InMemoryDebugProtocolTransport();
            var registry = new RecordingRegistry();
            using var document = JsonDocument.Parse("{}");
            var start = new DebugSessionStartOrchestrator(
                new DebugProtocolTransportFactory(new FixedConnector(transport))).StartAsync(new()
            {
                Runtime = Runtime(manager),
                LaunchPlan = Plan(document.RootElement),
                BackgroundHandles = registry,
                InitializeFeatures = new()
            }, CancellationToken.None).AsTask();
            var initialize = await ReadRequestAsync(transport);
            await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false}");
            var launch = await ReadRequestAsync(transport);
            await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
            await RespondAsync(transport, launch, "{}");
            var started = await start;
            var semantics = new DebugSemanticService(manager);
            var termination = new DebugLifecycleService(manager, semantics).TerminateAsync(
                owner, started.DebugTreeId, null, DebugTerminationScope.Tree,
                terminateDebuggee: true, CancellationToken.None);
            var disconnect = await ReadRequestAsync(transport);
            disconnect.Command.Should().Be("disconnect");
            await RespondAsync(transport, disconnect, "{}");
            var result = await termination;

            result.Graceful.Should().BeTrue();
            manager.ListTrees(owner).Should().BeEmpty();
            transport.IsAlive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Published_root_protocol_fault_disposes_tree_and_publishes_safe_terminal_event()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var events = new RecordingEventPublisher();
        using var document = JsonDocument.Parse("{}");
        var start = new DebugSessionStartOrchestrator(
            new DebugProtocolTransportFactory(new FixedConnector(transport))).StartAsync(new()
        {
            Runtime = Runtime(manager), LaunchPlan = Plan(document.RootElement),
            BackgroundHandles = new RecordingRegistry(), EventPublisher = events,
            InitializeFeatures = new()
        }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false}");
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        var result = await start;
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");

        await FeedAsync(transport, "{not-json");
        await WaitUntilAsync(() => manager.ListTrees(owner).Count == 0 && events.Events.OfType<DebugTreeFaultedEvent>().Any());
        events.Events.OfType<DebugTreeFaultedEvent>().Single().SafeReasonCode.Should().Be("MALFORMED_JSON");
    }

    [Fact]
    public async Task Dynamic_capability_removal_immediately_disables_semantic_operation()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        using var document = JsonDocument.Parse("{}");
        var start = new DebugSessionStartOrchestrator(
            new DebugProtocolTransportFactory(new FixedConnector(transport))).StartAsync(new()
        {
            Runtime = Runtime(manager),
            LaunchPlan = Plan(document.RootElement),
            BackgroundHandles = new RecordingRegistry(),
            InitializeFeatures = new()
        }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize,
            "{\"supportsConfigurationDoneRequest\":false,\"supportsLoadedSourcesRequest\":true}");
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        var result = await start;
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");

        await FeedAsync(transport,
            """{"seq":21,"type":"event","event":"capabilities","body":{"capabilities":{"supportsLoadedSourcesRequest":false}}}""");
        await WaitUntilAsync(() => manager.ResolveTree(owner, result.DebugTreeId).SelectSession()
            .Capabilities?.SupportsLoadedSourcesRequest == false);

        var semantics = new DebugSemanticService(manager);
        var operation = () => semantics.LoadedSourcesAsync(owner, result.DebugTreeId, null, CancellationToken.None).AsTask();
        await operation.Should().ThrowAsync<InvalidOperationException>();

        await FeedAsync(transport, """{"seq":22,"type":"event","event":"terminated","body":{}}""");
        await WaitUntilAsync(() => manager.ListTrees(owner).Count == 0);
    }

    [Fact]
    public async Task Negotiated_restart_uses_typed_in_place_request()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        using var document = JsonDocument.Parse("{}");
        var start = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory(new FixedConnector(transport)))
            .StartAsync(new()
            {
                Runtime = Runtime(manager), LaunchPlan = Plan(document.RootElement),
                BackgroundHandles = registry, InitializeFeatures = new()
            }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false,\"supportsRestartRequest\":true}");
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        var result = await start;
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");

        var restart = new DebugSemanticService(manager).RestartInPlaceAsync(owner, result.DebugTreeId, null, CancellationToken.None);
        var request = await ReadRequestAsync(transport);
        request.Command.Should().Be("restart");
        await RespondAsync(transport, request, "{}");
        (await restart).Should().BeTrue();
        await registry.Handle!.StopAsync(new() { Reason = "test" }, CancellationToken.None);
    }

    [Fact]
    public async Task Attach_disconnect_explicitly_preserves_debuggee_when_supported()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        using var document = JsonDocument.Parse("{\"processId\":42}");
        var start = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory(new FixedConnector(transport)))
            .StartAsync(new()
            {
                Runtime = Runtime(manager), LaunchPlan = Plan(document.RootElement),
                BackgroundHandles = registry, InitializeFeatures = new(), IsAttach = true
            }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false,\"supportTerminateDebuggee\":true}");
        var attach = await ReadRequestAsync(transport);
        attach.Command.Should().Be("attach");
        attach.Arguments.GetProperty("processId").GetInt32().Should().Be(42);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, attach, "{}");
        var result = await start;
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");

        var disconnect = new DebugSemanticService(manager).DisconnectAsync(
            owner, result.DebugTreeId, null, terminateDebuggee: null, suspendDebuggee: false, CancellationToken.None);
        var request = await ReadRequestAsync(transport);
        request.Arguments.GetProperty("terminateDebuggee").GetBoolean().Should().BeFalse();
        await RespondAsync(transport, request, "{}");
        await disconnect;
    }

    [Fact]
    public async Task Tree_termination_forces_owned_cleanup_when_disconnect_fails()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        using var document = JsonDocument.Parse("{}");
        var start = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory(new FixedConnector(transport)))
            .StartAsync(new()
            {
                Runtime = Runtime(manager), LaunchPlan = Plan(document.RootElement),
                BackgroundHandles = registry, InitializeFeatures = new()
            }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false}");
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        var result = await start;
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");
        var semantics = new DebugSemanticService(manager);
        var termination = new DebugLifecycleService(manager, semantics).TerminateAsync(
            owner, result.DebugTreeId, null, DebugTerminationScope.Tree,
            terminateDebuggee: true, CancellationToken.None);

        (await ReadRequestAsync(transport)).Command.Should().Be("disconnect");
        transport.Complete(new(ProcessCompletionKind.Exited, SafeReasonCode: "TEST_CONNECTION_LOST"));
        var outcome = await termination;

        outcome.Graceful.Should().BeFalse();
        outcome.TreeDisposed.Should().BeTrue();
        outcome.SafeReasonCode.Should().Be("DEBUG_TREE_FORCED_DISPOSAL");
        manager.ListTrees(owner).Should().BeEmpty();
    }

    [Fact]
    public async Task Background_handle_registration_failure_rolls_back_tree_and_transport()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        using var document = JsonDocument.Parse("{}");
        var start = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory(new FixedConnector(transport)))
            .StartAsync(new()
            {
                Runtime = Runtime(manager), LaunchPlan = Plan(document.RootElement),
                BackgroundHandles = new ThrowingRegistry(), InitializeFeatures = new()
            }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false}");
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");

        var failure = async () => await start;
        await failure.Should().ThrowAsync<InvalidOperationException>();
        manager.ListTrees(new(manager.RuntimeId, "owner-session", "owner-thread")).Should().BeEmpty();
        transport.IsAlive.Should().BeFalse();
    }

    [Fact]
    public async Task Launch_withheld_until_configuration_done_publishes_one_live_tree()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        var events = new RecordingEventPublisher();
        var contentStore = new HPD.Agent.InMemoryContentStore();
        var hostTrace = new DebugProtocolHostTraceBuffer();
        using var initiatingCancellation = new CancellationTokenSource();
        var orchestrator = new DebugSessionStartOrchestrator(
            new DebugProtocolTransportFactory(new FixedConnector(transport)));
        using var document = JsonDocument.Parse("{\"program\":\"fixture\"}");
        var start = orchestrator.StartAsync(new()
        {
            Runtime = Runtime(manager),
            LaunchPlan = Plan(document.RootElement),
            BackgroundHandles = registry,
            EventPublisher = events,
            ContentStore = contentStore,
            HostTraceSink = hostTrace,
            InitializeFeatures = new(),
            InitialConfiguration = new() { StopOnEntry = true }
        }, initiatingCancellation.Token).AsTask();
        await Task.Yield();
        if (start.IsCompleted) await start;

        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":true,\"supportsDataBreakpoints\":true}");
        var launch = await ReadRequestAsync(transport);
        launch.Command.Should().Be("launch");
        launch.Arguments.GetProperty("program").GetString().Should().Be("fixture");
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        var configurationDone = await ReadRequestAsync(transport);
        configurationDone.Command.Should().Be("configurationDone");
        await RespondAsync(transport, configurationDone, "{}");
        start.IsCompleted.Should().BeFalse();
        await RespondAsync(transport, launch, "{}");

        var result = await start;
        result.Status.Should().Be(DebugSessionStatus.Running);
        registry.RegisterCount.Should().Be(1);
        manager.ListTrees(new(manager.RuntimeId, "owner-session", "owner-thread")).Should().ContainSingle();
        result.Handle.Status.Should().Be("Running");
        events.Events.Should().ContainSingle().Which.Should().BeOfType<DebugTreeStartedEvent>();
        initiatingCancellation.Cancel();
        manager.ListTrees(new(manager.RuntimeId, "owner-session", "owner-thread")).Should().ContainSingle(
            "cancellation after publication must not terminate the tree");
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");
        await FeedAsync(transport, """{"seq":30,"type":"event","event":"thread","body":{"reason":"started","threadId":1}}""");
        await FeedAsync(transport, """{"seq":31,"type":"event","event":"stopped","body":{"reason":"entry","threadId":1}}""");
        await WaitUntilAsync(() => manager.ResolveTree(owner, result.DebugTreeId).SelectSession().State.Status == DebugSessionStatus.Stopped);
        var topFrames = await ReadRequestAsync(transport);
        topFrames.Command.Should().Be("stackTrace");
        await RespondAsync(transport, topFrames,
            "{\"stackFrames\":[{\"id\":10,\"name\":\"main\",\"line\":1,\"column\":1}],\"totalFrames\":1}");
        await WaitUntilAsync(() => manager.ResolveTree(owner, result.DebugTreeId).SelectSession()
            .Projections.GetStackFrames(1, 1).Count == 1);

        var semantics = new DebugSemanticService(manager);
        var threadsTask = semantics.ThreadsAsync(owner, result.DebugTreeId, null, CancellationToken.None).AsTask();
        var threadsRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, threadsRequest, "{\"threads\":[{\"id\":1,\"name\":\"main\"}]}");
        (await threadsTask).Should().ContainSingle().Which.Name.Should().Be("main");

        var protocolSession = manager.ResolveTree(owner, result.DebugTreeId).SelectSession();
        var frameToken = protocolSession.Projections.CreateSuspensionToken(1, 10, "frame", 10);
        var discoveryTask = semantics.DataBreakpointInfoAsync(owner, result.DebugTreeId, null,
            "counter", variablesToken: null, frameToken, bytes: null, asAddress: null, mode: null,
            CancellationToken.None).AsTask();
        var discoveryRequest = await ReadRequestAsync(transport);
        discoveryRequest.Command.Should().Be("dataBreakpointInfo");
        discoveryRequest.Arguments.GetProperty("frameId").GetInt32().Should().Be(10);
        await RespondAsync(transport, discoveryRequest,
            "{\"dataId\":\"adapter-secret-id\",\"description\":\"counter\",\"accessTypes\":[\"write\"],\"canPersist\":true}");
        var discovery = await discoveryTask;
        discovery.DiscoveryToken.Should().NotBeNull().And.NotBe("adapter-secret-id");
        protocolSession.Projections.ResolveDataBreakpointToken(discovery.DiscoveryToken!).Should().Be("adapter-secret-id");

        await FeedAsync(transport, """{"seq":33,"type":"event","event":"output","body":{"category":"stdout","output":"hello debugger\n","variablesReference":71,"locationReference":72}}""");
        await WaitUntilAsync(() => manager.ResolveTree(owner, result.DebugTreeId).SelectSession().Output.Snapshot().Records.Count == 1);
        var outputRecord = manager.ResolveTree(owner, result.DebugTreeId).SelectSession().Output.Snapshot().Records.Single();
        outputRecord.VariablesToken.Should().NotBeNull().And.NotBe("71");
        outputRecord.LocationToken.Should().NotBeNull().And.NotBe("72");
        var outputVariablesTask = semantics.VariablesAsync(owner, result.DebugTreeId, null,
            outputRecord.VariablesToken!, filter: null, pageSize: 200, continuationToken: null, CancellationToken.None).AsTask();
        var outputVariablesRequest = await ReadRequestAsync(transport);
        outputVariablesRequest.Arguments.GetProperty("variablesReference").GetInt32().Should().Be(71);
        await RespondAsync(transport, outputVariablesRequest,
            "{\"variables\":[{\"name\":\"child\",\"value\":\"value\",\"variablesReference\":73}]}");
        var outputVariables = await outputVariablesTask;
        var nestedOutputToken = outputVariables.Variables.Single().VariablesToken;
        nestedOutputToken.Should().NotBeNull().And.NotBe("73");
        protocolSession.Projections.ResolveSuspensionToken(
            nestedOutputToken!, "variables", out var outputThreadId, out _).Should().Be(73);
        outputThreadId.Should().Be(0);
        var read = await registry.Handle!.ReadAsync(new() { TailLines = 10 }, CancellationToken.None);
        read.Text.Should().Contain("hello debugger");
        read.Metadata.Should().Contain("retainedOutputBytes", "15");
        (await semantics.PersistOutputAsync(owner, result.DebugTreeId, null, false, CancellationToken.None))
            .Status.Should().Be(DebugArtifactWriteStatus.Stored);
        (await registry.Handle.GetArtifactsAsync(CancellationToken.None)).Artifacts
            .Should().ContainSingle().Which.Kind.Should().Be("debug-output");
        hostTrace.Snapshot().Should().NotBeEmpty();
        (await registry.Handle.GetArtifactsAsync(CancellationToken.None)).Artifacts
            .Should().NotContain(x => x.Kind.Contains("trace", StringComparison.OrdinalIgnoreCase));

        var oversizedText = new string('x', DebugOutputBuffer.DefaultMaximumRecordBytes + 1024);
        var encodedOversizedText = Encoding.UTF8.GetString(JsonEncodedText.Encode(oversizedText).EncodedUtf8Bytes);
        await FeedAsync(transport,
            "{\"seq\":34,\"type\":\"event\",\"event\":\"output\",\"body\":{\"category\":\"stderr\",\"output\":\"" +
            encodedOversizedText + "\"}}");
        await WaitUntilAsync(() => manager.ResolveTree(owner, result.DebugTreeId).StoredArtifacts.Count == 2);
        var automaticArtifact = (await registry.Handle.GetArtifactsAsync(CancellationToken.None)).Artifacts.Last();
        automaticArtifact.Kind.Should().Be("debug-output");
        automaticArtifact.Metadata.Should().Contain("category", DebugOutputCategory.StandardError.ToString());

        var continueTask = semantics.ContinueAsync(owner, result.DebugTreeId, null, 1, false, TimeSpan.FromSeconds(2), CancellationToken.None);
        var continueRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, continueRequest, "{\"allThreadsContinued\":false}");
        Action expiredDiscovery = () => protocolSession.Projections.ResolveDataBreakpointToken(discovery.DiscoveryToken!);
        expiredDiscovery.Should().Throw<InvalidOperationException>();
        Action expiredOutputReference = () => protocolSession.Projections.ResolveSuspensionToken(
            outputRecord.VariablesToken!, "variables", out _, out _);
        expiredOutputReference.Should().Throw<InvalidOperationException>();
        await FeedAsync(transport, """{"seq":32,"type":"event","event":"stopped","body":{"reason":"breakpoint","threadId":1}}""");
        (await continueTask).IsStopped.Should().BeTrue();
        var refreshedFrames = await ReadRequestAsync(transport);
        refreshedFrames.Command.Should().Be("stackTrace");
        await RespondAsync(transport, refreshedFrames,
            "{\"stackFrames\":[{\"id\":11,\"name\":\"main\",\"line\":2,\"column\":1}],\"totalFrames\":1}");

        var disconnectTask = semantics.DisconnectAsync(owner, result.DebugTreeId, null, terminateDebuggee: true, suspendDebuggee: false, CancellationToken.None);
        var disconnectRequest = await ReadRequestAsync(transport);
        disconnectRequest.Arguments.TryGetProperty("terminateDebuggee", out _).Should().BeFalse("the adapter did not advertise explicit termination support");
        await RespondAsync(transport, disconnectRequest, "{}");
        await disconnectTask;
        manager.ListTrees(new(manager.RuntimeId, "owner-session", "owner-thread")).Should().BeEmpty();
    }

    [Fact]
    public async Task Start_debugging_uses_fresh_transport_and_replies_only_after_child_is_configured()
    {
        await using var rootTransport = new InMemoryDebugProtocolTransport();
        await using var childTransport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var connector = new QueueConnector(rootTransport, childTransport);
        var registry = new RecordingRegistry();
        var events = new RecordingEventPublisher();
        using var document = JsonDocument.Parse("{}");
        var start = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory(connector)).StartAsync(new()
        {
            Runtime = Runtime(manager),
            LaunchPlan = Plan(document.RootElement),
            BackgroundHandles = registry,
            EventPublisher = events,
            InitializeFeatures = new() { StartDebuggingHandler = true },
            ChildSessionPlanFactory = new ChildPlanFactory(),
            Authorization = new() { Grants = DebugTreeGrant.Routine | DebugTreeGrant.ChildSessions }
        }, CancellationToken.None).AsTask();
        var rootInitialize = await ReadRequestAsync(rootTransport);
        await RespondAsync(rootTransport, rootInitialize, "{\"supportsConfigurationDoneRequest\":false}");
        var rootLaunch = await ReadRequestAsync(rootTransport);
        await FeedAsync(rootTransport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(rootTransport, rootLaunch, "{}");
        var rootResult = await start;
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");

        await FeedAsync(rootTransport, """{"seq":50,"type":"request","command":"startDebugging","arguments":{"configuration":{"program":"child"},"outputPresentation":"separate","request":"launch"}}""");
        var childInitialize = await ReadRequestAsync(childTransport);
        await RespondAsync(childTransport, childInitialize, "{\"supportsConfigurationDoneRequest\":false}");
        var childLaunch = await ReadRequestAsync(childTransport);
        childLaunch.Arguments.GetProperty("program").GetString().Should().Be("child");
        manager.ResolveTree(owner, rootResult.DebugTreeId).Sessions.Should().ContainSingle(
            "the child is not published before launch and configuration complete");
        await FeedAsync(childTransport, """{"seq":21,"type":"event","event":"initialized"}""");
        await RespondAsync(childTransport, childLaunch, "{}");

        await WaitUntilAsync(() => manager.ResolveTree(owner, rootResult.DebugTreeId).Sessions.Count == 2 &&
            events.Events.OfType<DebugChildSessionStartedEvent>().Any());
        connector.ConnectCount.Should().Be(2);
        events.Events.OfType<DebugChildSessionStartedEvent>().Should().ContainSingle().Which
            .OutputPresentation.Should().Be("separate");
        await registry.Handle!.StopAsync(new() { Reason = "test" }, CancellationToken.None);
        rootTransport.IsAlive.Should().BeFalse();
        childTransport.IsAlive.Should().BeFalse();
        manager.ListTrees(owner).Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_child_start_leaves_the_root_tree_live_and_unchanged()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        using var document = JsonDocument.Parse("{}");
        var start = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory(new FixedConnector(transport))).StartAsync(new()
        {
            Runtime = Runtime(manager), LaunchPlan = Plan(document.RootElement), BackgroundHandles = registry,
            InitializeFeatures = new() { StartDebuggingHandler = true },
            ChildSessionPlanFactory = new ThrowingChildPlanFactory(),
            Authorization = new() { Grants = DebugTreeGrant.Routine | DebugTreeGrant.ChildSessions }
        }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false}");
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        var result = await start;
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");

        await FeedAsync(transport, """{"seq":70,"type":"request","command":"startDebugging","arguments":{"configuration":{"program":"bad"},"request":"launch"}}""");
        var response = await ReadMessageAsync(transport);
        response.GetProperty("success").GetBoolean().Should().BeFalse();
        var tree = manager.ResolveTree(owner, result.DebugTreeId);
        tree.Sessions.Should().ContainSingle();
        tree.SelectSession().SessionId.Should().Be(result.DebugSessionId);
        transport.IsAlive.Should().BeTrue();
        await registry.Handle!.StopAsync(new() { Reason = "test" }, CancellationToken.None);
    }

    [Fact]
    public async Task Run_in_terminal_shell_interpretation_fails_closed_without_tree_grant()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        using var coordinator = new EventCoordinator();
        var registry = new RecordingRegistry();
        using var document = JsonDocument.Parse("{}");
        var start = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory(new FixedConnector(transport))).StartAsync(new()
        {
            Runtime = Runtime(manager), LaunchPlan = Plan(document.RootElement), BackgroundHandles = registry,
            InitializeFeatures = new() { RunInTerminalHandler = true, ShellArgumentAuthorization = true },
            HostRequestBroker = new DebugHostRequestBroker(coordinator, null),
            Authorization = new() { Grants = DebugTreeGrant.Routine | DebugTreeGrant.TerminalProcesses }
        }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false}");
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        await start;

        await FeedAsync(transport, """{"seq":60,"type":"request","command":"runInTerminal","arguments":{"cwd":"/workspace","args":["tool","a b"],"argsCanBeInterpretedByShell":true}}""");
        var response = await ReadMessageAsync(transport);
        response.GetProperty("type").GetString().Should().Be("response");
        response.GetProperty("command").GetString().Should().Be("runInTerminal");
        response.GetProperty("success").GetBoolean().Should().BeFalse();
        await registry.Handle!.StopAsync(new() { Reason = "test" }, CancellationToken.None);
    }

    [Fact]
    public async Task Source_breakpoint_removal_sends_an_empty_complete_replacement()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        using var document = JsonDocument.Parse("{}");
        var start = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory(new FixedConnector(transport))).StartAsync(new()
        {
            Runtime = Runtime(manager), LaunchPlan = Plan(document.RootElement),
            BackgroundHandles = registry, InitializeFeatures = new()
        }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, "{\"supportsConfigurationDoneRequest\":false}");
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        var result = await start;
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");
        var service = new DebugBreakpointService(manager);

        var add = service.SetSourceAsync(owner, result.DebugTreeId, null,
            [new("/workspace/a.cs", 10)], CancellationToken.None).AsTask();
        var addRequest = await ReadRequestAsync(transport);
        addRequest.Command.Should().Be("setBreakpoints");
        addRequest.Arguments.GetProperty("breakpoints").GetArrayLength().Should().Be(1);
        await RespondAsync(transport, addRequest, "{\"breakpoints\":[{\"id\":1,\"verified\":true,\"line\":10}]}");
        await add;

        var snapshot = service.GetSnapshot(owner, result.DebugTreeId);
        snapshot.DebugSessionId.Should().Be(result.DebugSessionId);
        snapshot.Desired.Source.Should().ContainSingle().Which.Should().Match<DebugSourceBreakpoint>(
            x => x.Path == "/workspace/a.cs" && x.Line == 10);
        snapshot.Confirmed.Should().ContainSingle().Which.Should().Match<DebugConfirmedBreakpoint>(
            x => x.Kind == DebugBreakpointKind.Source && x.Verified && x.AdapterId == 1);

        var remove = service.SetSourceAsync(owner, result.DebugTreeId, null, [], CancellationToken.None).AsTask();
        var removeRequest = await ReadRequestAsync(transport);
        removeRequest.Arguments.GetProperty("source").GetProperty("path").GetString().Should().Be("/workspace/a.cs");
        removeRequest.Arguments.GetProperty("breakpoints").GetArrayLength().Should().Be(0);
        await RespondAsync(transport, removeRequest, "{\"breakpoints\":[]}");
        await remove;

        var tree = manager.ResolveTree(owner, result.DebugTreeId);
        tree.Breakpoints.Snapshot.Source.Should().BeEmpty();
        tree.SelectSession().ConfirmedBreakpoints.Snapshot.Should().BeEmpty();
        await registry.Handle!.StopAsync(new() { Reason = "test" }, CancellationToken.None);
    }

    [Fact]
    public async Task Phase6_advanced_surface_is_typed_bounded_opaque_and_authorized()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await using var manager = new DebugSessionManager();
        var registry = new RecordingRegistry();
        var contentStore = new HPD.Agent.InMemoryContentStore();
        using var launchDocument = JsonDocument.Parse("{\"program\":\"fixture\"}");
        var start = new DebugSessionStartOrchestrator(new DebugProtocolTransportFactory(new FixedConnector(transport)))
            .StartAsync(new()
            {
                Runtime = Runtime(manager), LaunchPlan = Plan(launchDocument.RootElement),
                BackgroundHandles = registry, InitializeFeatures = new(), ContentStore = contentStore,
                Authorization = new()
                {
                    Grants = DebugTreeGrant.Routine | DebugTreeGrant.DataBreakpoints |
                        DebugTreeGrant.Evaluate | DebugTreeGrant.MutateVariables | DebugTreeGrant.WriteMemory
                }
            }, CancellationToken.None).AsTask();
        var initialize = await ReadRequestAsync(transport);
        await RespondAsync(transport, initialize, """
            {"supportsConfigurationDoneRequest":false,"supportsModulesRequest":true,
             "supportsLoadedSourcesRequest":true,"supportsExceptionInfoRequest":true,
             "supportsBreakpointLocationsRequest":true,"supportsStepInTargetsRequest":true,
             "supportsGotoTargetsRequest":true,"supportsCompletionsRequest":true,
             "supportsSetVariable":true,"supportsSetExpression":true,
             "supportsReadMemoryRequest":true,"supportsWriteMemoryRequest":true,
             "supportsDisassembleRequest":true,"supportsTerminateThreadsRequest":true}
            """);
        var launch = await ReadRequestAsync(transport);
        await FeedAsync(transport, """{"seq":20,"type":"event","event":"initialized"}""");
        await RespondAsync(transport, launch, "{}");
        var started = await start;
        var owner = new DebugTreeLookupScope(manager.RuntimeId, "owner-session", "owner-thread");
        await FeedAsync(transport, """{"seq":21,"type":"event","event":"thread","body":{"reason":"started","threadId":1}}""");
        await FeedAsync(transport, """{"seq":22,"type":"event","event":"stopped","body":{"reason":"entry","threadId":1}}""");
        var automaticStack = await ReadRequestAsync(transport);
        await RespondAsync(transport, automaticStack,
            "{\"stackFrames\":[{\"id\":10,\"name\":\"main\",\"line\":1,\"column\":1}],\"totalFrames\":1}");
        var tree = manager.ResolveTree(owner, started.DebugTreeId);
        await WaitUntilAsync(() => tree.SelectSession().State.Status == DebugSessionStatus.Stopped);
        var session = tree.SelectSession();
        var semantics = new DebugSemanticService(manager);

        Action unsupportedSingleThread = () => semantics.ContinueAsync(owner, started.DebugTreeId, null,
            1, true, TimeSpan.FromSeconds(1), CancellationToken.None);
        unsupportedSingleThread.Should().Throw<DebugSemanticException>().Which.Reason
            .Should().Be(DebugSemanticFailureReason.CapabilityUnavailable);
        session.State.Status.Should().Be(DebugSessionStatus.Stopped,
            "option validation must happen before resumption state is mutated");

        Action unsupportedGranularity = () => semantics.NextAsync(owner, started.DebugTreeId, null,
            1, false, DebugSteppingGranularity.Line, TimeSpan.FromSeconds(1), CancellationToken.None);
        unsupportedGranularity.Should().Throw<DebugSemanticException>().Which.Reason
            .Should().Be(DebugSemanticFailureReason.CapabilityUnavailable);
        session.State.Status.Should().Be(DebugSessionStatus.Stopped);

        var modulesTask = semantics.ModulesAsync(owner, started.DebugTreeId, null, 1, null, CancellationToken.None).AsTask();
        var modulesRequest = await ReadRequestAsync(transport);
        modulesRequest.Arguments.GetProperty("moduleCount").GetInt64().Should().Be(1);
        await RespondAsync(transport, modulesRequest,
            "{\"modules\":[{\"id\":\"adapter-module\",\"name\":\"app\"}],\"totalModules\":2}");
        var modules = await modulesTask;
        modules.Modules.Single().ModuleToken.Should().NotContain("adapter-module");
        modules.ContinuationToken.Should().NotBeNull();

        using var adapterData = JsonDocument.Parse("{\"secret\":7}");
        var sourceToken = session.Projections.CreateSourceToken(1, 10, new()
        {
            Path = "/workspace/a.cs", SourceReference = 9, AdapterData = adapterData.RootElement.Clone()
        });
        var sourceTask = semantics.SourceAsync(owner, started.DebugTreeId, null, sourceToken, CancellationToken.None).AsTask();
        var sourceRequest = await ReadRequestAsync(transport);
        sourceRequest.Arguments.GetProperty("sourceReference").GetInt32().Should().Be(9);
        sourceRequest.Arguments.GetProperty("source").GetProperty("adapterData").GetProperty("secret").GetInt32().Should().Be(7);
        await RespondAsync(transport, sourceRequest, "{\"content\":\"source text\",\"mimeType\":\"text/plain\"}");
        (await sourceTask).InlineContent.Should().Be("source text");

        var exceptionTask = semantics.ExceptionInfoAsync(owner, started.DebugTreeId, null, 1, CancellationToken.None).AsTask();
        var exceptionRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, exceptionRequest,
            "{\"exceptionId\":\"E\",\"breakMode\":\"future-mode\",\"details\":{\"message\":\"safe\"}}");
        var exception = await exceptionTask;
        exception.BreakMode.Should().Be("future-mode");
        exception.Details!.Message.Should().Be("safe");

        var hostileExceptionTask = semantics.ExceptionInfoAsync(owner, started.DebugTreeId, null,
            1, CancellationToken.None).AsTask();
        var hostileExceptionRequest = await ReadRequestAsync(transport);
        var oversizedExceptionText = JsonSerializer.Serialize(new string('e', 70 * 1024));
        await RespondAsync(transport, hostileExceptionRequest,
            $"{{\"exceptionId\":\"E\",\"breakMode\":\"always\",\"details\":{{\"message\":{oversizedExceptionText}}}}}");
        var hostileException = await hostileExceptionTask;
        hostileException.Truncated.Should().BeTrue();
        Encoding.UTF8.GetByteCount(hostileException.Details!.Message!).Should().BeLessThanOrEqualTo(64 * 1024);

        var nestedExceptionTask = semantics.ExceptionInfoAsync(owner, started.DebugTreeId, null,
            1, CancellationToken.None).AsTask();
        var nestedExceptionRequest = await ReadRequestAsync(transport);
        var nestedDetails = "{\"message\":\"leaf\"}";
        for (var depth = 0; depth < 12; depth++)
            nestedDetails = $"{{\"message\":\"level-{depth}\",\"innerException\":[{nestedDetails}]}}";
        await RespondAsync(transport, nestedExceptionRequest,
            $"{{\"exceptionId\":\"Nested\",\"breakMode\":\"always\",\"details\":{nestedDetails}}}");
        var nestedException = await nestedExceptionTask;
        nestedException.Truncated.Should().BeTrue();
        var observedDepth = 0;
        for (var details = nestedException.Details; details?.InnerExceptions.Count > 0;
             details = details.InnerExceptions[0])
            observedDepth++;
        observedDepth.Should().BeLessThanOrEqualTo(8);

        var largeSourceTask = semantics.SourceAsync(owner, started.DebugTreeId, null,
            sourceToken, CancellationToken.None).AsTask();
        var largeSourceRequest = await ReadRequestAsync(transport);
        var largeSourceText = new string('s', 70 * 1024);
        await RespondAsync(transport, largeSourceRequest,
            $"{{\"content\":{JsonSerializer.Serialize(largeSourceText)},\"mimeType\":\"text/plain\"}}");
        var largeSource = await largeSourceTask;
        largeSource.Truncated.Should().BeTrue();
        largeSource.ArtifactStatus.Should().Be(DebugArtifactWriteStatus.Stored);
        largeSource.ContentAddress.Should().NotBeNull();
        Encoding.UTF8.GetByteCount(largeSource.InlineContent).Should().BeLessThanOrEqualTo(64 * 1024);

        var largeEvaluationTask = semantics.EvaluateAsync(owner, started.DebugTreeId, null,
            "largeValue", frameToken: null, context: "watch", CancellationToken.None).AsTask();
        var largeEvaluationRequest = await ReadRequestAsync(transport);
        var largeEvaluationText = new string('v', 70 * 1024);
        await RespondAsync(transport, largeEvaluationRequest,
            $"{{\"result\":{JsonSerializer.Serialize(largeEvaluationText)},\"variablesReference\":0}}");
        var largeEvaluation = await largeEvaluationTask;
        largeEvaluation.Truncated.Should().BeTrue();
        largeEvaluation.ArtifactStatus.Should().Be(DebugArtifactWriteStatus.Stored);
        largeEvaluation.ContentAddress.Should().NotBeNull();

        var frameToken = session.Projections.CreateSuspensionToken(1, 10, "frame", 10);
        var targetsTask = semantics.StepInTargetsAsync(owner, started.DebugTreeId, null, frameToken, CancellationToken.None).AsTask();
        var targetsRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, targetsRequest, "{\"targets\":[{\"id\":77,\"label\":\"callee\"}]}");
        var target = (await targetsTask).Single();
        target.TargetToken.Should().NotBe("77");
        session.Projections.ResolveSuspensionToken(target.TargetToken, "stepInTarget", out _, out _).Should().Be(77);

        var locationsTask = semantics.BreakpointLocationsAsync(owner, started.DebugTreeId, null,
            sourceToken, 1, 1, null, null, CancellationToken.None).AsTask();
        var locationsRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, locationsRequest, "{\"breakpoints\":[{\"line\":2,\"column\":3}]}");
        (await locationsTask).Single().Line.Should().Be(2);

        var gotoTargetsTask = semantics.GotoTargetsAsync(owner, started.DebugTreeId, null,
            1, sourceToken, 1, 1, CancellationToken.None).AsTask();
        var gotoTargetsRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, gotoTargetsRequest, "{\"targets\":[{\"id\":88,\"label\":\"destination\",\"line\":4}]}");
        var gotoTarget = (await gotoTargetsTask).Single();
        gotoTarget.TargetToken.Should().NotBe("88");

        var completionsTask = semantics.CompletionsAsync(owner, started.DebugTreeId, null,
            "cou", 3, 1, frameToken, 1, null, CancellationToken.None).AsTask();
        var completionsRequest = await ReadRequestAsync(transport);
        completionsRequest.Arguments.GetProperty("frameId").GetInt32().Should().Be(10);
        await RespondAsync(transport, completionsRequest,
            "{\"targets\":[{\"label\":\"count\"},{\"label\":\"counter\"}]}");
        var completions = await completionsTask;
        completions.Items.Single().Label.Should().Be("count");
        completions.ContinuationToken.Should().NotBeNull();

        var locationToken = session.Projections.CreateSuspensionToken(1, 10, "location", 99);
        var resolveLocationTask = semantics.ResolveLocationAsync(owner, started.DebugTreeId, null,
            locationToken, CancellationToken.None).AsTask();
        var resolveLocationRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, resolveLocationRequest,
            "{\"source\":{\"path\":\"/workspace/a.cs\",\"sourceReference\":9},\"line\":5,\"column\":2}");
        var resolvedLocation = await resolveLocationTask;
        resolvedLocation.Source.SourceToken.Should().NotContain("/workspace/a.cs");

        var variablesToken = session.Projections.CreateSuspensionToken(1, 10, "variables", 44);
        var wrongProof = DebugPrivilegedOperationAuthorization.Create(tree, session, DebugPrivilegedOperation.SetExpression);
        var deniedMutation = async () => await semantics.SetVariableAsync(owner, started.DebugTreeId, null,
            variablesToken, "counter", "2", wrongProof, CancellationToken.None);
        (await deniedMutation.Should().ThrowAsync<DebugSemanticException>()).Which.Reason
            .Should().Be(DebugSemanticFailureReason.PermissionDenied);
        var variableProof = DebugPrivilegedOperationAuthorization.Create(tree, session, DebugPrivilegedOperation.SetVariable);
        var setVariableTask = semantics.SetVariableAsync(owner, started.DebugTreeId, null,
            variablesToken, "counter", "2", variableProof, CancellationToken.None).AsTask();
        var setVariableRequest = await ReadRequestAsync(transport);
        setVariableRequest.Arguments.GetProperty("variablesReference").GetInt32().Should().Be(44);
        await RespondAsync(transport, setVariableRequest,
            "{\"value\":\"2\",\"variablesReference\":45,\"memoryReference\":\"mem-secret\"}");
        var mutation = await setVariableTask;
        mutation.MemoryReferenceToken.Should().NotBe("mem-secret");

        var expressionProof = DebugPrivilegedOperationAuthorization.Create(tree, session, DebugPrivilegedOperation.SetExpression);
        var setExpressionTask = semantics.SetExpressionAsync(owner, started.DebugTreeId, null,
            "counter", "3", frameToken, expressionProof, CancellationToken.None).AsTask();
        var setExpressionRequest = await ReadRequestAsync(transport);
        setExpressionRequest.Arguments.GetProperty("expression").GetString().Should().Be("counter");
        await RespondAsync(transport, setExpressionRequest, "{\"value\":\"3\"}");
        (await setExpressionTask).Value.Should().Be("3");

        var memoryToken = session.Projections.CreateSuspensionTextToken(1, 10, "memory", "mem-secret");
        var oversizedWrite = async () => await semantics.WriteMemoryAsync(owner, started.DebugTreeId, null,
            memoryToken, 0, new byte[4097], false,
            DebugPrivilegedOperationAuthorization.Create(tree, session, DebugPrivilegedOperation.WriteMemory),
            CancellationToken.None);
        (await oversizedWrite.Should().ThrowAsync<DebugSemanticException>()).Which.Reason
            .Should().Be(DebugSemanticFailureReason.InvalidArguments);
        var readTask = semantics.ReadMemoryAsync(owner, started.DebugTreeId, null,
            memoryToken, 0, 4, CancellationToken.None).AsTask();
        var readRequest = await ReadRequestAsync(transport);
        readRequest.Arguments.GetProperty("memoryReference").GetString().Should().Be("mem-secret");
        await RespondAsync(transport, readRequest, "{\"address\":\"0x1\",\"data\":\"AQIDBA==\"}");
        (await readTask).Bytes.Should().Equal(1, 2, 3, 4);

        var malformedReadTask = semantics.ReadMemoryAsync(owner, started.DebugTreeId, null,
            memoryToken, 0, 4, CancellationToken.None).AsTask();
        var malformedReadRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, malformedReadRequest,
            "{\"address\":\"0x1\",\"data\":\"not-base64!\"}");
        var malformedRead = async () => await malformedReadTask;
        (await malformedRead.Should().ThrowAsync<DebugSemanticException>()).Which.Reason
            .Should().Be(DebugSemanticFailureReason.AdapterRequestFailed);

        var contradictoryReadTask = semantics.ReadMemoryAsync(owner, started.DebugTreeId, null,
            memoryToken, 0, 4, CancellationToken.None).AsTask();
        var contradictoryReadRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, contradictoryReadRequest,
            "{\"address\":\"0x1\",\"data\":\"AQIDBA==\",\"unreadableBytes\":1}");
        var contradictoryRead = async () => await contradictoryReadTask;
        (await contradictoryRead.Should().ThrowAsync<DebugSemanticException>()).Which.Reason
            .Should().Be(DebugSemanticFailureReason.AdapterRequestFailed);

        var overflowRead = async () => await semantics.ReadMemoryAsync(owner, started.DebugTreeId,
            null, memoryToken, long.MaxValue, 4, CancellationToken.None);
        (await overflowRead.Should().ThrowAsync<DebugSemanticException>()).Which.Reason
            .Should().Be(DebugSemanticFailureReason.InvalidArguments);

        var writeProof = DebugPrivilegedOperationAuthorization.Create(tree, session, DebugPrivilegedOperation.WriteMemory);
        var writeTask = semantics.WriteMemoryAsync(owner, started.DebugTreeId, null,
            memoryToken, 0, new byte[] { 5, 6 }, false, writeProof, CancellationToken.None).AsTask();
        var writeRequest = await ReadRequestAsync(transport);
        writeRequest.Arguments.GetProperty("data").GetString().Should().Be("BQY=");
        await RespondAsync(transport, writeRequest, "{\"bytesWritten\":2,\"offset\":0}");
        (await writeTask).BytesWritten.Should().Be(2);

        var invalidWriteTask = semantics.WriteMemoryAsync(owner, started.DebugTreeId, null,
            memoryToken, 0, new byte[] { 7, 8 }, false,
            DebugPrivilegedOperationAuthorization.Create(tree, session, DebugPrivilegedOperation.WriteMemory),
            CancellationToken.None).AsTask();
        var invalidWriteRequest = await ReadRequestAsync(transport);
        await RespondAsync(transport, invalidWriteRequest, "{\"bytesWritten\":3,\"offset\":0}");
        var invalidWrite = async () => await invalidWriteTask;
        (await invalidWrite.Should().ThrowAsync<DebugSemanticException>()).Which.Reason
            .Should().Be(DebugSemanticFailureReason.AdapterRequestFailed);

        var disassemblyTask = semantics.DisassembleAsync(owner, started.DebugTreeId, null,
            memoryToken, 0, 0, 1, true, null, CancellationToken.None).AsTask();
        var disassemblyRequest = await ReadRequestAsync(transport);
        disassemblyRequest.Arguments.GetProperty("memoryReference").GetString().Should().Be("mem-secret");
        await RespondAsync(transport, disassemblyRequest,
            "{\"instructions\":[{\"address\":\"0x1\",\"instruction\":\"nop\"}]}");
        var instruction = (await disassemblyTask).Instructions.Single();
        instruction.InstructionReferenceToken.Should().NotBe("0x1");
        session.Projections.ResolveTextToken(instruction.InstructionReferenceToken, "instruction",
            out var instructionThread, out var instructionFrame).Should().Be("0x1");
        instructionThread.Should().Be(1);
        instructionFrame.Should().Be(10);

        var terminateTask = semantics.TerminateThreadsAsync(owner, started.DebugTreeId, null,
            [1, 1], CancellationToken.None).AsTask();
        var terminateRequest = await ReadRequestAsync(transport);
        terminateRequest.Arguments.GetProperty("threadIds").GetArrayLength().Should().Be(1);
        await RespondAsync(transport, terminateRequest, "{}");
        await terminateTask;

        IDebugAdapterExtensionHost extensionHost = new DebugAdapterExtensionRegistry(
            [new FixtureHostExtension(), new FixtureMutatingHostExtension()]);
        var extensionTask = extensionHost.InvokeAsync<DapNoArguments, DapNoBody>(
            FunctionContext(manager), started.DebugTreeId, null, "fixture.inspect", new(),
            CancellationToken.None).AsTask();
        var extensionRequest = await ReadRequestAsync(transport);
        extensionRequest.Command.Should().Be("fixture.inspect");
        await RespondAsync(transport, extensionRequest, "{}");
        await extensionTask;
        var mutatingExtension = async () => await extensionHost.InvokeAsync<DapNoArguments, DapNoBody>(
            FunctionContext(manager), started.DebugTreeId, null, "fixture.mutate", new(),
            CancellationToken.None);
        (await mutatingExtension.Should().ThrowAsync<DebugSemanticException>()).Which.Reason
            .Should().Be(DebugSemanticFailureReason.PermissionDenied);

        await FeedAsync(transport,
            "{\"seq\":90,\"type\":\"event\",\"event\":\"capabilities\",\"body\":{\"capabilities\":{\"supportsReadMemoryRequest\":false}}}");
        await WaitUntilAsync(() => session.Capabilities?.SupportsReadMemoryRequest == false);
        var removedCapability = async () => await semantics.ReadMemoryAsync(owner, started.DebugTreeId,
            null, memoryToken, 0, 1, CancellationToken.None);
        (await removedCapability.Should().ThrowAsync<DebugSemanticException>()).Which.Reason
            .Should().Be(DebugSemanticFailureReason.CapabilityUnavailable);

        await FeedAsync(transport,
            "{\"seq\":91,\"type\":\"event\",\"event\":\"continued\",\"body\":{\"threadId\":1,\"allThreadsContinued\":false}}");
        await WaitUntilAsync(() => session.State.Status == DebugSessionStatus.Running);
        var runningException = async () => await semantics.ExceptionInfoAsync(owner,
            started.DebugTreeId, null, 1, CancellationToken.None);
        (await runningException.Should().ThrowAsync<DebugSemanticException>()).Which.Reason
            .Should().Be(DebugSemanticFailureReason.InvalidSessionState);
        var staleSource = () => session.Projections.ResolveSourceToken(sourceToken);
        staleSource.Should().Throw<DebugSemanticException>().Which.Reason
            .Should().Be(DebugSemanticFailureReason.ReferenceExpired);

        await registry.Handle!.StopAsync(new() { Reason = "test" }, CancellationToken.None);
    }

    private static DebugRuntimeBinding Runtime(DebugSessionManager manager) => new()
    {
        AgentRuntimeRegistrationId = manager.RuntimeId,
        SessionId = "owner-session",
        ThreadId = "owner-thread",
        SessionManager = manager,
        EventScope = new(null, "owner-session", "owner-thread"),
        State = new()
    };

    private static FunctionExecutionContext FunctionContext(
        DebugSessionManager manager,
        AIFunction? suppliedFunction = null,
        IServiceProvider? services = null,
        RuntimeProcessExecutionBinding? processExecution = null,
        IAgentBackgroundHandleRegistry? backgroundHandles = null,
        string? workspaceRootOverride = null,
        string? permissionAction = null)
    {
        var function = suppliedFunction ?? AIFunctionFactory.Create(() => "ok", new AIFunctionFactoryOptions
            { Name = "debug_host", Description = "trusted host test" });
        var state = AgentLoopState.InitialSafe([], "run", "conversation", "agent");
        if (permissionAction is not null)
        {
            var permission = new DebugPermissionStateData().WithDecision(
                "call", permissionAction,
                DebugPermissionMiddleware.Classify(permissionAction));
            state = state with
            {
                MiddlewareState = state.MiddlewareState.SetState(
                    typeof(DebugPermissionStateData).FullName!,
                    permission)
            };
        }
        var session = new Session("owner-session");
        var thread = new Thread("owner-session", "agent") { Id = "owner-thread" };
        var coordinator = new EventCoordinator();
        var workspaceRoot = Path.GetFullPath(
            workspaceRootOverride ?? Directory.GetCurrentDirectory());
        var runConfig = new AgentRunConfig
        {
            ContextOverrides = new Dictionary<string, object>
            {
                [AgentWorkspace.ContextKey] = new AgentWorkspace(
                    "root", workspaceRoot, [new AgentWorkspaceRoot("root", workspaceRoot)])
            }
        };
        var agent = new AgentContext("agent", "conversation", state, coordinator, session, thread,
            CancellationToken.None, services: services);
        agent.RuntimeCapabilities.Set<IDebugSessionManager>(manager);
        agent.RuntimeCapabilities.Set(new DebugRuntimeBindingState());
        if (processExecution is not null)
            agent.RuntimeCapabilities.Set(processExecution);
        var before = agent.AsBeforeFunction(function, "call", new Dictionary<string, object?>(),
            runConfig, null, null);
        return new FunctionExecutionContext(before, new FunctionRequest
        {
            Function = function,
            CallId = "call",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            RunConfig = runConfig,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = coordinator,
            BackgroundHandles = backgroundHandles
        });
    }

    private static DebugAdapterLaunchPlan Plan(JsonElement arguments) => new()
    {
        AdapterId = "fixture",
        EnvironmentId = "env",
        EnvironmentRevision = 1,
        PolicyRevision = 1,
        EndpointCatalogRevision = 1,
        PackageProvenance = new() { PackageId = "fixture", PackageVersion = "1", AssemblyName = "fixture" },
        TrustDecision = new() { TrustLevel = DebugAdapterTrustLevel.Trusted, PolicyRevision = "1", ReasonCode = "TEST" },
        CanonicalWorkingDirectory = "/workspace",
        AuthorizationScope = "debug.adapter.launch",
        FilteredEnvironment = new Dictionary<string, string?>(),
        Transport = new()
        {
            Kind = DebugAdapterTransportKind.ApprovedTcpConnect,
            Command = "",
            EndpointId = "endpoint",
            AuthorizedAddress = "loopback:1",
            AuthorityReference = "authority"
        },
        Arguments = arguments.Clone()
    };

    private static async Task<Request> ReadRequestAsync(InMemoryDebugProtocolTransport transport)
    {
        await foreach (var bytes in transport.ReadWrittenAsync().WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token))
        {
            var frame = new DebugProtocolFramer().Append(bytes).Single();
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            return new(root.GetProperty("seq").GetInt32(), root.GetProperty("command").GetString()!, root.GetProperty("arguments").Clone());
        }
        throw new InvalidOperationException();
    }

    private static async Task<JsonElement> ReadMessageAsync(InMemoryDebugProtocolTransport transport)
    {
        await foreach (var bytes in transport.ReadWrittenAsync().WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token))
        {
            var frame = new DebugProtocolFramer().Append(bytes).Single();
            using var document = JsonDocument.Parse(frame);
            return document.RootElement.Clone();
        }
        throw new InvalidOperationException();
    }

    private static ValueTask RespondAsync(InMemoryDebugProtocolTransport transport, Request request, string body)
        => FeedAsync(transport, $"{{\"seq\":99,\"type\":\"response\",\"request_seq\":{request.Sequence},\"success\":true,\"command\":\"{request.Command}\",\"body\":{body}}}");

    private static ValueTask FeedAsync(InMemoryDebugProtocolTransport transport, string json)
        => transport.FeedProtocolAsync(DebugProtocolFramer.Encode(Encoding.UTF8.GetBytes(json)));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class FixtureHostExtension : DebugAdapterExtension<DapNoArguments, DapNoBody>
    {
        public override string AdapterId => "fixture";
        public override string Command => "fixture.inspect";
        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo<DapNoArguments> RequestTypeInfo
            => DapJsonContext.Default.DapNoArguments;
        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo<DapNoBody> ResponseTypeInfo
            => DapJsonContext.Default.DapNoBody;
    }

    private sealed class FixtureMutatingHostExtension : DebugAdapterExtension<DapNoArguments, DapNoBody>
    {
        public override string AdapterId => "fixture";
        public override string Command => "fixture.mutate";
        public override bool RequiresPrivilegedAuthorization => true;
        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo<DapNoArguments> RequestTypeInfo
            => DapJsonContext.Default.DapNoArguments;
        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo<DapNoBody> ResponseTypeInfo
            => DapJsonContext.Default.DapNoBody;
    }

    private sealed record Request(int Sequence, string Command, JsonElement Arguments);

    private sealed class FixedConnector(InMemoryDebugProtocolTransport transport) : IDebugApprovedTransportConnector
    {
        public ValueTask<IDebugProtocolTransport> ConnectAsync(DebugAdapterTransportPlan authorizedPlan, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IDebugProtocolTransport>(transport);
        public ValueTask<IDebugProtocolTransport> ConnectEnvironmentServerAsync(DebugAdapterTransportPlan authorizedPlan, IDebugProtocolTransport startedProcess, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class QueueConnector(params InMemoryDebugProtocolTransport[] transports) : IDebugApprovedTransportConnector
    {
        private readonly Queue<InMemoryDebugProtocolTransport> _transports = new(transports);
        public int ConnectCount { get; private set; }
        public ValueTask<IDebugProtocolTransport> ConnectAsync(DebugAdapterTransportPlan authorizedPlan, CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            return ValueTask.FromResult<IDebugProtocolTransport>(_transports.Dequeue());
        }
        public ValueTask<IDebugProtocolTransport> ConnectEnvironmentServerAsync(DebugAdapterTransportPlan authorizedPlan, IDebugProtocolTransport startedProcess, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RealProcessConnector(
        string command,
        IReadOnlyList<string> arguments) : IDebugApprovedTransportConnector
    {
        public ValueTask<IDebugProtocolTransport> ConnectAsync(
            DebugAdapterTransportPlan authorizedPlan,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IDebugProtocolTransport>(
                DebugRealAdapterQualificationTests.ProcessDebugProtocolTransport.Start(
                    command, arguments.ToArray()));

        public ValueTask<IDebugProtocolTransport> ConnectEnvironmentServerAsync(
            DebugAdapterTransportPlan authorizedPlan,
            IDebugProtocolTransport startedProcess,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static DebugAdapterDescriptor DebugpyDescriptor() => new()
    {
        Id = "debugpy",
        Languages = ["python"],
        FileExtensions = [".py"],
        RootMarkers = ["pyproject.toml"],
        TargetKinds = DebugTargetKind.SourceFile | DebugTargetKind.Process,
        Provenance = new()
        {
            PackageId = "debugpy",
            PackageVersion = "qualification",
            AssemblyName = "qualification"
        }
    };

    private static DebugAdapterDescriptor NetcoredbgDescriptor() => new()
    {
        Id = "netcoredbg",
        Languages = ["csharp", "fsharp", "visualbasic"],
        FileExtensions = [".dll", ".cs", ".fs", ".vb"],
        RootMarkers = [".sln", ".csproj"],
        TargetKinds = DebugTargetKind.Executable | DebugTargetKind.ProjectDirectory |
            DebugTargetKind.Process,
        Provenance = new()
        {
            PackageId = "netcoredbg",
            PackageVersion = "qualification",
            AssemblyName = "qualification"
        }
    };

    private sealed class PlannerCatalogProvider(
        DebugAdapterDescriptor descriptor,
        IDebugAdapterFactory factory) : IDebugAdapterCatalogProvider
    {
        public IEnumerable<DebugAdapterCatalogEntry> GetEntries()
        {
            yield return new()
            {
                Descriptor = descriptor,
                FactoryResolver = _ => factory
            };
        }
    }

    private sealed class PlannerTrustPolicy : IDebugAdapterTrustPolicy
    {
        public DebugAdapterTrustDecision Evaluate(DebugAdapterDescriptor descriptor) => new()
        {
            TrustLevel = DebugAdapterTrustLevel.Trusted,
            PolicyRevision = "planner-test",
            ReasonCode = "TEST_TRUSTED"
        };
    }

    private sealed class PlannerFixtureFactory : IDebugAdapterFactory
    {
        public ValueTask<DebugAdapterAvailability> ProbeAsync(
            DebugAdapterDescriptor descriptor,
            DebugAdapterResolutionContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DebugAdapterAvailability(
                DebugAdapterAvailabilityKind.Available, "1"));

        public ValueTask<DebugAdapterLaunchPlan> CreateLaunchPlanAsync(
            DebugAdapterDescriptor descriptor,
            DebugLaunchContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(PlannerPlan(descriptor, context.Resolution, context.Configuration));

        public ValueTask<DebugAdapterLaunchPlan> CreateAttachPlanAsync(
            DebugAdapterDescriptor descriptor,
            DebugAttachContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(PlannerPlan(descriptor, context.Resolution, context.Configuration));

        private static DebugAdapterLaunchPlan PlannerPlan(
            DebugAdapterDescriptor descriptor,
            DebugAdapterResolutionContext resolution,
            JsonElement configuration) => new()
        {
            AdapterId = descriptor.Id,
            EnvironmentId = resolution.EnvironmentId,
            EnvironmentRevision = resolution.EnvironmentRevision,
            PolicyRevision = resolution.PolicyRevision,
            EndpointCatalogRevision = resolution.EndpointCatalogRevision,
            PackageProvenance = descriptor.Provenance,
            TrustDecision = resolution.TrustDecision,
            ProcessExecution = resolution.ProcessExecution,
            ExecutionTarget = resolution.ProcessExecution?.ExecutionTarget,
            CanonicalWorkingDirectory = resolution.WorkspaceRoot,
            AuthorizationScope = resolution.AuthorizationScope,
            FilteredEnvironment = resolution.FilteredEnvironment,
            Transport = new()
            {
                Kind = DebugAdapterTransportKind.ApprovedTcpConnect,
                Command = string.Empty,
                EndpointId = "planner-test",
                AuthorizedAddress = "loopback:1",
                AuthorityReference = "planner-test"
            },
            Arguments = configuration.Clone()
        };
    }

    private static RuntimeProcessExecutionBinding PlannerExecution() => new()
    {
        EnvironmentId = "planner-environment",
        EnvironmentRevision = 1,
        ProcessProvider = new PlannerProcessProvider(),
        ExecutionTarget = new TargetHandle<ExecutionUnit>(
            new TargetRoute
            {
                Kind = new TargetKind("planner.execution"),
                Scope = new ResourceScope("planner")
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Control | TargetHandleAuthority.Observe)
    };

    private sealed class PlannerProcessProvider : IProcessProvider
    {
        public ProviderId ProviderId => new("planner.process");
        public ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ChildPlanFactory : IDebugChildSessionPlanFactory
    {
        public ValueTask<DebugChildSessionPlan> CreateAsync(
            DebugRuntimeBinding runtime,
            DebugTreeAuthorization authorization,
            DebugAdapterLaunchPlan parentPlan,
            string request,
            JsonElement configuration,
            string? outputPresentation,
            DebugDesiredBreakpointSnapshot desiredBreakpoints,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new DebugChildSessionPlan
            {
                LaunchPlan = parentPlan with { Arguments = configuration.Clone() },
                IsAttach = request == "attach",
                Breakpoints = desiredBreakpoints
            });
    }

    private sealed class RecordingHostRequestBroker : IDebugHostRequestBroker
    {
        public List<DebugRunInTerminalRequestEvent> Requests { get; } = [];

        public ValueTask<DebugRunInTerminalResponseEvent> RequestRunInTerminalAsync(
            DebugEventScope scope,
            string debugTreeId,
            string debugSessionId,
            string? terminalKind,
            string? title,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string?> environmentDelta,
            bool argsCanBeInterpretedByShell,
            CancellationToken cancellationToken)
        {
            var requestId = Guid.NewGuid().ToString("N");
            Requests.Add(new DebugRunInTerminalRequestEvent
            {
                DebugRequestId = requestId,
                DebugTreeId = debugTreeId,
                DebugSessionId = debugSessionId,
                TerminalKind = terminalKind,
                Title = title,
                WorkingDirectory = workingDirectory,
                Arguments = arguments,
                EnvironmentDelta = environmentDelta,
                ArgsCanBeInterpretedByShell = argsCanBeInterpretedByShell,
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId,
                TraceId = scope.TraceId
            });
            return ValueTask.FromResult(new DebugRunInTerminalResponseEvent
            {
                DebugRequestId = requestId,
                ProcessId = 4242,
                ShellProcessId = 4241,
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId,
                TraceId = scope.TraceId
            });
        }

        public ValueTask<RespondResult> RespondAsync(
            DebugRunInTerminalResponseEvent response,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<RespondResult>(default!);
    }

    private sealed class ThrowingChildPlanFactory : IDebugChildSessionPlanFactory
    {
        public ValueTask<DebugChildSessionPlan> CreateAsync(
            DebugRuntimeBinding runtime, DebugTreeAuthorization authorization,
            DebugAdapterLaunchPlan parentPlan, string request, JsonElement configuration,
            string? outputPresentation, DebugDesiredBreakpointSnapshot desiredBreakpoints,
            CancellationToken cancellationToken)
            => ValueTask.FromException<DebugChildSessionPlan>(new InvalidOperationException("invalid child configuration"));
    }

    private sealed class RecordingRegistry : IAgentBackgroundHandleRegistry
    {
        public int RegisterCount { get; private set; }
        public DebugSessionHandle? Handle { get; private set; }
        public ValueTask<BackgroundHandleRegistration> RegisterHandleAsync(BackgroundHandleDescriptor descriptor, IBackgroundHandle handle, CancellationToken cancellationToken = default)
        {
            RegisterCount++;
            Handle = (DebugSessionHandle)handle;
            return ValueTask.FromResult(new BackgroundHandleRegistration(descriptor.HandleId!, descriptor.Name, descriptor.Kind, descriptor.SourceKind));
        }
        public bool TryGetHandle(string handleId, BackgroundHandleScope scope, out RegisteredBackgroundHandle handle) { handle = null!; return false; }
        public IReadOnlyList<RegisteredBackgroundHandle> ListHandles(BackgroundHandleQuery query) => [];
    }

    private sealed class ThrowingRegistry : IAgentBackgroundHandleRegistry
    {
        public ValueTask<BackgroundHandleRegistration> RegisterHandleAsync(BackgroundHandleDescriptor descriptor, IBackgroundHandle handle, CancellationToken cancellationToken = default)
            => ValueTask.FromException<BackgroundHandleRegistration>(new InvalidOperationException("registration failed"));
        public bool TryGetHandle(string handleId, BackgroundHandleScope scope, out RegisteredBackgroundHandle handle) { handle = null!; return false; }
        public IReadOnlyList<RegisteredBackgroundHandle> ListHandles(BackgroundHandleQuery query) => [];
    }

    private sealed class RecordingEventPublisher : IDebugLifecycleEventPublisher
    {
        public List<AgentEvent> Events { get; } = [];
        public ValueTask PublishAsync(AgentEvent @event, bool durable, CancellationToken cancellationToken = default)
        {
            Events.Add(@event);
            return ValueTask.CompletedTask;
        }
    }
}
