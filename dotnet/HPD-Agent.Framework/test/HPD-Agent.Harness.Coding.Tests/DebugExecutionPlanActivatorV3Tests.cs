using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugExecutionPlanActivatorV3Tests
{
    [Fact]
    public async Task Direct_activation_revalidates_and_returns_the_exact_adapter_method()
    {
        await using var fixture = new ActivationFixture();
        var plan = fixture.DirectPlan();

        var activated = await fixture.Activator.ActivateAsync(
            plan,
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None);

        activated.AdapterPlan.Should().BeSameAs(plan.Adapter);
        activated.AdapterStartMethod.Should().Be(DebugAdapterStartMethod.Launch);
        activated.OwnedResources.Should().BeEmpty();
        fixture.Factory.ProbeCount.Should().Be(1);
    }

    [Fact]
    public async Task Activation_rejects_wrong_permission_and_changed_environment()
    {
        await using var fixture = new ActivationFixture();
        var plan = fixture.DirectPlan();

        var permission = () => fixture.Activator.ActivateAsync(
            plan,
            fixture.Context(DebugPermissionClass.Attach),
            CancellationToken.None).AsTask();
        var stale = () => fixture.Activator.ActivateAsync(
            plan with { EnvironmentRevision = 2 },
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        await permission.Should().ThrowAsync<UnauthorizedAccessException>();
        await stale.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Activation_rejects_an_invalidated_runtime_before_adapter_reprobe()
    {
        await using var fixture = new ActivationFixture();
        fixture.Runtime.State.Invalidate("ENVIRONMENT_LOST").Should().BeTrue();

        var action = () => fixture.Activator.ActivateAsync(
            fixture.DirectPlan(),
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Factory.ProbeCount.Should().Be(0);
        fixture.Provider.Handle.Should().BeNull();
    }

    [Fact]
    public async Task Hosted_activation_owns_runner_and_uses_reported_testhost_pid()
    {
        await using var fixture = new ActivationFixture(
            "Host debugging is enabled. Please attach debugger to testhost process to continue.\n" +
            "Process Id: 4217, Name: dotnet\n");
        var plan = fixture.HostedPlan();

        var activated = await fixture.Activator.ActivateAsync(
            plan,
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None);

        activated.SemanticStartKind.Should().Be(
            DebugSemanticStartKind.HostedLaunchAttach);
        activated.AdapterStartMethod.Should().Be(DebugAdapterStartMethod.Attach);
        activated.OwnedResources.Should().ContainSingle();
        fixture.Factory.AttachedProcessId.Should().Be("4217");
        fixture.Provider.Handle!.Spec.Should().BeSameAs(plan.Host.Invocation);
        await activated.OwnedResources[0].DisposeAsync();
        fixture.Provider.Handle!.StopCount.Should().Be(1);
        fixture.Provider.Handle.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Hosted_attach_failure_rolls_back_the_runner()
    {
        await using var fixture = new ActivationFixture(
            "Host debugging is enabled. Please attach debugger to testhost process to continue.\n" +
            "Process Id: 4217, Name: dotnet\n");
        fixture.Factory.FailAttach = true;

        var action = () => fixture.Activator.ActivateAsync(
            fixture.HostedPlan(),
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_host_attach_failed");
        fixture.Provider.Handle!.StopCount.Should().Be(1);
        fixture.Provider.Handle.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Hosted_early_exit_is_classified_and_rolls_back()
    {
        await using var fixture = new ActivationFixture("ordinary runner output\n");

        var action = () => fixture.Activator.ActivateAsync(
            fixture.HostedPlan(),
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_host_exited_before_ready");
        fixture.Provider.Handle!.StopCount.Should().Be(1);
        fixture.Provider.Handle.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Prepared_activation_runs_bounded_preparation_and_launches_exact_output()
    {
        await using var fixture = new ActivationFixture();
        var output = Path.Combine(fixture.Workspace, "prepared.dll");
        await File.WriteAllBytesAsync(output, [0]);
        fixture.Provider.RunResult = Completed();

        var activated = await fixture.Activator.ActivateAsync(
            fixture.PreparedPlan(output),
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None);

        fixture.Provider.RunCount.Should().Be(1);
        fixture.Factory.LaunchedTarget.Should().Be(output);
        activated.AdapterStartMethod.Should().Be(DebugAdapterStartMethod.Launch);
    }

    [Fact]
    public async Task Activation_rejects_an_adapter_that_became_unavailable()
    {
        await using var fixture = new ActivationFixture();
        fixture.Factory.Availability =
            new(DebugAdapterAvailabilityKind.Unavailable, "MISSING");

        var action = () => fixture.Activator.ActivateAsync(
            fixture.DirectPlan(),
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("adapter_unavailable");
    }

    [Fact]
    public async Task Hosted_activation_rejects_output_beyond_the_byte_bound_and_cleans_up()
    {
        await using var fixture = new ActivationFixture(new string('x', 256));
        var plan = fixture.HostedPlan() with
        {
            Host = fixture.HostedPlan().Host with { MaximumStdoutBytes = 32 },
            Readiness = fixture.HostedPlan().Readiness with
            {
                MaximumObservationBytes = 32
            }
        };

        var action = () => fixture.Activator.ActivateAsync(
            plan,
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_host_readiness_invalid");
        fixture.Provider.Handle!.StopCount.Should().Be(1);
        fixture.Provider.Handle.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Hosted_readiness_cancellation_stops_and_disposes_the_owned_process()
    {
        await using var fixture = new ActivationFixture(blockOutput: true);
        using var cancellation = new CancellationTokenSource();
        var activation = fixture.Activator.ActivateAsync(
            fixture.HostedPlan(),
            fixture.Context(DebugPermissionClass.Launch),
            cancellation.Token).AsTask();

        await fixture.Provider.OutputReadStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        Func<Task> action = () => activation;
        var exception = await action.Should()
            .ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_activation_cancelled");
        fixture.Provider.Handle!.StopCount.Should().Be(1);
        fixture.Provider.Handle.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Hosted_readiness_timeout_stops_and_disposes_the_owned_process()
    {
        await using var fixture = new ActivationFixture(blockOutput: true);
        var original = fixture.HostedPlan();
        var plan = original with
        {
            Readiness = original.Readiness with
            {
                Timeout = TimeSpan.FromMilliseconds(25)
            }
        };

        var action = () => fixture.Activator.ActivateAsync(
            plan,
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_host_readiness_timeout");
        fixture.Provider.Handle!.StopCount.Should().Be(1);
        fixture.Provider.Handle.LastStopRequest!.Kind.Should()
            .Be(StopKind.GracefulThenKill);
        fixture.Provider.Handle.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Cleanup_failure_does_not_replace_the_primary_attach_failure()
    {
        await using var fixture = new ActivationFixture(
            "Host debugging is enabled. Please attach debugger to testhost process to continue.\n" +
            "Process Id: 4217, Name: dotnet\n");
        fixture.Factory.FailAttach = true;
        fixture.Provider.ThrowOnStop = true;
        fixture.Provider.ThrowOnDispose = true;

        var action = () => fixture.Activator.ActivateAsync(
            fixture.HostedPlan(),
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_host_attach_failed");
        fixture.Provider.Handle!.StopCount.Should().Be(1);
        fixture.Provider.Handle.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Activation_rejects_a_changed_process_provider()
    {
        await using var fixture = new ActivationFixture();
        var alternate = new FakeProcessProvider("", false, new ProviderId("other.process"));
        var plan = fixture.DirectPlan() with
        {
            Adapter = fixture.DirectPlan().Adapter with
            {
                ProcessExecution = fixture.Binding with
                {
                    ProcessProvider = alternate
                }
            }
        };

        var action = () => fixture.Activator.ActivateAsync(
            plan,
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Factory.ProbeCount.Should().Be(0);
    }

    [Fact]
    public async Task Adapter_reprobe_cancellation_is_classified_before_resource_acquisition()
    {
        await using var fixture = new ActivationFixture();
        fixture.Factory.BlockProbe = true;
        using var cancellation = new CancellationTokenSource();
        var activation = fixture.Activator.ActivateAsync(
            fixture.DirectPlan(),
            fixture.Context(DebugPermissionClass.Launch),
            cancellation.Token).AsTask();

        await fixture.Factory.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await AssertActivationCancelledAsync(activation);
        fixture.Provider.Handle.Should().BeNull();
    }

    [Fact]
    public async Task Host_start_cancellation_acquires_no_resource()
    {
        await using var fixture = new ActivationFixture();
        fixture.Provider.BlockStart = true;
        using var cancellation = new CancellationTokenSource();
        var activation = fixture.Activator.ActivateAsync(
            fixture.HostedPlan(),
            fixture.Context(DebugPermissionClass.Launch),
            cancellation.Token).AsTask();

        await fixture.Provider.StartStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await AssertActivationCancelledAsync(activation);
        fixture.Provider.Handle.Should().BeNull();
    }

    [Fact]
    public async Task Attach_plan_cancellation_rolls_back_the_owned_runner()
    {
        await using var fixture = new ActivationFixture(
            "Host debugging is enabled. Please attach debugger to testhost process to continue.\n" +
            "Process Id: 4217, Name: dotnet\n");
        fixture.Factory.BlockAttach = true;
        using var cancellation = new CancellationTokenSource();
        var activation = fixture.Activator.ActivateAsync(
            fixture.HostedPlan(),
            fixture.Context(DebugPermissionClass.Launch),
            cancellation.Token).AsTask();

        await fixture.Factory.AttachStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await AssertActivationCancelledAsync(activation);
        fixture.Provider.Handle!.StopCount.Should().Be(1);
        fixture.Provider.Handle.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Preparation_cancellation_is_classified_before_launch()
    {
        await using var fixture = new ActivationFixture();
        fixture.Provider.BlockRun = true;
        using var cancellation = new CancellationTokenSource();
        var activation = fixture.Activator.ActivateAsync(
            fixture.PreparedPlan(Path.Combine(fixture.Workspace, "prepared.dll")),
            fixture.Context(DebugPermissionClass.Launch),
            cancellation.Token).AsTask();

        await fixture.Provider.RunStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await AssertActivationCancelledAsync(activation);
        fixture.Factory.LaunchedTarget.Should().BeNull();
    }

    [Fact]
    public async Task Prepared_activation_rejects_a_missing_exact_output()
    {
        await using var fixture = new ActivationFixture();
        var missing = Path.Combine(fixture.Workspace, "missing.dll");

        var action = () => fixture.Activator.ActivateAsync(
            fixture.PreparedPlan(missing),
            fixture.Context(DebugPermissionClass.Launch),
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_preparation_output_missing");
    }

    [Theory]
    [InlineData(
        "pid: 42",
        "Incomplete")]
    [InlineData(
        "Host debugging is enabled. Please attach debugger to testhost process to continue.\nProcess Id: 999999999999999999999, Name: dotnet",
        "Invalid")]
    [InlineData(
        "Host debugging is enabled. Please attach debugger to testhost process to continue.\nProcess Id: 1, Name: dotnet\nProcess Id: 2, Name: dotnet",
        "Invalid")]
    public void Readiness_requires_one_complete_official_handshake(
        string transcript,
        string expected)
    {
        new VSTestHostDebugReadinessParser()
            .Observe(transcript, DebugReadinessMultiplicity.ExactlyOne)
            .Status.ToString().Should().Be(expected);
    }

    private static ProcessInvocationResult Completed() => new()
    {
        ExitCode = 0,
        CompletionKind = ProcessCompletionKind.Completed,
        Output = new()
        {
            Stdout = new(),
            Stderr = new(),
            OutputDrainTimeout = TimeSpan.Zero
        }
    };

    private static async Task AssertActivationCancelledAsync(
        Task<DebugActivatedExecution> activation)
    {
        var action = () => activation;
        var exception = await action.Should()
            .ThrowAsync<DebugStartPlanningException>();
        exception.Which.Kind.Should().Be("debug_activation_cancelled");
    }

    private sealed class ActivationFixture : IAsyncDisposable
    {
        public ActivationFixture(string output = "", bool blockOutput = false)
        {
            Workspace = Path.Combine(
                Path.GetTempPath(),
                "hpd-activation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Workspace);
            Manager = new DebugSessionManager(new DebugTerminalRecordStore(new DebugTerminalRecordStoreOptions()));
            Provider = new FakeProcessProvider(output, blockOutput);
            Binding = new()
            {
                EnvironmentId = "environment",
                EnvironmentRevision = 1,
                ProcessProvider = Provider,
                ExecutionTarget = ExecutionTarget()
            };
            Runtime = new()
            {
                AgentRuntimeRegistrationId = Manager.RuntimeId,
                SessionId = "session",
                ThreadId = "thread",
                SessionManager = Manager,
                EventScope = new(null, "session", "thread"),
                ProcessExecution = Binding,
                State = new()
            };
            Trust = new()
            {
                TrustLevel = DebugAdapterTrustLevel.Trusted,
                PolicyRevision = "test",
                ReasonCode = "TEST"
            };
            Descriptor = new()
            {
                Id = "netcoredbg",
                Languages = ["csharp"],
                FileExtensions = [".cs"],
                RootMarkers = ["*.csproj"],
                TargetKinds = DebugTargetKind.Executable | DebugTargetKind.Process,
                ProgramKinds = DebugAdapterProgramKind.ExecutableFile,
                Provenance = new()
                {
                    PackageId = "fixture",
                    PackageVersion = "1",
                    AssemblyName = "fixture"
                }
            };
            Factory = new FakeAdapterFactory(this);
            var entry = new DebugAdapterCatalogEntry
            {
                Descriptor = Descriptor,
                FactoryResolver = _ => Factory
            };
            var catalog = new DebugAdapterCatalog(
                [new FixedCatalogProvider(entry)],
                new EmptyServiceProvider());
            Activator = new(
                catalog,
                new BuiltInDebugAdapterConfigurationComposer(),
                new DebugHostReadinessParserRegistry(
                    [new VSTestHostDebugReadinessParser()]),
                new FixedTrustPolicy(Trust));
        }

        public string Workspace { get; }
        public DebugSessionManager Manager { get; }
        public FakeProcessProvider Provider { get; }
        public RuntimeProcessExecutionBinding Binding { get; }
        public DebugRuntimeBinding Runtime { get; }
        public DebugAdapterTrustDecision Trust { get; }
        public DebugAdapterDescriptor Descriptor { get; }
        public FakeAdapterFactory Factory { get; }
        public DebugExecutionPlanActivator Activator { get; }

        public DirectAdapterDebugExecutionPlan DirectPlan() => new()
        {
            PlannerId = "direct",
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = "environment",
            EnvironmentRevision = 1,
            CanonicalWorkingDirectory = Workspace,
            InitialConfiguration = new(),
            Adapter = AdapterPlan(DebugAdapterStartMethod.Launch)
        };

        public HostedAttachDebugExecutionPlan HostedPlan() => new()
        {
            PlannerId = "hosted",
            SemanticStartKind = DebugSemanticStartKind.HostedLaunchAttach,
            EnvironmentId = "environment",
            EnvironmentRevision = 1,
            CanonicalWorkingDirectory = Workspace,
            InitialConfiguration = new(),
            Host = new()
            {
                Role = "testhost-runner",
                Invocation = Invocation("dotnet"),
                StartupTimeout = TimeSpan.FromSeconds(2),
                StopTimeout = TimeSpan.FromSeconds(1)
            },
            Readiness = new()
            {
                ProtocolId = VSTestHostDebugReadinessParser.Protocol,
                Timeout = TimeSpan.FromSeconds(2)
            },
            Attach = new()
            {
                AdapterId = Descriptor.Id,
                Resolution = Resolution("debug.adapter.attach"),
                WorkingDirectory = Workspace
            }
        };

        public PreparedAdapterDebugExecutionPlan PreparedPlan(string output) => new()
        {
            PlannerId = "prepared",
            SemanticStartKind = DebugSemanticStartKind.DirectLaunch,
            EnvironmentId = "environment",
            EnvironmentRevision = 1,
            CanonicalWorkingDirectory = Workspace,
            InitialConfiguration = new(),
            Preparation = new()
            {
                Role = "prepare",
                Invocation = Invocation("prepare"),
                ExpectedOutputPath = output
            },
            Launch = new()
            {
                AdapterId = Descriptor.Id,
                Resolution = Resolution("debug.adapter.launch"),
                WorkingDirectory = Workspace
            }
        };

        public DebugExecutionActivationContext Context(
            DebugPermissionClass permissionClass)
            => new()
            {
                Ownership = new(
                    Manager.RuntimeId,
                    "session",
                    "thread",
                    "tree",
                    "environment",
                    1),
                Runtime = Runtime,
                Permission = new("call", "launch", permissionClass),
                DebugSessionId = "root"
            };

        public DebugAdapterStartPlan AdapterPlan(DebugAdapterStartMethod method)
        {
            using var arguments = JsonDocument.Parse("{}");
            return new()
            {
                Method = method,
                AdapterId = Descriptor.Id,
                EnvironmentId = "environment",
                EnvironmentRevision = 1,
                PolicyRevision = 1,
                EndpointCatalogRevision = 1,
                PackageProvenance = Descriptor.Provenance,
                TrustDecision = Trust,
                ProcessExecution = Binding,
                ExecutionTarget = Binding.ExecutionTarget,
                CanonicalWorkingDirectory = Workspace,
                AuthorizationScope = method == DebugAdapterStartMethod.Attach
                    ? "debug.adapter.attach"
                    : "debug.adapter.launch",
                FilteredEnvironment = new Dictionary<string, string?>(),
                Transport = new()
                {
                    Kind = DebugAdapterTransportKind.EnvironmentStdio,
                    Command = "fixture"
                },
                Arguments = arguments.RootElement.Clone()
            };
        }

        private DebugAdapterResolutionContext Resolution(string scope) => new()
        {
            WorkspaceRoot = Workspace,
            EnvironmentId = "environment",
            EnvironmentRevision = 1,
            TargetPlatform = "test",
            PolicyRevision = 1,
            EndpointCatalogRevision = 1,
            AuthorizationScope = scope,
            ProcessExecution = Binding,
            TrustDecision = Trust
        };

        private ProcessInvocationSpec Invocation(string command) => new()
        {
            Target = Binding.ExecutionTarget,
            Role = ProcessRole.Task,
            Command = new()
            {
                FileName = command,
                WorkingDirectory = Workspace
            },
            Policy = ProcessInvocationPolicy.Default with
            {
                StopProcessTree = true
            }
        };

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            try { Directory.Delete(Workspace, recursive: true); } catch { }
        }
    }

    private sealed class FakeAdapterFactory(ActivationFixture fixture)
        : IDebugAdapterFactory
    {
        public int ProbeCount { get; private set; }
        public bool FailAttach { get; set; }
        public bool BlockProbe { get; set; }
        public bool BlockAttach { get; set; }
        public TaskCompletionSource ProbeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AttachStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DebugAdapterAvailability Availability { get; set; } =
            new(DebugAdapterAvailabilityKind.Available);
        public string? AttachedProcessId { get; private set; }
        public string? LaunchedTarget { get; private set; }

        public async ValueTask<DebugAdapterAvailability> ProbeAsync(
            DebugAdapterDescriptor descriptor,
            DebugAdapterResolutionContext context,
            CancellationToken cancellationToken = default)
        {
            ProbeCount++;
            if (BlockProbe)
            {
                ProbeStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return Availability;
        }

        public ValueTask<DebugAdapterStartPlan> CreateLaunchPlanAsync(
            DebugAdapterDescriptor descriptor,
            DebugLaunchContext context,
            CancellationToken cancellationToken = default)
        {
            LaunchedTarget = context.Target;
            return ValueTask.FromResult(
                fixture.AdapterPlan(DebugAdapterStartMethod.Launch));
        }

        public async ValueTask<DebugAdapterStartPlan> CreateAttachPlanAsync(
            DebugAdapterDescriptor descriptor,
            DebugAttachContext context,
            CancellationToken cancellationToken = default)
        {
            if (BlockAttach)
            {
                AttachStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            if (FailAttach)
                throw new InvalidOperationException("fixture attach failure");
            AttachedProcessId = context.ProcessId;
            return fixture.AdapterPlan(DebugAdapterStartMethod.Attach);
        }
    }

    private sealed class FakeProcessProvider(
        string output,
        bool blockOutput,
        ProviderId? providerId = null)
        : IProcessProvider
    {
        public ProviderId ProviderId { get; } = providerId ?? new("test.process");
        public FakeProcessHandle? Handle { get; private set; }
        public ProcessInvocationResult RunResult { get; set; } = Completed();
        public int RunCount { get; private set; }
        public bool BlockRun { get; set; }
        public bool BlockStart { get; set; }
        public bool ThrowOnStop { get; set; }
        public bool ThrowOnDispose { get; set; }
        public TaskCompletionSource RunStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OutputReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? outputSink = null,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            if (BlockRun)
            {
                RunStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return RunResult;
        }

        public async ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? outputSink = null,
            CancellationToken cancellationToken = default)
        {
            if (BlockStart)
            {
                StartStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            Handle = new(
                spec,
                output,
                blockOutput,
                OutputReadStarted,
                () => ThrowOnStop,
                () => ThrowOnDispose);
            return Handle;
        }

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

    private sealed class FakeProcessHandle(
        ProcessInvocationSpec spec,
        string output,
        bool blockOutput,
        TaskCompletionSource outputReadStarted,
        Func<bool> throwOnStop,
        Func<bool> throwOnDispose) : IProcessInvocationHandle
    {
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public ProcessStopRequest? LastStopRequest { get; private set; }
        public TargetHandle<ProcessInvocation> Handle { get; } = new(
            new TargetRoute { Kind = new("test.process"), Scope = new("test") },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Control | TargetHandleAuthority.Observe);
        public ResourceRef<ProcessInvocation>? Resource => null;
        public ProcessInvocationSpec Spec { get; } = spec;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (throwOnDispose())
                throw new InvalidOperationException("fixture dispose failure");
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(
            ProcessStopRequest request,
            CancellationToken cancellationToken = default)
        {
            StopCount++;
            LastStopRequest = request;
            if (throwOnStop())
                throw new InvalidOperationException("fixture stop failure");
            return ValueTask.CompletedTask;
        }

        public ValueTask<ProcessInvocationResult> WaitAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Completed());

        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            outputReadStarted.TrySetResult();
            if (blockOutput)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (output.Length > 0)
                yield return new(
                    Handle,
                    ProcessOutputStream.Stdout,
                    1,
                    DateTimeOffset.UtcNow,
                    Encoding.UTF8.GetBytes(output),
                    ProcessOutputChunkFlags.Final);
            await Task.CompletedTask;
        }

        public ValueTask WriteStdinAsync(
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CloseStdinAsync(
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SignalAsync(
            ProcessSignal signal,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ResizeTerminalAsync(
            TerminalSpec size,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FixedCatalogProvider(DebugAdapterCatalogEntry entry)
        : IDebugAdapterCatalogProvider
    {
        public IEnumerable<DebugAdapterCatalogEntry> GetEntries() => [entry];
    }

    private sealed class FixedTrustPolicy(DebugAdapterTrustDecision decision)
        : IDebugAdapterTrustPolicy
    {
        public DebugAdapterTrustDecision Evaluate(
            DebugAdapterDescriptor descriptor) => decision;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static TargetHandle<ExecutionUnit> ExecutionTarget() => new(
        new TargetRoute
        {
            Kind = new("test.execution"),
            Scope = new("test")
        },
        TargetHandleLifetime.LiveCapability,
        TargetHandleAuthority.Control | TargetHandleAuthority.Observe);
}
