using System.Buffers;
using HPD.Execution.Contracts;
using HPD.Execution.Runtime;

namespace HPD.Execution.Runtime.Tests;

public sealed class InMemoryExecutionRuntimeTests
{
    [Fact]
    public void Runtime_public_surface_lives_in_runtime_assembly()
    {
        var assembly = typeof(ExecutionProviderRegistry).Assembly;
        var publicTypes = assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("HPD.Execution.Runtime", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal("HPD-Execution.Runtime", assembly.GetName().Name);
        Assert.NotEmpty(publicTypes);
        Assert.All(publicTypes, type => Assert.StartsWith("HPD.Execution.Runtime", type.Namespace));
    }

    [Fact]
    public async Task Registry_registers_provider_families_and_reports_capabilities()
    {
        ExecutionProviderRegistry registry = CreateRegistry();

        IReadOnlyList<ProviderDescriptor> providers = await registry.ListAsync();
        ProviderCapabilityReport report = await registry.GetCapabilitiesAsync(InMemoryExecutionProvider.InMemoryProviderId);

        Assert.Single(providers);
        Assert.NotEmpty(registry.RuntimeHostProviders);
        Assert.NotEmpty(registry.ExecutionUnitProviders);
        Assert.NotEmpty(registry.ProcessProviders);
        Assert.NotEmpty(registry.ProcessIsolationProviders);
        Assert.NotEmpty(registry.FunctionSandboxProviders);
        Assert.NotEmpty(registry.ArtifactProviders);
        Assert.NotEmpty(registry.NetworkProviders);
        Assert.Contains(report.Capabilities, fact => fact.AppliesTo == ProviderContractKind.RuntimeHost && fact.State == CapabilityState.Supported);
        Assert.Contains(report.Capabilities, fact => fact.AppliesTo == ProviderContractKind.ProcessIsolation && fact.State == CapabilityState.Supported);
        Assert.Contains(report.PreflightChecks, check => check.State == PreflightCheckState.Passed);
    }

    [Fact]
    public async Task Planner_selects_provider_for_required_runtime_contracts_and_validates_plan()
    {
        ExecutionProviderRegistry registry = CreateRegistry();
        var planner = new DefaultRuntimePlanner(registry, registry);

        RuntimePlan plan = await planner.PlanAsync(new RuntimePlanRequest
        {
            TopologyPolicy = new RuntimeTopologyPolicy { Mode = RuntimeTopologyMode.OneHostPerRuntime },
            RequestedPlatform = new PlatformSpec("linux", "x64"),
            RequiredContracts = ProviderContractKind.RuntimeHost | ProviderContractKind.ExecutionUnit | ProviderContractKind.ProcessInvocation | ProviderContractKind.ProcessIsolation,
            Capabilities = new CapabilityRequirementSet
            {
                Items =
                [
                    new CapabilityRequirement
                    {
                        Id = new CapabilityId("hpd.execution.host.lifecycle"),
                        AppliesTo = ProviderContractKind.RuntimeHost,
                        Strength = CapabilityRequirementStrength.Required,
                    },
                ],
            },
        });
        RuntimePlanValidationResult validation = await planner.ValidateAsync(plan);

        Assert.True(validation.IsSupported);
        Assert.Empty(plan.UnsupportedReasons);
        Assert.Single(plan.Activations);
        Assert.Contains(plan.Providers, provider => provider.ContractKind == ProviderContractKind.ProcessInvocation);
        Assert.Contains(plan.Providers, provider => provider.ContractKind == ProviderContractKind.ProcessIsolation);
        Assert.Contains(plan.CapabilityCoverage, coverage => coverage.State == CapabilityState.Supported);
    }

    [Fact]
    public async Task Planner_returns_unsupported_reason_when_required_contracts_are_missing()
    {
        var registry = new ExecutionProviderRegistry();
        var planner = new DefaultRuntimePlanner(registry, registry);

        RuntimePlan plan = await planner.PlanAsync(new RuntimePlanRequest
        {
            TopologyPolicy = new RuntimeTopologyPolicy { Mode = RuntimeTopologyMode.OneHostPerExecutionUnit },
            RequiredContracts = ProviderContractKind.RuntimeHost,
        });
        RuntimePlanValidationResult validation = await planner.ValidateAsync(plan);

        Assert.False(validation.IsSupported);
        Assert.Single(plan.UnsupportedReasons);
        Assert.Equal(ExecutionMode.Unsupported, plan.Compatibility.ExecutionMode);
    }

    [Fact]
    public async Task Planner_uses_provider_capability_report_for_permissions_and_activation_model()
    {
        var registry = new ExecutionProviderRegistry();
        registry.RegisterModule(new CapabilityReportModule(
            CapabilityState.Supported,
            new ProviderActivationModel(ProviderActivationKind.SupervisedExecutable, ProviderActivationScope.Runtime, ProviderTransportKind.StdIo, RequiresSupervision: true),
            RequiredPermissions:
            [
                new ProviderPermissionRequirement
                {
                    Id = new PermissionId("com.apple.security.virtualization"),
                    Capability = new CapabilityId("hpd.execution.apple.host.boot"),
                    Required = true,
                    State = PermissionGrantState.Granted,
                    Severity = PermissionSeverity.Info,
                },
            ]));
        var planner = new DefaultRuntimePlanner(registry, registry);

        RuntimePlan plan = await planner.PlanAsync(new RuntimePlanRequest
        {
            TopologyPolicy = new RuntimeTopologyPolicy { Mode = RuntimeTopologyMode.OneHostPerRuntime },
            RequestedPlatform = new PlatformSpec("linux", "arm64"),
            RequiredContracts = ProviderContractKind.RuntimeHost,
            Capabilities = new CapabilityRequirementSet
            {
                Items =
                [
                    new CapabilityRequirement
                    {
                        Id = new CapabilityId("hpd.execution.apple.host.boot"),
                        AppliesTo = ProviderContractKind.RuntimeHost,
                        Strength = CapabilityRequirementStrength.Required,
                    },
                ],
            },
        });
        RuntimePlanValidationResult validation = await planner.ValidateAsync(plan);

        Assert.True(validation.IsSupported);
        ProviderActivationSpec activation = Assert.Single(plan.Activations);
        Assert.Equal(ProviderActivationKind.SupervisedExecutable, activation.ActivationKind);
        Assert.True(activation.Supervisor.RequiresSupervision);
        Assert.Equal(ProviderTransportKind.StdIo, activation.Transport.TransportKind);
        Assert.True(activation.Transport.RequiresStreaming);
        Assert.Contains(activation.RequiredPermissions, permission => permission.Value == "com.apple.security.virtualization");
        Assert.Contains(plan.PermissionPlan, permission => permission.Id.Value == "com.apple.security.virtualization");
        Assert.Contains(plan.CapabilityCoverage, item => item.State == CapabilityState.Supported);
    }

    [Fact]
    public async Task Planner_does_not_treat_planned_or_deferred_capabilities_as_supported()
    {
        foreach (CapabilityState state in new[] { CapabilityState.Planned, CapabilityState.Deferred })
        {
            var registry = new ExecutionProviderRegistry();
            registry.RegisterModule(new CapabilityReportModule(state));
            var planner = new DefaultRuntimePlanner(registry, registry);

            RuntimePlan plan = await planner.PlanAsync(new RuntimePlanRequest
            {
                TopologyPolicy = new RuntimeTopologyPolicy { Mode = RuntimeTopologyMode.OneHostPerRuntime },
                RequiredContracts = ProviderContractKind.RuntimeHost,
                Capabilities = new CapabilityRequirementSet
                {
                    Items =
                    [
                        new CapabilityRequirement
                        {
                            Id = new CapabilityId("hpd.execution.apple.host.boot"),
                            AppliesTo = ProviderContractKind.RuntimeHost,
                            Strength = CapabilityRequirementStrength.Required,
                        },
                    ],
                },
            });
            RuntimePlanValidationResult validation = await planner.ValidateAsync(plan);

            Assert.False(validation.IsSupported);
            Assert.Contains(plan.CapabilityCoverage, coverage => coverage.State == state);
            Assert.Contains(plan.UnsupportedReasons, reason => reason.Code.Value == $"hpd.execution.capability.{state.ToString().ToLowerInvariant()}");
        }
    }

    [Fact]
    public async Task Planner_treats_permission_gated_capability_as_supported_only_when_required_permission_is_granted()
    {
        foreach ((PermissionGrantState permissionState, bool expectedSupported) in new[]
        {
            (PermissionGrantState.Granted, true),
            (PermissionGrantState.PromptRequired, false),
            (PermissionGrantState.Denied, false),
        })
        {
            var registry = new ExecutionProviderRegistry();
            registry.RegisterModule(new CapabilityReportModule(
                CapabilityState.RequiresPermission,
                RequiredPermissions:
                [
                    new ProviderPermissionRequirement
                    {
                        Id = new PermissionId("com.apple.security.virtualization"),
                        Capability = new CapabilityId("hpd.execution.apple.host.boot"),
                        Required = true,
                        State = permissionState,
                        Severity = permissionState == PermissionGrantState.Granted ? PermissionSeverity.Info : PermissionSeverity.Error,
                    },
                ]));
            var planner = new DefaultRuntimePlanner(registry, registry);

            RuntimePlan plan = await planner.PlanAsync(RequiredAppleHostBootRequest());
            RuntimePlanValidationResult validation = await planner.ValidateAsync(plan);

            Assert.Equal(expectedSupported, validation.IsSupported);
            Assert.Contains(plan.CapabilityCoverage, coverage => coverage.State == CapabilityState.RequiresPermission);
            Assert.Contains(plan.PermissionPlan, permission => permission.State == permissionState);
            if (expectedSupported)
            {
                Assert.DoesNotContain(plan.UnsupportedReasons, reason => reason.Code.Value == "hpd.execution.capability.requires-permission");
            }
            else
            {
                Assert.Contains(plan.UnsupportedReasons, reason => reason.Code.Value == "hpd.execution.capability.requires-permission");
            }
        }
    }

    [Fact]
    public async Task Planner_reports_configuration_required_as_remediable_but_not_ready()
    {
        var registry = new ExecutionProviderRegistry();
        registry.RegisterModule(new CapabilityReportModule(CapabilityState.RequiresConfiguration));
        var planner = new DefaultRuntimePlanner(registry, registry);

        RuntimePlan plan = await planner.PlanAsync(RequiredAppleHostBootRequest());
        RuntimePlanValidationResult validation = await planner.ValidateAsync(plan);

        Assert.False(validation.IsSupported);
        Assert.Contains(plan.CapabilityCoverage, coverage => coverage.State == CapabilityState.RequiresConfiguration);
        Assert.Contains(plan.UnsupportedReasons, reason => reason.Code.Value == "hpd.execution.capability.requires-configuration");
        Assert.Contains(plan.UnsupportedReasons, reason => reason.Message.Contains("provider configuration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Runtime_ensures_host_unit_runs_process_and_finalizes_with_cleanup_diagnostic()
    {
        ExecutionProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryExecutionRuntime(registry);
        TargetHandle<ExecutionUnit> unitHandle = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1");

        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host = await runtime.EnsureHostAsync(new RuntimeHostSpec
        {
            Platform = new PlatformSpec("linux", "x64"),
            Bootstrap = new RuntimeHostBootstrapSpec
            {
                ReadinessGates =
                [
                    new ReadinessGateSpec("guest-control", ReadinessGateKind.ProviderCheck, ReadinessGateScope.Provider, new RetryPolicy()),
                ],
            },
        });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit = await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
        {
            PreferredHost = new ResourceRef<RuntimeHost>(host.Metadata.Id, host.Metadata.Scope, host.Metadata.Generation),
        });
        ProcessInvocationResult process = await runtime.RunProcessAsync(new ProcessInvocationSpec
        {
            Target = unitHandle,
            Command = new ProcessCommandSpec { FileName = "/bin/echo", Arguments = ["ready"] },
        });
        RuntimeFinalizationResult finalized = await runtime.FinalizeRuntimeAsync(new RuntimeFinalizationRequest(new ResourceScope("in-memory-runtime"), PromoteMemory: false, CleanupPolicy.Default));

        Assert.Equal(RuntimeHostPhase.Ready, host.Status.HostPhase);
        Assert.True(host.Status.Readiness?.Ready);
        Assert.Equal(ExecutionUnitPhase.Ready, unit.Status.UnitPhase);
        Assert.Equal(ProcessCompletionKind.Completed, process.CompletionKind);
        Assert.Equal(0, process.ExitCode);
        Assert.Contains(finalized.Diagnostics, diagnostic => diagnostic.Code.Value == "hpd.execution.runtime.finalized");
    }

    [Fact]
    public async Task Process_handle_streams_output_and_zero_timeout_returns_timed_out_result()
    {
        ExecutionProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryExecutionRuntime(registry);
        var sink = new RecordingProcessOutputSink();
        var spec = new ProcessInvocationSpec
        {
            Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"),
            Command = new ProcessCommandSpec { FileName = "/bin/work" },
            Policy = new ProcessInvocationPolicy { Timeout = TimeSpan.Zero },
        };

        ProcessInvocationResult result = await runtime.RunProcessAsync(spec, sink);
        await using IProcessInvocationHandle handle = await runtime.StartProcessAsync(spec);
        ProcessInvocationResult waited = await handle.WaitAsync();
        List<ProcessOutputChunk> output = [];
        await foreach (ProcessOutputChunk chunk in handle.ReadOutputAsync())
        {
            output.Add(chunk);
        }

        Assert.Equal(ProcessCompletionKind.TimedOut, result.CompletionKind);
        Assert.True(result.Output.OutputDrainTimedOut);
        Assert.Single(sink.Chunks);
        Assert.Equal(ProcessCompletionKind.Completed, waited.CompletionKind);
        Assert.Single(output);
        Assert.True(output[0].Flags.HasFlag(ProcessOutputChunkFlags.Final));
    }

    [Fact]
    public async Task Runtime_prepares_required_process_isolation_before_starting_process()
    {
        ExecutionProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryExecutionRuntime(registry);
        var spec = new ProcessInvocationSpec
        {
            Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"),
            Command = new ProcessCommandSpec { FileName = "/bin/test" },
            Isolation = new ProcessIsolationPolicy
            {
                Mode = ProcessIsolationMode.Isolated,
                Filesystem = new FilesystemAccessPolicy
                {
                    Rules =
                    [
                        new PathAccessRule { Kind = PathAccessRuleKind.AllowWrite, Path = new HostPath("/workspace"), Reason = "package install updates workspace content" },
                        new PathAccessRule { Kind = PathAccessRuleKind.AllowWrite, Path = new HostPath("/tmp"), Reason = "package install scratch space" },
                        new PathAccessRule { Kind = PathAccessRuleKind.DenyRead, Path = new HostPath("/home/agent/.ssh"), Reason = "credentials are not needed for public package install" },
                        new PathAccessRule { Kind = PathAccessRuleKind.DenyWrite, Path = new HostPath("/workspace/.git/hooks"), Reason = "package install must not mutate repository hooks" },
                    ],
                },
                Network = new NetworkEgressPolicy
                {
                    Mode = NetworkEgressMode.Filtered,
                    AllowedDomains =
                    [
                        new DomainRule { Pattern = "registry.npmjs.org", Kind = DomainRuleKind.ExactHost },
                        new DomainRule { Pattern = "*.github.com", Kind = DomainRuleKind.WildcardSubdomain },
                    ],
                    RequireProxyMediation = true,
                },
                UnixSockets = UnixSocketAccessPolicy.None,
                Environment = new EnvironmentAccessPolicy
                {
                    AllowedVariables = ["PATH", "HOME", "TMPDIR"],
                    StripUnlistedVariables = true,
                },
                Violations = new ProcessViolationPolicy
                {
                    Action = ProcessViolationAction.ObserveAndFailInvocation,
                    ObservationTailLimit = 50,
                },
            },
        };

        await using IProcessInvocationHandle handle = await runtime.StartProcessAsync(spec);

        Assert.Contains(handle.Spec.ProviderExtensions, extension => extension.SchemaId.Value == "hpd.execution.process-isolation.in-memory");
    }

    [Fact]
    public async Task Function_sandbox_resolves_guest_binary_invokes_function_and_reports_timeout_poison_status()
    {
        ExecutionProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryExecutionRuntime(registry);
        var guestBinary = Ref<ContentArtifact>("guest-binary", "content-artifact");
        var observations = new RecordingFunctionObservationSink();

        ResourceSnapshot<FunctionSandbox, FunctionSandboxSpec, FunctionSandboxStatus> sandbox = await runtime.EnsureFunctionSandboxAsync(new FunctionSandboxSpec
        {
            GuestBinary = guestBinary,
            RequiredGuestAbi = new GuestAbiSpec("hyperlight", "x64"),
        });
        FunctionInvocationResult returned = await runtime.InvokeFunctionAsync(new FunctionInvocationSpec
        {
            Sandbox = sandbox.Status.Handle!.Value,
            Function = new FunctionName("add"),
            ExpectedReturn = new FunctionReturnType(FunctionValueKind.Int32),
        }, observations);
        FunctionInvocationResult timedOut = await runtime.InvokeFunctionAsync(new FunctionInvocationSpec
        {
            Sandbox = sandbox.Status.Handle.Value,
            Function = new FunctionName("slow"),
            Policy = new FunctionInvocationPolicy { Timeout = TimeSpan.Zero },
        });

        Assert.Equal(FunctionSandboxPhase.Ready, sandbox.Status.SandboxPhase);
        Assert.Equal(guestBinary, sandbox.Status.ResolvedGuestBinary);
        Assert.Equal(FunctionInvocationCompletionKind.Returned, returned.CompletionKind);
        Assert.Equal(FunctionValueKind.Int32, returned.ReturnValue.Kind);
        Assert.Single(observations.Events);
        Assert.Equal(FunctionInvocationCompletionKind.TimedOut, timedOut.CompletionKind);
        Assert.True(timedOut.Poison?.Restorable);
    }

    [Fact]
    public async Task Provider_resolves_artifact_materializes_rootfs_projects_workspace_and_syncs()
    {
        ExecutionProviderRegistry registry = CreateRegistry();
        InMemoryExecutionProvider provider = (InMemoryExecutionProvider)registry.ArtifactProviders[0];
        ResourceMetadata<ContentArtifact> artifactMetadata = Metadata<ContentArtifact>("artifact-1", "content-artifact");
        ResourceRef<ContentArtifact> artifactRef = new(artifactMetadata.Id, artifactMetadata.Scope, artifactMetadata.Generation);
        ResourceMetadata<RootFilesystemView> rootfsMetadata = Metadata<RootFilesystemView>("rootfs-1", "root-filesystem-view");
        ResourceMetadata<ContentProjection> projectionMetadata = Metadata<ContentProjection>("projection-1", "content-projection");

        ContentArtifactStatus artifact = await provider.ResolveAsync(artifactMetadata, new ContentArtifactSpec
        {
            Kind = ContentArtifactKind.ContainerRootfsImage,
            Reference = new ArtifactReference { Original = "docker.io/library/alpine:latest" },
        });
        ContentArtifactStatus functionArtifact = await provider.ResolveAsync(Metadata<ContentArtifact>("function-guest", "content-artifact"), new ContentArtifactSpec
        {
            Kind = ContentArtifactKind.FunctionGuestBinary,
            Reference = new ArtifactReference { Original = "host://function.wasm" },
            FunctionGuest = new FunctionGuestBinaryOptions(ArtifactFormat.Elf, new GuestAbiSpec("hyperlight", "x64")),
        });
        RootFilesystemViewStatus rootfs = await provider.MaterializeAsync(rootfsMetadata, new RootFilesystemViewSpec { Image = artifactRef }, host: null, unit: null);
        ContentProjectionStatus projection = await provider.ProjectAsync(projectionMetadata, WorkspaceProjection(), host: null, unit: null);
        SyncResult sync = await provider.SyncAsync(Handle<ContentProjection>(TargetRouteSegmentKind.ContentProjection, "projection-1"), new SyncRequest { OverrideMode = SyncMode.Manual });

        Assert.Equal(ContentArtifactPhase.Available, artifact.ArtifactPhase);
        Assert.Equal(ContentArtifactKind.ContainerRootfsImage, artifact.Kind);
        Assert.Equal(ContentArtifactPhase.Available, functionArtifact.ArtifactPhase);
        Assert.True(functionArtifact.FunctionGuest?.Compatible);
        Assert.Equal(RootFilesystemViewPhase.Materialized, rootfs.RootfsPhase);
        Assert.Equal(ContentProjectionPhase.Projected, projection.ProjectionPhase);
        Assert.Single(projection.Views);
        Assert.True(sync.Checkpoint.Version > 0);
    }

    [Fact]
    public async Task Provider_realizes_network_membership_endpoint_and_authority_binding()
    {
        ExecutionProviderRegistry registry = CreateRegistry();
        InMemoryExecutionProvider provider = (InMemoryExecutionProvider)registry.NetworkProviders[0];
        ResourceMetadata<Network> networkMetadata = Metadata<Network>("network-1", "network");
        ResourceRef<Network> networkRef = new(networkMetadata.Id, networkMetadata.Scope, networkMetadata.Generation);
        ResourceMetadata<NetworkMembership> membershipMetadata = Metadata<NetworkMembership>("membership-1", "network-membership");
        ResourceRef<NetworkMembership> membershipRef = new(membershipMetadata.Id, membershipMetadata.Scope, membershipMetadata.Generation);

        NetworkStatus network = await provider.EnsureNetworkAsync(networkMetadata, new NetworkSpec
        {
            Scope = NetworkScope.Runtime,
            ConnectivityIntent = NetworkConnectivityIntent.NatEgress,
            AddressFamilies = AddressFamilyRequirement.IPv4Required,
        }, observed: null);
        NetworkMembershipStatus membership = await provider.EnsureMembershipAsync(membershipMetadata, new NetworkMembershipSpec
        {
            Network = networkRef,
            Target = new NetworkMembershipTarget(NetworkMembershipTargetKind.ExecutionUnit, Host: null, Unit: Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"), Process: null),
            ServiceNames = [new ServiceName("web")],
        }, observed: null);
        PublishedEndpointStatus endpoint = await provider.EnsurePublishedEndpointAsync(Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"), new PublishedEndpointSpec
        {
            Listener = new EndpointListenerSpec(EndpointListenerKind.HostAddress, NetworkTransport.Tcp, Address: null, Ports: new PortRange(new NetworkPort(8080), 1), Socket: null),
            Target = new EndpointRouteTarget(EndpointTargetKind.NetworkMembership, membershipRef, Unit: null, Process: null, ServiceName: null, NetworkTransport.Tcp, new NetworkPort(80), SocketPath: null),
            SensitivePolicy = new SensitiveEndpointPolicy { Kind = SensitiveEndpointKind.EngineSocket, AuthorityClass = SensitiveAuthorityClass.RootfulEngineControl },
        }, observed: null);
        AuthorityBindingStatus authority = await provider.EnsureAuthorityBindingAsync(Metadata<AuthorityBinding>("authority-1", "authority-binding"), HostFunctionBinding(), observed: null);

        Assert.Equal(NetworkPhase.Ready, network.NetworkPhase);
        Assert.True(network.RealizedCapabilities.HasFlag(NetworkCapabilitySet.TcpPublish));
        Assert.Equal(NetworkMembershipPhase.Ready, membership.MembershipPhase);
        Assert.Single(membership.RegisteredRecords);
        Assert.Equal(PublishedEndpointPhase.Bound, endpoint.EndpointPhase);
        Assert.Equal(AuthorityBindingPhase.Projected, authority.BindingPhase);
        Assert.Equal(RevocationVerificationStatus.Verified, authority.BoundAuthority?.RevocationStatus);
        Assert.NotNull(authority.BoundAuthority?.AuditCorrelationId);
    }

    private static ExecutionProviderRegistry CreateRegistry()
    {
        var registry = new ExecutionProviderRegistry();
        registry.RegisterModule(new InMemoryExecutionProviderModule());
        return registry;
    }

    private static ContentProjectionSpec WorkspaceProjection() =>
        new()
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.WorkspacePath,
                Workspace = Ref<Workspace>("workspace-1", "workspace"),
                WorkspaceRole = ContentProjectionRole.Workspace,
                PathPrefix = "/",
            },
            Target = new ContentProjectionTarget
            {
                Host = Ref<RuntimeHost>("host-1", "runtime-host"),
            },
            View = new ProjectionView { Kind = ProjectionViewKind.FilesystemTree, GuestPath = new GuestPath("/workspace") },
            Role = ContentProjectionRole.Workspace,
            AccessMode = AccessMode.ReadWrite,
            SyncPolicy = SyncPolicy.InitialOnly,
        };

    private static AuthorityBindingSpec HostFunctionBinding() =>
        new()
        {
            Kind = AuthorityBindingKind.HostFunction,
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.HostFunction,
                HostFunction = new HostFunctionBindingProfile(
                    new HostFunctionName("read_secret"),
                    new FunctionSignature
                    {
                        Name = new FunctionName("read_secret"),
                        ReturnType = new FunctionReturnType(FunctionValueKind.String),
                    }),
            },
            Target = new AuthorityBindingTarget(AuthorityTargetKind.FunctionSandbox, FunctionSandbox: Handle<FunctionSandbox>(TargetRouteSegmentKind.FunctionSandbox, "sandbox-1"), Locus: BoundaryLocus.FunctionSandbox),
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.TypedCallback,
                CallbackSignature = new FunctionSignature
                {
                    Name = new FunctionName("read_secret"),
                    ReturnType = new FunctionReturnType(FunctionValueKind.String),
                },
            },
            Policy = new AuthorityBindingPolicy
            {
                Direction = AuthorityBindingDirection.FunctionGuestToHost,
                AuthorityClass = SensitiveAuthorityClass.HostFunctionCallback,
                EffectiveAuthorityClass = SensitiveAuthorityClass.HostFunctionCallback,
                RequireAudit = true,
            },
        };

