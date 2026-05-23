namespace HPD.Execution.AppleVirtualization.Tests;

using System.Text;
using FluentAssertions;
using HPD.Execution.AppleVirtualization.Authority;
using HPD.Execution.AppleVirtualization.Engines;
using HPD.Execution.AppleVirtualization.Processes;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.AppleVirtualization.State;
using HPD.Execution.AppleVirtualization.Tests.Fixtures;
using HPD.Execution.Contracts;
using Xunit;

public sealed class AppleVirtualizationContainerSmokeWorkflowTests
{
    [Fact]
    public async Task Controlled_fake_engine_can_run_simple_container_command_and_return_output()
    {
        TestContext context = await CreateReadyContextAsync();
        context.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, "container-smoke-ok\n"u8.ToArray(), final: true));
        var sink = new RecordingOutputSink();

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context), sink);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.Span).Should().Be("container-smoke-ok\n");
        result.Output.Stdout.BytesObserved.Should().Be("container-smoke-ok\n"u8.ToArray().Length);
        sink.Chunks.Should().ContainSingle(chunk => chunk.Stream == ProcessOutputStream.Stdout);
        sink.Chunks.Single().Bytes.ToArray().Should().Equal("container-smoke-ok\n"u8.ToArray());
        context.Helper.Requests.Should().Contain(request => request.Operation == AppleVirtualizationHelperOperation.EngineStatus);
        context.Helper.Requests.Should().Contain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessStart);
    }

    [Fact]
    public async Task Controlled_fake_containerd_engine_uses_containerd_api_authority_and_projection()
    {
        TestContext context = await CreateReadyContextAsync(
            engineKind: EngineControlPlaneKind.Containerd,
            engineApi: EngineApiKind.ContainerdApi,
            authorityMode: EngineAuthorityMode.Rootful,
            projectedSocketPath: "/run/hpd/engine/containerd.sock");
        context.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, "containerd-smoke-ok\n"u8.ToArray(), final: true));

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context) with
        {
            Api = EngineApiKind.ContainerdApi,
            Command = new ProcessCommandSpec
            {
                FileName = "/hpd/container-smoke",
                Arguments = ["run", "--rm", "--image", "alpine:3.20", "--engine-socket", "/run/hpd/engine/containerd.sock"],
                WorkingDirectory = "/",
            },
        });

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.Span).Should().Be("containerd-smoke-ok\n");
        AppleVirtualizationHelperEnvelope authorityBind = context.Helper.Requests.Single(request =>
            request.Operation == AppleVirtualizationHelperOperation.AuthorityBind);
        authorityBind.AuthorityBindingRequest!.EffectiveAuthorityClass.Should().Be(SensitiveAuthorityClass.RootfulEngineControl);
        authorityBind.AuthorityBindingRequest.Projection.TargetSocketPath!.Value.Value.Should().Be("/run/hpd/engine/containerd.sock");
        AppleVirtualizationHelperEnvelope processStart = context.Helper.Requests.Single(request =>
            request.Operation == AppleVirtualizationHelperOperation.ProcessStart);
        processStart.ProcessStartRequest!.Command.Arguments.Should().Contain("/run/hpd/engine/containerd.sock");
    }

    [Fact]
    public async Task Controlled_fake_podman_engine_uses_podman_api_authority_and_projection()
    {
        TestContext context = await CreateReadyContextAsync(
            engineKind: EngineControlPlaneKind.Podman,
            engineApi: EngineApiKind.PodmanApi,
            authorityMode: EngineAuthorityMode.Rootless,
            projectedSocketPath: "/run/hpd/engine/podman.sock");
        context.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, "podman-smoke-ok\n"u8.ToArray(), final: true));

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context) with
        {
            Api = EngineApiKind.PodmanApi,
            Command = new ProcessCommandSpec
            {
                FileName = "/hpd/container-smoke",
                Arguments = ["run", "--rm", "--image", "docker.io/library/alpine:3.20", "--engine-socket", "/run/hpd/engine/podman.sock"],
                WorkingDirectory = "/",
            },
        });

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.Span).Should().Be("podman-smoke-ok\n");
        AppleVirtualizationHelperEnvelope authorityBind = context.Helper.Requests.Single(request =>
            request.Operation == AppleVirtualizationHelperOperation.AuthorityBind);
        authorityBind.AuthorityBindingRequest!.EffectiveAuthorityClass.Should().Be(SensitiveAuthorityClass.RootlessEngineControl);
        authorityBind.AuthorityBindingRequest.Projection.TargetSocketPath!.Value.Value.Should().Be("/run/hpd/engine/podman.sock");
        AppleVirtualizationHelperEnvelope processStart = context.Helper.Requests.Single(request =>
            request.Operation == AppleVirtualizationHelperOperation.ProcessStart);
        processStart.ProcessStartRequest!.Command.Arguments.Should().Contain("/run/hpd/engine/podman.sock");
    }

    [Fact]
    public async Task Container_smoke_process_resource_is_cleaned_up_after_transient_run()
    {
        TestContext context = await CreateReadyContextAsync();
        context.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, "ok\n"u8.ToArray(), final: true));

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context));

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        result.ProcessId.Should().NotBeNull();
        context.Ledger.TryGetProcessInvocation(new ResourceRef<ProcessInvocation>(
                result.ProcessId!.Value,
                AppleVirtualizationContractFixtures.RuntimeScope,
                new ResourceGeneration(1)))
            .Succeeded.Should().BeFalse("container smoke process resources are transient unless explicitly retained");
        context.Ledger.TryGetExecutionUnit(context.UnitEntry.Resource).Entry!.Status.ActiveProcesses.Should().BeEmpty();
    }

    [Fact]
    public async Task Container_output_uses_existing_bounded_process_output_capture_accounting()
    {
        TestContext context = await CreateReadyContextAsync();
        context.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, "0123456789"u8.ToArray(), final: true));

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context) with
        {
            MaxCapturedBytesPerStream = 4,
        });

        result.Output.Stdout.BytesObserved.Should().Be(10);
        result.Output.Stdout.BytesCaptured.Should().Be(4);
        result.Output.Stdout.BytesDiscarded.Should().Be(6);
        result.Output.Stdout.Truncated.Should().BeTrue();
        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal("0123"u8.ToArray());
    }

    [Fact]
    public async Task Container_workflow_fails_when_engine_is_not_ready()
    {
        TestContext context = await CreateReadyContextAsync(
            AppleVirtualizationEngineObservationState.Degraded,
            createAuthorityBinding: false);

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context));

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle(condition =>
            condition.Reason == "AppleVirtualization.ContainerSmokeEngineNotReady");
        context.Helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessStart);
    }

    [Fact]
    public async Task Container_workflow_fails_without_required_authority_binding_for_engine_api_access()
    {
        TestContext context = await CreateReadyContextAsync(createAuthorityBinding: false);

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context));

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle(condition =>
            condition.Reason == "AppleVirtualization.ContainerSmokeEngineAuthorityRequired");
        context.Helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessStart);
    }

    [Fact]
    public async Task Container_workflow_fails_when_engine_authority_has_been_revoked()
    {
        TestContext context = await CreateReadyContextAsync();
        AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus> binding =
            context.Ledger.TryGetAuthorityBinding(context.AuthorityRef).Entry!;
        AuthorityBindingSpec spec = context.Ledger.TryGetAuthorityBindingSpec(context.AuthorityRef)!;
        context.Ledger.UpsertAuthorityBinding(
            Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
            binding.Status with
            {
                BoundAuthority = binding.Status.BoundAuthority! with
                {
                    RevocationStatus = RevocationVerificationStatus.Verified,
                },
            },
            spec);
        int requestsBeforeRun = context.Helper.Requests.Count;

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context));

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle(condition =>
            condition.Reason == "AppleVirtualization.ContainerSmokeEngineAuthorityRevoked");
        context.Helper.Requests.Skip(requestsBeforeRun).Should().NotContain(request =>
            request.Operation == AppleVirtualizationHelperOperation.ProcessStart);
    }

    [Fact]
    public async Task Host_docker_socket_is_never_used_by_container_smoke_workflow()
    {
        TestContext context = await CreateReadyContextAsync(hostSocketAuthority: true);

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context));

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle(condition =>
            condition.Reason == "AppleVirtualization.ContainerSmokeHostEngineSocketPassthroughRejected");
        context.Helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessStart);
        context.Helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.EndpointPublish);
    }

    [Fact]
    public async Task Container_workflow_reports_nonzero_exit_diagnostics_without_replacing_process_accounting()
    {
        TestContext context = await CreateReadyContextAsync();
        var processProvider = new StubProcessProvider(new ProcessInvocationResult
        {
            ProcessId = new ResourceId<ProcessInvocation>("process-smoke-nonzero"),
            ExitCode = 17,
            CompletionKind = ProcessCompletionKind.Exited,
            StartedAt = new DateTimeOffset(2026, 5, 21, 12, 0, 0, TimeSpan.Zero),
            ExitedAt = new DateTimeOffset(2026, 5, 21, 12, 0, 1, TimeSpan.Zero),
            Output = new ProcessCapturedOutput
            {
                Stdout = new ProcessStreamOutput
                {
                    CapturedBytes = "partial stdout\n"u8.ToArray(),
                    BytesObserved = 15,
                    BytesCaptured = 15,
                },
                Stderr = new ProcessStreamOutput
                {
                    CapturedBytes = "diagnostic stderr\n"u8.ToArray(),
                    BytesObserved = 18,
                    BytesCaptured = 18,
                },
                OutputDrainTimeout = TimeSpan.FromSeconds(2),
            },
        });
        var workflow = new AppleVirtualizationContainerSmokeWorkflow(
            context.Ledger,
            new AppleVirtualizationEngineControlPlaneProvider(context.Ledger, context.Helper, Options(AppleVirtualizationEngineObservationState.Ready, EngineAuthorityMode.Rootless)),
            processProvider);

        ProcessInvocationResult result = await workflow.RunAsync(Request(context));

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        result.ExitCode.Should().Be(17);
        result.Output.Stdout.CapturedBytes.ToArray().Should().Equal("partial stdout\n"u8.ToArray());
        result.Output.Stderr.CapturedBytes.ToArray().Should().Equal("diagnostic stderr\n"u8.ToArray());
        result.Diagnostics.Should().ContainSingle(condition =>
            condition.Reason == "AppleVirtualization.ContainerSmokeNonZeroExit" &&
            condition.Severity == DiagnosticSeverity.Error);
        processProvider.LastSpec!.Isolation.AuthorityBindings.Should().ContainSingle().Which.Should().Be(context.AuthorityRef);
    }

    [Fact]
    public async Task Projection_network_and_endpoint_state_remains_hpd_owned()
    {
        TestContext context = await CreateReadyContextAsync(seedProjectionNetworkAndEndpointState: true);
        context.Helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, "ok\n"u8.ToArray(), final: true));
        int projectionCount = context.UnitEntry.Status.RealizedContentProjections.Count;
        int membershipCount = context.UnitEntry.Status.NetworkMemberships.Count;
        int endpointCount = context.UnitEntry.Status.PublishedEndpoints.Count;

        ProcessInvocationResult result = await context.Workflow.RunAsync(Request(context));

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        ExecutionUnitStatus unit = context.Ledger.TryGetExecutionUnit(context.UnitEntry.Resource).Entry!.Status;
        unit.RealizedContentProjections.Should().HaveCount(projectionCount);
        unit.NetworkMemberships.Should().HaveCount(membershipCount);
        unit.PublishedEndpoints.Should().HaveCount(endpointCount);
        context.Helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.EndpointPublish);
    }

    [Fact]
    public void Real_container_acceptance_placeholder_remains_skipped_by_default()
    {
        string? enabled = Environment.GetEnvironmentVariable("HPD_APPLEVZ_REAL_CONTAINER_SMOKE");

        enabled.Should().NotBe("1", "real container/VM acceptance must stay explicit-env skipped by default");
    }

    private static async Task<TestContext> CreateReadyContextAsync(
        AppleVirtualizationEngineObservationState engineState = AppleVirtualizationEngineObservationState.Ready,
        bool createAuthorityBinding = true,
        bool hostSocketAuthority = false,
        bool seedProjectionNetworkAndEndpointState = false,
        EngineControlPlaneKind engineKind = EngineControlPlaneKind.DockerCompatible,
        EngineApiKind engineApi = EngineApiKind.DockerCompatible,
        EngineAuthorityMode authorityMode = EngineAuthorityMode.Rootless,
        string projectedSocketPath = "/run/hpd/engine/docker.sock")
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> host = SeedReadyHost(ledger);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger, host.Resource);
        if (seedProjectionNetworkAndEndpointState)
        {
            unit = SeedProjectionNetworkAndEndpointState(ledger, unit.Resource, unit.Status.AssignedHost!.Value);
        }

        var engineProvider = new AppleVirtualizationEngineControlPlaneProvider(
            ledger,
            helper,
            Options(engineState, authorityMode));
        var authorityProvider = new AppleVirtualizationAuthorityBindingProvider(
            ledger,
            helper,
            () => new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        var processProvider = new AppleVirtualizationProcessProvider(ledger, helper);
        var workflow = new AppleVirtualizationContainerSmokeWorkflow(ledger, engineProvider, processProvider);

        ResourceMetadata<EngineControlPlane> engineMetadata = Metadata<EngineControlPlane>("engine-1", "engine-control-plane");
        EngineControlPlaneSpec engineSpec = EngineSpec(host.Resource, engineKind, engineApi, authorityMode);
        EngineControlPlaneStatus engine = await engineProvider.EnsureEngineControlPlaneAsync(engineMetadata, engineSpec, observed: null);
        ResourceRef<AuthorityBinding> authorityRef = AuthorityRef();
        if (createAuthorityBinding)
        {
            AuthorityBindingSpec bindingSpec = hostSocketAuthority
                ? HostDockerSocketSpec(unit.TargetHandle)
                : EngineSocketAuthoritySpec(engine, unit.TargetHandle, engineApi, projectedSocketPath);
            if (hostSocketAuthority)
            {
                ledger.UpsertAuthorityBinding(
                    Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
                    new AuthorityBindingStatus
                    {
                        Phase = ResourcePhase.Ready,
                        BindingPhase = AuthorityBindingPhase.Projected,
                        BoundAuthority = new BoundAuthority
                        {
                            SourceKind = AuthoritySourceKind.UnixSocket,
                            ProjectionKind = AuthorityProjectionKind.SocketPath,
                            EffectiveAuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                        },
                    },
                    bindingSpec);
                ledger.AttachAuthorityBindingToExecutionUnit(unit.Resource, authorityRef);
            }
            else
            {
                await authorityProvider.EnsureAuthorityBindingAsync(
                    Metadata<AuthorityBinding>("engine-authority-1", "authority-binding"),
                    bindingSpec,
                    observed: null);
            }
        }

        return new TestContext(helper, ledger, workflow, engineMetadata, engineSpec, unit, authorityRef);
    }

    private static EngineControlPlaneSpec EngineSpec(
        ResourceRef<RuntimeHost> host,
        EngineControlPlaneKind kind,
        EngineApiKind api,
        EngineAuthorityMode authorityMode) =>
        new()
        {
            Kind = kind,
            Api = api,
            AuthorityMode = authorityMode,
            Host = host,
            EndpointPolicy = new SensitiveEndpointPolicy
            {
                Kind = SensitiveEndpointKind.EngineSocket,
                AuthorityClass = authorityMode == EngineAuthorityMode.Rootless
                    ? SensitiveAuthorityClass.RootlessEngineControl
                    : SensitiveAuthorityClass.RootfulEngineControl,
                Redaction = SensitiveRedactionLevel.RedactIdentifiers,
                RequireAudit = true,
                Lease = new SensitiveLeasePolicy
                {
                    Lifetime = BindingLifetime.ExecutionUnit,
                    RevokeOnTargetStop = true,
                },
            },
        };

    private static AuthorityBindingSpec EngineSocketAuthoritySpec(
        EngineControlPlaneStatus engine,
        TargetHandle<ExecutionUnit> unit,
        EngineApiKind api,
        string projectedSocketPath)
    {
        bool created = AppleVirtualizationEngineEndpointAuthority.TryCreateBindingSpec(
            engine,
            api,
            unit,
            new UnixSocketPath(projectedSocketPath),
            new SensitiveProvenance(Actor: "agent-83", Reason: "container-smoke"),
            out AuthorityBindingSpec? spec,
            out Diagnostic? diagnostic);

        created.Should().BeTrue(diagnostic?.Message);
        return spec!;
    }

    private static AuthorityBindingSpec HostDockerSocketSpec(TargetHandle<ExecutionUnit> unit) =>
        new()
        {
            Kind = AuthorityBindingKind.GuestCapability,
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.UnixSocket,
                Locus = BoundaryLocus.Host,
                SocketPath = new UnixSocketPath("/run/user/1000/docker.sock"),
            },
            Target = new AuthorityBindingTarget(AuthorityTargetKind.ExecutionUnit, Unit: unit),
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.SocketPath,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine/docker.sock"),
                ReadOnly = false,
            },
            Policy = new AuthorityBindingPolicy
            {
                Direction = AuthorityBindingDirection.ProviderToGuest,
                AuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                EffectiveAuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                Redaction = SensitiveRedactionLevel.RedactIdentifiers,
                RequireAudit = true,
            },
        };

    private static AppleVirtualizationContainerSmokeWorkflowRequest Request(TestContext context) =>
        new()
        {
            EngineMetadata = context.EngineMetadata,
            EngineSpec = context.EngineSpec,
            Api = EngineApiKind.DockerCompatible,
            TargetUnit = context.UnitEntry.TargetHandle,
            EngineAuthorityBinding = context.AuthorityRef,
            Command = new ProcessCommandSpec
            {
                FileName = "/hpd/container-smoke",
                Arguments = ["run", "hello"],
                WorkingDirectory = "/workspace",
            },
            Io = new ProcessIoSpec
            {
                StandardOutput = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = true,
                },
                StandardError = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = true,
                },
            },
            Isolation = ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Isolated,
                Network = new NetworkEgressPolicy { Mode = NetworkEgressMode.Blocked },
            },
            Policy = ProcessInvocationPolicy.Default with
            {
                Timeout = TimeSpan.FromSeconds(5),
                OutputDrainTimeout = TimeSpan.FromSeconds(2),
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessOutput(
        string processId,
        ProcessOutputStream stream,
        ReadOnlyMemory<byte> bytes,
        bool final) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = AppleVirtualizationHelperOperation.ProcessReadOutput,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProcessOutputEventSchema,
            ProviderGeneration = 1,
            ProcessOutputEvent = new AppleVirtualizationProcessOutputEvent
            {
                ProcessId = processId,
                Stream = stream,
                Sequence = 1,
                ObservedAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                Bytes = bytes,
                Flags = final ? ProcessOutputChunkFlags.Final : ProcessOutputChunkFlags.None,
            },
        };

    private static AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus> SeedReadyHost(
        AppleVirtualizationProviderStateLedger ledger) =>
        ledger.UpsertRuntimeHost(
            Metadata<RuntimeHost>("runtime-host-1", "runtime-host"),
            new RuntimeHostStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                HostPhase = RuntimeHostPhase.Ready,
                Readiness = new RuntimeHostReadinessStatus(true),
                GuestControl = new GuestControlStatus(Expected: true, Installed: true, Reachable: true),
            });

    private static AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> SeedReadyUnit(
        AppleVirtualizationProviderStateLedger ledger,
        ResourceRef<RuntimeHost> host) =>
        ledger.UpsertExecutionUnit(
            Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Ready,
                AssignedHost = host,
            });

    private static AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> SeedProjectionNetworkAndEndpointState(
        AppleVirtualizationProviderStateLedger ledger,
        ResourceRef<ExecutionUnit> unit,
        ResourceRef<RuntimeHost> host)
    {
        ResourceRef<ContentProjection> projection = new(new ResourceId<ContentProjection>("projection-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));
        ledger.UpsertContentProjection(
            Metadata<ContentProjection>("projection-1", "content-projection"),
            new ContentProjectionStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                ProjectionPhase = ContentProjectionPhase.Projected,
                Views =
                [
                    new RealizedProjectionView
                    {
                        Kind = ProjectionViewKind.FilesystemTree,
                        GuestPath = new GuestPath("/workspace"),
                        EffectiveAccess = AccessMode.ReadOnly,
                    },
                ],
            });
        ledger.AttachContentProjectionToExecutionUnit(unit, projection);

        ResourceRef<Network> network = new(new ResourceId<Network>("network-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));
        ledger.UpsertNetwork(
            Metadata<Network>("network-1", "network"),
            new NetworkStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                NetworkPhase = NetworkPhase.Ready,
                RealizedCapabilities = NetworkCapabilitySet.IPv4 | NetworkCapabilitySet.NatEgress,
            });
        ledger.UpsertNetworkMembership(
            Metadata<NetworkMembership>("membership-1", "network-membership"),
            new NetworkMembershipStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                MembershipPhase = NetworkMembershipPhase.Ready,
            },
            new NetworkMembershipSpec
            {
                Network = network,
                Target = new NetworkMembershipTarget(
                    NetworkMembershipTargetKind.ExecutionUnit,
                    Host: null,
                    Unit: ledger.TryGetExecutionUnit(unit).Entry!.TargetHandle,
                    Process: null),
            });

        ledger.UpsertPublishedEndpoint(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            new PublishedEndpointStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                EndpointPhase = PublishedEndpointPhase.Bound,
            });

        return ledger.TryGetExecutionUnit(unit).Entry!;
    }

    private static AppleVirtualizationProviderOptions Options(
        AppleVirtualizationEngineObservationState state,
        EngineAuthorityMode authorityMode) =>
        new()
        {
            HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake,
            EngineBootstrap = new AppleVirtualizationEngineBootstrapOptions
            {
                Enabled = true,
                AuthorityModeConfigured = true,
                AuthorityMode = authorityMode,
                ScriptedObservationState = state,
            },
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableEngineControlPlane = true,
            },
        };

    private static ResourceMetadata<TResource> Metadata<TResource>(string id, string kind)
        where TResource : IExecutionResourceMarker =>
        AppleVirtualizationContractFixtures.Metadata<TResource>(id, kind);

    private static ResourceRef<AuthorityBinding> AuthorityRef() =>
        new(new ResourceId<AuthorityBinding>("engine-authority-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private sealed record TestContext(
        FakeAppleVirtualizationHelperClient Helper,
        AppleVirtualizationProviderStateLedger Ledger,
        AppleVirtualizationContainerSmokeWorkflow Workflow,
        ResourceMetadata<EngineControlPlane> EngineMetadata,
        EngineControlPlaneSpec EngineSpec,
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> UnitEntry,
        ResourceRef<AuthorityBinding> AuthorityRef);

    private sealed class RecordingOutputSink : IProcessOutputSink
    {
        public List<ProcessOutputChunk> Chunks { get; } = [];

        public ValueTask OnOutputAsync(ProcessOutputChunk chunk, CancellationToken cancellationToken = default)
        {
            Chunks.Add(chunk);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubProcessProvider : IProcessProvider
    {
        private readonly ProcessInvocationResult _result;

        public StubProcessProvider(ProcessInvocationResult result)
        {
            _result = result;
        }

        public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;
        public ProcessInvocationSpec? LastSpec { get; private set; }

        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
        {
            LastSpec = spec;
            return ValueTask.FromResult(_result);
        }

        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
