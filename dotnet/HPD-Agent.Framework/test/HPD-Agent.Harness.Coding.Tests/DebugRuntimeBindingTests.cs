using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Security;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Adapters;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Environment.Contracts;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugRuntimeBindingTests
{
    [Fact]
    public void Capture_retains_only_runtime_owned_services_and_opaque_scope()
    {
        var process = new ProbeProcessProvider();
        var manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
        var context = CreateContext(manager, Execution(process));

        var binding = DebugRuntimeBinding.Capture(context, requireProcessExecution: true);

        binding.AgentRuntimeRegistrationId.Should().Be(manager.RuntimeId);
        binding.SessionId.Should().Be("session-1");
        binding.ThreadId.Should().Be("thread-1");
        binding.ProcessExecution!.ProcessProvider.Should().BeSameAs(process);
        binding.State.IsAvailable.Should().BeTrue();
        binding.State.Invalidate("ENVIRONMENT_LOST").Should().BeTrue();
        binding.State.Invalidate("SECOND_REASON").Should().BeFalse();
        binding.State.ReasonCode.Should().Be("ENVIRONMENT_LOST");
    }

    [Fact]
    public void Capture_fails_closed_without_an_authorized_execution_target()
    {
        var context = CreateContext(new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions())), processExecution: null);

        var action = () => DebugRuntimeBinding.Capture(context, requireProcessExecution: true);

        action.Should().Throw<InvalidOperationException>().WithMessage("*authorized process execution binding*");
    }

    [Fact]
    public void Capture_retains_the_invocation_wide_disabled_process_sandbox()
    {
        var runConfig = new AgentRunConfig
        {
            Security = new AgentSecurityRunConfig
            {
                Sandbox = new AgentSandboxRunConfig { Mode = AgentSandboxPolicy.Disabled }
            }
        };
        var context = CreateContext(
            new DebugSessionManager(new DebugTerminalRecordStore(
                new DebugTerminalRecordStoreOptions())),
            Execution(new ProbeProcessProvider()),
            runConfig);

        var binding = DebugRuntimeBinding.Capture(context, requireProcessExecution: true);

        binding.ProcessSandbox.IsEnforced.Should().BeFalse();
    }

    [Fact]
    public void Capture_translates_every_workspace_root_into_explicit_sandbox_grants()
    {
        var container = Path.Combine(
            Path.GetTempPath(),
            "hpd-runtime-roots-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(container, "a");
        var second = Path.Combine(container, "b");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var runConfig = new AgentRunConfig
        {
            Context = new AgentContextRunConfig { Properties = new Dictionary<string, object>
            {
                [AgentWorkspace.ContextKey] = new AgentWorkspace(
                    "a",
                    first,
                    [
                        new AgentWorkspaceRoot("a", first),
                        new AgentWorkspaceRoot("b", second)
                    ])
            } }
        };
        var context = CreateContext(
            new DebugSessionManager(new DebugTerminalRecordStore(
                new DebugTerminalRecordStoreOptions())),
            Execution(new ProbeProcessProvider()),
            runConfig);

        var binding = DebugRuntimeBinding.Capture(context, requireProcessExecution: true);

        foreach (var root in new[] { first, second })
        {
            binding.ProcessSandbox.Filesystem.Should().Contain(grant =>
                grant.Path == root && grant.Access == AgentSandboxPathAccess.Read);
            binding.ProcessSandbox.Filesystem.Should().Contain(grant =>
                grant.Path == root && grant.Access == AgentSandboxPathAccess.Write);
        }
    }

    [Fact]
    public async Task Environment_tool_probe_is_direct_bounded_shell_free_and_trust_gated()
    {
        var process = new ProbeProcessProvider();
        var resolver = new EnvironmentDebugAdapterToolResolver();
        var descriptor = Descriptor();
        var denied = Resolution(Execution(process), DebugAdapterTrustLevel.Denied);

        var deniedResult = await resolver.ResolveAsync(descriptor, denied);
        var trustedResult = await resolver.ResolveAsync(descriptor, Resolution(Execution(process), DebugAdapterTrustLevel.Trusted));

        deniedResult.SafeReasonCode.Should().Be("ADAPTER_PACKAGE_NOT_TRUSTED");
        process.RunCount.Should().Be(1);
        trustedResult.Available.Should().BeTrue();
        trustedResult.Version.Should().Be("fixture 1.2.3");
        trustedResult.SearchScope.Should().Be(DebugAdapterToolSearchScope.GlobalCommand);
        trustedResult.ProcessProviderId.Should().Be("test.process");
        process.LastSpec!.Command.FileName.Should().Be("fixture-adapter");
        process.LastSpec.Command.Arguments.Should().Equal("--version");
        process.LastSpec.Policy.Timeout.Should().Be(TimeSpan.FromSeconds(3));
        process.LastSpec.Isolation.Network.Mode.Should().Be(NetworkEgressMode.Blocked);
        process.LastSpec.Io.StandardInput.Kind.Should().Be(ProcessInputKind.None);
    }

    [Fact]
    public async Task Debugpy_probe_imports_the_adapter_instead_of_assuming_version_switch_support()
    {
        var process = new ProbeProcessProvider();
        var descriptor = Descriptor() with { Id = "debugpy", CommandHints = ["python"] };

        var result = await new EnvironmentDebugAdapterToolResolver().ResolveAsync(
            descriptor,
            Resolution(Execution(process), DebugAdapterTrustLevel.Trusted));

        result.Available.Should().BeTrue();
        process.LastSpec!.Command.Arguments.Should().Equal(
            "-c",
            "import debugpy; import debugpy.adapter; print(debugpy.__version__)");
    }

    [Fact]
    public async Task Environment_policy_violation_is_reported_as_denied_not_missing()
    {
        var process = new ProbeProcessProvider
        {
            Violations = [new ProcessViolation("ExecutableAllowlist", "command denied")]
        };

        var result = await new EnvironmentDebugAdapterToolResolver().ResolveAsync(
            Descriptor(),
            Resolution(Execution(process), DebugAdapterTrustLevel.Trusted));

        result.Available.Should().BeFalse();
        result.SafeReasonCode.Should().Be("ADAPTER_PROBE_DENIED_BY_ENVIRONMENT_POLICY");
    }

    [Fact]
    public async Task Standard_factory_builds_owned_authorized_stdio_plans_for_launch_and_attach()
    {
        var process = new ProbeProcessProvider();
        var environment = new Dictionary<string, string?> { ["SAFE"] = "before" };
        var resolution = Resolution(Execution(process), DebugAdapterTrustLevel.Trusted) with
        {
            FilteredEnvironment = environment
        };
        var descriptor = Descriptor() with { ArgumentHints = ["--dap"] };
        var factory = new StandardDebugAdapterFactory(new EnvironmentDebugAdapterToolResolver());
        using var document = JsonDocument.Parse("{\"program\":\"fixture\"}");

        var launch = await factory.CreateLaunchPlanAsync(descriptor, new DebugLaunchContext
        {
            Resolution = resolution,
            Target = "/workspace/fixture",
            WorkingDirectory = "/workspace",
            Configuration = document.RootElement
        });
        var attach = await factory.CreateAttachPlanAsync(descriptor, new DebugAttachContext
        {
            Resolution = resolution,
            ProcessId = "42",
            WorkingDirectory = "/workspace",
            Configuration = document.RootElement
        });
        environment["SAFE"] = "after";
        document.Dispose();

        launch.ResolvedCommand.Should().Be("fixture-adapter");
        launch.CommandArguments.Should().Equal("--dap");
        launch.TransportKind.Should().Be("stdio");
        launch.ToolSearchScope.Should().Be(DebugAdapterToolSearchScope.GlobalCommand);
        launch.ProcessProviderId.Should().Be("test.process");
        launch.ProcessExecution.Should().BeSameAs(resolution.ProcessExecution);
        launch.ExecutionTarget.Should().Be(resolution.ProcessExecution!.ExecutionTarget);
        launch.PackageProvenance.PackageId.Should().Be("fixture");
        launch.TrustDecision.Should().BeSameAs(resolution.TrustDecision);
        launch.EndpointCatalogRevision.Should().Be(resolution.EndpointCatalogRevision);
        launch.AuthorizationScope.Should().Be("debug.adapter.launch");
        launch.CanonicalWorkingDirectory.Should().Be("/workspace");
        launch.ToolProvenance.LocationIdentity.Should().Be("fixture-adapter");
        launch.FilteredEnvironment["SAFE"].Should().Be("before");
        launch.Arguments.GetProperty("program").GetString().Should().Be("fixture");
        attach.AdapterId.Should().Be("fixture");
        attach.Arguments.GetProperty("program").GetString().Should().Be("fixture");
    }

    [Fact]
    public async Task Transport_factory_starts_directly_through_the_captured_provider_and_target()
    {
        var process = new ProbeProcessProvider();
        var resolution = Resolution(Execution(process), DebugAdapterTrustLevel.Trusted);
        using var document = JsonDocument.Parse("{}");
        var plan = await new StandardDebugAdapterFactory(new EnvironmentDebugAdapterToolResolver())
            .CreateLaunchPlanAsync(Descriptor(), new DebugLaunchContext
            {
                Resolution = resolution,
                Target = "/workspace/fixture",
                WorkingDirectory = "/workspace",
                Configuration = document.RootElement
            });

        await using var transport = await new DebugProtocolTransportFactory().CreateAsync(plan);

        process.StartCount.Should().Be(1);
        process.StartOutputSink.Should().BeNull();
        process.LastSpec!.Target.Should().Be(resolution.ProcessExecution!.ExecutionTarget);
        process.LastSpec.Command.FileName.Should().Be("fixture-adapter");
        process.LastSpec.Command.WorkingDirectory.Should().Be("/workspace");
        process.LastSpec.Io.StandardInput.Kind.Should().Be(ProcessInputKind.Stream);
        process.LastSpec.Io.StandardOutput.Capture.Should().BeFalse();
        process.LastSpec.Io.StandardOutput.Stream.Should().BeTrue();
        process.LastSpec.Isolation.Network.Mode.Should().Be(NetworkEgressMode.Blocked);
        process.LastSpec.Isolation.Mode.Should().Be(ProcessIsolationMode.Isolated);
    }

    [Fact]
    public async Task Attach_transport_disables_cross_process_sandbox_only_after_a_trusted_plan()
    {
        var process = new ProbeProcessProvider();
        var resolution = Resolution(Execution(process), DebugAdapterTrustLevel.Trusted);
        using var document = JsonDocument.Parse("{}");
        var plan = await new StandardDebugAdapterFactory(
                new EnvironmentDebugAdapterToolResolver())
            .CreateAttachPlanAsync(Descriptor(), new DebugAttachContext
            {
                Resolution = resolution,
                ProcessId = "42",
                WorkingDirectory = "/workspace",
                Configuration = document.RootElement
            });

        await using var transport =
            await new DebugProtocolTransportFactory().CreateAsync(plan);

        process.StartCount.Should().Be(1);
        process.LastSpec!.Isolation.Mode.Should().Be(ProcessIsolationMode.Disabled);
    }

    [Fact]
    public async Task Full_access_policy_disables_launch_adapter_process_isolation()
    {
        var process = new ProbeProcessProvider();
        var resolution = Resolution(Execution(process), DebugAdapterTrustLevel.Trusted) with
        {
            ProcessSandbox = new AgentSandboxRuntime
            {
                Security = new AgentSecurityRunConfig
                {
                    Sandbox = new AgentSandboxRunConfig { Mode = AgentSandboxPolicy.Disabled }
                }
            }
        };
        using var document = JsonDocument.Parse("{}");
        var plan = await new StandardDebugAdapterFactory(
                new EnvironmentDebugAdapterToolResolver())
            .CreateLaunchPlanAsync(Descriptor(), new DebugLaunchContext
            {
                Resolution = resolution,
                Target = "/workspace/fixture",
                WorkingDirectory = "/workspace",
                Configuration = document.RootElement
            });

        await using var transport =
            await new DebugProtocolTransportFactory().CreateAsync(plan);

        process.LastSpec!.Isolation.Mode.Should().Be(ProcessIsolationMode.Disabled);
    }

    [Fact]
    public async Task Resolver_executes_only_host_approved_candidates_and_preserves_their_scope()
    {
        var process = new ProbeProcessProvider();
        var resolver = new EnvironmentDebugAdapterToolResolver(new FixedSearchPolicy(new DebugAdapterToolCandidate
        {
            Command = "/workspace/.tools/fixture-adapter",
            ProbeArguments = ["probe"],
            SearchScope = DebugAdapterToolSearchScope.WorkspaceLocal,
            LocationIdentity = "workspace:.tools/fixture-adapter",
            ContentDigest = "sha256:test",
            LaunchArguments = ["qualified-launch"]
        }));

        var result = await resolver.ResolveAsync(
            Descriptor(),
            Resolution(Execution(process), DebugAdapterTrustLevel.Trusted));

        process.LastSpec!.Command.FileName.Should().Be("/workspace/.tools/fixture-adapter");
        process.LastSpec.Command.Arguments.Should().Equal("probe");
        result.SearchScope.Should().Be(DebugAdapterToolSearchScope.WorkspaceLocal);
        result.LocationIdentity.Should().Be("workspace:.tools/fixture-adapter");
        result.ContentDigest.Should().Be("sha256:test");
        result.LaunchArguments.Should().Equal("qualified-launch");
    }

    [Fact]
    public void Configured_search_policy_rejects_unbounded_or_unsafe_locations()
    {
        var action = () => new ConfiguredDebugAdapterToolSearchPolicy([new()
        {
            AdapterId = "fixture",
            Command = "fixture\0adapter",
            ProbeArguments = ["--version"],
            SearchScope = DebugAdapterToolSearchScope.PackageManaged,
            LocationIdentity = "package:fixture@1"
        }]);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(DebugAdapterToolSearchScope.WorkspaceLocal)]
    [InlineData(DebugAdapterToolSearchScope.PackageManaged)]
    [InlineData(DebugAdapterToolSearchScope.ManagedAssembly)]
    [InlineData(DebugAdapterToolSearchScope.GlobalCommand)]
    public void Configured_search_policy_materializes_every_approved_scope_immutably(DebugAdapterToolSearchScope scope)
    {
        var launchArguments = new[] { "before" };
        var policy = new ConfiguredDebugAdapterToolSearchPolicy([new()
        {
            AdapterId = "fixture",
            Command = "approved-adapter",
            ProbeArguments = ["probe"],
            LaunchArguments = launchArguments,
            SearchScope = scope,
            LocationIdentity = $"{scope}:fixture"
        }]);
        launchArguments[0] = "after";

        var candidate = policy.GetApprovedCandidates(Descriptor(),
            Resolution(Execution(new ProbeProcessProvider()), DebugAdapterTrustLevel.Trusted)).Single();

        candidate.SearchScope.Should().Be(scope);
        candidate.LaunchArguments.Should().Equal("before");
        candidate.LaunchArguments.Should().NotBeOfType<string[]>();
    }

    [Fact]
    public async Task Endpoint_attach_uses_only_host_resolved_authorized_descriptor()
    {
        var process = new ProbeProcessProvider();
        var resolution = Resolution(Execution(process), DebugAdapterTrustLevel.Trusted) with
        {
            EndpointCatalogRevision = 3,
            PolicyRevision = 5
        };
        var endpointResolver = new FixedEndpointResolver(new()
        {
            EndpointId = "endpoint-1",
            EnvironmentId = resolution.EnvironmentId,
            EndpointCatalogRevision = 3,
            PolicyRevision = 5,
            TransportKind = DebugAdapterTransportKind.ApprovedTcpConnect,
            AuthorizedAddress = "opaque-loopback-binding",
            AuthorityReference = "authority-1"
        });
        var factory = new StandardDebugAdapterFactory(
            new EnvironmentDebugAdapterToolResolver(),
            endpointResolver: endpointResolver);
        using var configuration = JsonDocument.Parse("{}");

        var plan = await factory.CreateAttachPlanAsync(Descriptor(), new()
        {
            Resolution = resolution,
            EndpointId = "endpoint-1",
            WorkingDirectory = "/workspace",
            Configuration = configuration.RootElement
        });

        process.RunCount.Should().Be(0);
        plan.Transport.Kind.Should().Be(DebugAdapterTransportKind.ApprovedTcpConnect);
        plan.Transport.EndpointId.Should().Be("endpoint-1");
        plan.Transport.AuthorizedAddress.Should().Be("opaque-loopback-binding");
        plan.ResolvedCommand.Should().BeNull();
    }

    [Fact]
    public async Task Behavioral_factories_use_adapter_specific_probes_and_closed_transport_kinds()
    {
        var process = new ProbeProcessProvider();
        var resolution = Resolution(Execution(process), DebugAdapterTrustLevel.Trusted);
        var standard = new StandardDebugAdapterFactory(new EnvironmentDebugAdapterToolResolver());
        using var configuration = JsonDocument.Parse("{}");

        var delveDescriptor = Descriptor() with
        {
            Id = "delve",
            CommandHints = ["dlv"],
            ArgumentHints = ["dap"]
        };
        var delve = new DelveAdapterFactory(standard);
        await delve.ProbeAsync(delveDescriptor, resolution);
        process.LastSpec!.Command.Arguments.Should().Equal("version");
        var delvePlan = await delve.CreateLaunchPlanAsync(delveDescriptor, new()
        {
            Resolution = resolution,
            Target = "/workspace",
            WorkingDirectory = "/workspace",
            Configuration = configuration.RootElement
        });

        var codeLldbDescriptor = Descriptor() with
        {
            Id = "codelldb",
            CommandHints = ["codelldb"],
            ArgumentHints = ["--port", "0"]
        };
        var codeLldbPlan = await new CodeLldbAdapterFactory(standard).CreateLaunchPlanAsync(
            codeLldbDescriptor,
            new() { Resolution = resolution, Target = "/workspace/app", WorkingDirectory = "/workspace", Configuration = configuration.RootElement });
        var javaScriptPlan = await new JavaScriptDebugAdapterFactory(standard).CreateLaunchPlanAsync(
            Descriptor() with { Id = "javascript", CommandHints = ["js-debug-adapter"] },
            new() { Resolution = resolution, Target = "/workspace/app.js", WorkingDirectory = "/workspace", Configuration = configuration.RootElement });

        delvePlan.Transport.Kind.Should().Be(DebugAdapterTransportKind.EnvironmentTcpServer);
        delvePlan.Transport.AllocatesDynamicLoopbackEndpoint.Should().BeTrue();
        delvePlan.Transport.DynamicEndpointMode.Should().Be(DebugDynamicEndpointMode.AdapterReportsSelectedPort);
        delvePlan.CommandArguments.Should().Equal("dap", "--listen=127.0.0.1:0");
        codeLldbPlan.Transport.Kind.Should().Be(DebugAdapterTransportKind.EnvironmentTcpServer);
        codeLldbPlan.Transport.Arguments.Should().Equal("--port", "0");
        javaScriptPlan.Transport.DynamicEndpointMode.Should().Be(DebugDynamicEndpointMode.AppendSelectedPortAndLoopbackHost);
    }

    private static DebugAdapterDescriptor Descriptor() => new()
    {
        Id = "fixture",
        Languages = ["fixture"],
        FileExtensions = [".fixture"],
        RootMarkers = [],
        TargetKinds = DebugTargetKind.SourceFile,
        CommandHints = ["fixture-adapter"],
        Provenance = new() { PackageId = "fixture", PackageVersion = "1", AssemblyName = "fixture" }
    };

    private static DebugAdapterResolutionContext Resolution(RuntimeProcessExecutionBinding execution, DebugAdapterTrustLevel trust) => new()
    {
        WorkspaceRoot = "/workspace",
        EnvironmentId = execution.EnvironmentId,
        EnvironmentRevision = execution.EnvironmentRevision,
        TargetPlatform = "linux-x64",
        PolicyRevision = 1,
        ProcessExecution = execution,
        TrustDecision = new() { TrustLevel = trust, PolicyRevision = "1", ReasonCode = "TEST" }
    };

    private static RuntimeProcessExecutionBinding Execution(IProcessProvider process) => new()
    {
        EnvironmentId = "test-environment",
        EnvironmentRevision = 7,
        ProcessProvider = process,
        ExecutionTarget = new TargetHandle<ExecutionUnit>(
            new TargetRoute { Kind = new TargetKind("test.execution-unit"), Scope = new ResourceScope("test") },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Control | TargetHandleAuthority.Observe)
    };

    private static FunctionExecutionContext CreateContext(
        IDebugSessionManager manager,
        RuntimeProcessExecutionBinding? processExecution,
        AgentRunConfig? runConfig = null)
    {
        runConfig ??= new AgentRunConfig();
        var function = AIFunctionFactory.Create(() => "ok", new AIFunctionFactoryOptions { Name = "debug_test", Description = "test" });
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new Session("session-1");
        var thread = new Thread("session-1", "test-agent") { Id = "thread-1" };
        var coordinator = new EventCoordinator();
        var agentContext = new AgentContext("AgentA", "conversation-1", state, coordinator, session, thread, CancellationToken.None);
        agentContext.RuntimeCapabilities.Set(manager);
        agentContext.RuntimeCapabilities.Set(new DebugRuntimeBindingState());
        if (processExecution is not null)
            agentContext.RuntimeCapabilities.Set(processExecution);
        var before = agentContext.AsBeforeFunction(function, "call-1", new Dictionary<string, object?>(), runConfig, null, null);
        var request = new FunctionRequest
        {
            Function = function,
            CallId = "call-1",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            RunConfig = runConfig,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = coordinator
        };
        return new FunctionExecutionContext(before, request);
    }

    private sealed class ProbeProcessProvider : IProcessProvider
    {
        public ProviderId ProviderId => new("test.process");
        public int RunCount { get; private set; }
        public int StartCount { get; private set; }
        public IProcessOutputSink? StartOutputSink { get; private set; }
        public ProcessInvocationSpec? LastSpec { get; private set; }
        public IReadOnlyList<ProcessViolation> Violations { get; init; } = [];

        public ValueTask<ProcessInvocationResult> RunAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default)
        {
            RunCount++;
            LastSpec = spec;
            return ValueTask.FromResult(new ProcessInvocationResult
            {
                ExitCode = 0,
                CompletionKind = ProcessCompletionKind.Exited,
                Violations = Violations,
                Output = new ProcessCapturedOutput
                {
                    Stdout = new ProcessStreamOutput { CapturedBytes = Encoding.UTF8.GetBytes("fixture 1.2.3\n") },
                    Stderr = new ProcessStreamOutput(),
                    OutputDrainTimeout = TimeSpan.Zero
                }
            });
        }

        public ValueTask<IProcessInvocationHandle> StartAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default)
        {
            StartCount++;
            LastSpec = spec;
            StartOutputSink = output;
            return ValueTask.FromResult<IProcessInvocationHandle>(new EmptyInvocationHandle(spec));
        }
        public ValueTask SignalAsync(TargetHandle<ProcessInvocation> process, ProcessSignal signal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ResizeTerminalAsync(TargetHandle<ProcessInvocation> process, TerminalSpec size, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ProcessInvocationResult> WaitAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyInvocationHandle(ProcessInvocationSpec spec) : IProcessInvocationHandle
    {
        public TargetHandle<ProcessInvocation> Handle { get; } = new(
            new TargetRoute { Kind = new("test.process"), Scope = new("test") },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Control | TargetHandleAuthority.Observe);
        public ResourceRef<ProcessInvocation>? Resource => null;
        public ProcessInvocationSpec Spec { get; } = spec;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CloseStdinAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<ProcessInvocationResult> WaitAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProcessInvocationResult
        {
            CompletionKind = ProcessCompletionKind.Completed,
            Output = new() { Stdout = new(), Stderr = new(), OutputDrainTimeout = TimeSpan.Zero }
        });
        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FixedSearchPolicy(params DebugAdapterToolCandidate[] candidates) : IDebugAdapterToolSearchPolicy
    {
        public IReadOnlyList<DebugAdapterToolCandidate> GetApprovedCandidates(
            DebugAdapterDescriptor descriptor,
            DebugAdapterResolutionContext context) => candidates;
    }

    private sealed class FixedEndpointResolver(AuthorizedDebugEndpointDescriptor endpoint) : IDebugEndpointResolver
    {
        public ValueTask<AuthorizedDebugEndpointDescriptor?> ResolveAsync(
            string endpointId,
            DebugAdapterResolutionContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AuthorizedDebugEndpointDescriptor?>(
                string.Equals(endpoint.EndpointId, endpointId, StringComparison.Ordinal) ? endpoint : null);
    }
}