    private static RuntimePlanRequest RequiredAppleHostBootRequest() =>
        new()
        {
            TopologyPolicy = new RuntimeTopologyPolicy { Mode = RuntimeTopologyMode.OneHostPerRuntime },
            RequiredContracts = ProviderContractKind.RuntimeHost,
            Capabilities = new CapabilityRequirementSet
            {
                Items =
                [
                    new CapabilityRequirement
                    {
                        Id = new CapabilityId("hpd.execution.apple.host.boot"),
                        AppliesTo = ProviderContractKind.RuntimeHost,
                        Strength = CapabilityRequirementStrength.Required,
                    },
                ],
            },
        };

    private static ResourceMetadata<T> Metadata<T>(string id, string kind)
        where T : IExecutionResourceMarker =>
        new()
        {
            Id = new ResourceId<T>(id),
            Kind = new ResourceKind(kind),
            Scope = new ResourceScope("test-runtime"),
            SchemaVersion = new SchemaVersion("v1"),
            Generation = new ResourceGeneration(1),
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

    private static ResourceRef<T> Ref<T>(string id, string kind)
        where T : IExecutionResourceMarker =>
        new(new ResourceId<T>(id), new ResourceScope("test-runtime"), new ResourceGeneration(1));

    private static TargetHandle<T> Handle<T>(TargetRouteSegmentKind kind, string id)
        where T : IOperationTargetMarker =>
        new(
            new TargetRoute
            {
                Kind = new TargetKind(typeof(T).Name),
                Scope = new ResourceScope("test-runtime"),
                Segments = [new TargetRouteSegment(kind, id)],
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Invoke);

    private sealed class RecordingProcessOutputSink : IProcessOutputSink
    {
        public List<ProcessOutputChunk> Chunks { get; } = [];

        public ValueTask OnOutputAsync(ProcessOutputChunk chunk, CancellationToken cancellationToken = default)
        {
            Chunks.Add(chunk);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingFunctionObservationSink : IFunctionObservationSink
    {
        public List<ExecutionEventChunk> Events { get; } = [];

        public ValueTask OnFunctionEventAsync(ExecutionEventChunk chunk, CancellationToken cancellationToken = default)
        {
            Events.Add(chunk);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapabilityReportModule(
        CapabilityState state,
        ProviderActivationModel? activationModel = null,
        IReadOnlyList<ProviderPermissionRequirement>? RequiredPermissions = null) : IProviderModule, IProviderCapabilityReporter
    {
        private static readonly ProviderId Id = new("hpd.execution.test-capability-report");

        public ProviderDescriptor Descriptor { get; } = new()
        {
            Id = Id,
            DisplayName = "Capability Report Test Provider",
            ContractVersion = new SemanticVersion(1, 0, 0),
            ProviderVersion = new SemanticVersion(1, 0, 0),
            ContractKinds = ProviderContractKind.RuntimeHost,
            TrustLevel = ProviderTrustLevel.BuiltIn,
            DefaultActivationScope = ProviderActivationScope.Runtime,
            ActivationModels =
            [
                activationModel ?? new ProviderActivationModel(ProviderActivationKind.InProcess, ProviderActivationScope.Runtime, ProviderTransportKind.None),
            ],
        };

        public void Register(IProviderRegistrationBuilder builder) => builder.AddProviderCapabilityReporter(this);

        public void RegisterJsonTypes(IProviderJsonTypeRegistry registry)
        {
        }

        public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, CancellationToken cancellationToken = default) =>
            GetCapabilitiesAsync(providerId, new ProviderCapabilityQuery(CapabilityRequirementSet.Empty), cancellationToken);

        public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, ProviderCapabilityQuery query, CancellationToken cancellationToken = default) =>
            new(new ProviderCapabilityReport
            {
                ProviderId = providerId,
                HostPlatform = new PlatformSpec("macos", "arm64"),
                Capabilities =
                [
                    new CapabilityFact
                    {
                        Id = new CapabilityId("hpd.execution.apple.host.boot"),
                        Category = new CapabilityCategory("runtime-host"),
                        AppliesTo = ProviderContractKind.RuntimeHost,
                        State = state,
                        Detail = $"test capability is {state}",
                    },
                ],
                RequiredPermissions = RequiredPermissions ?? Array.Empty<ProviderPermissionRequirement>(),
                PreflightChecks =
                [
                    new ProviderPreflightCheck("virtualization-entitlement", PreflightCheckState.Passed),
                ],
            });
    }
}
