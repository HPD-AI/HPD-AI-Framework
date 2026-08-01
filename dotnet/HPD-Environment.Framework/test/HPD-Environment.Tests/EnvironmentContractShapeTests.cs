using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Environment.Contracts;

namespace HPD.Environment.Contracts.Tests;

public sealed class ExecutionContractShapeTests
{
    [Fact]
    public void Public_contract_uses_execution_namespace_and_not_container_legacy_names()
    {
        Type[] publicTypes = ContractTypes();

        Assert.All(publicTypes, type => Assert.StartsWith("HPD.Environment.Contracts", type.Namespace));
        Assert.Contains(publicTypes, type => type.Name == nameof(RuntimeHost));
        Assert.Contains(publicTypes, type => type.Name == nameof(ExecutionUnit));
        Assert.Contains(publicTypes, type => type.Name == nameof(ProcessInvocation));
        Assert.Contains(publicTypes, type => type.Name == nameof(FunctionSandbox));
        Assert.DoesNotContain(publicTypes, type => type.Name.Contains("SessionMachine", StringComparison.Ordinal));
        Assert.DoesNotContain(publicTypes, type => type.Name.Contains("ContainerWorkload", StringComparison.Ordinal));
        Assert.DoesNotContain(publicTypes, type => type.Name.Contains("Materialization", StringComparison.Ordinal));
        Assert.DoesNotContain(publicTypes, type => type.Namespace?.Contains("Container", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Durable_resource_identity_is_typed_and_snapshot_preserves_spec_status_metadata()
    {
        var metadata = Metadata<RuntimeHost>("host-1", "runtime-host", ResourceLifetime.Runtime);
        var spec = new RuntimeHostSpec
        {
            Platform = new PlatformSpec("linux", "arm64"),
            TopologyPolicy = new RuntimeTopologyPolicy
            {
                Mode = RuntimeTopologyMode.OneHostPerRuntime,
                RetainEmptyHost = true,
            },
        };
        var status = new RuntimeHostStatus
        {
            HostPhase = RuntimeHostPhase.Ready,
            Phase = ResourcePhase.Ready,
            Conditions =
            [
                new Condition("Ready", ConditionStatus.True, "Booted", "guest control reachable", DateTimeOffset.UnixEpoch, new ResourceGeneration(7)),
            ],
        };

        var snapshot = new ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>(metadata, spec, status);
        IResource<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> resource = snapshot;

        Assert.Equal("host-1", resource.Metadata.Id.Value);
        Assert.Equal("runtime-host", resource.Metadata.Kind.Value);
        Assert.Equal(RuntimeTopologyMode.OneHostPerRuntime, resource.Spec.TopologyPolicy.Mode);
        Assert.Equal(RuntimeHostPhase.Ready, resource.Status.HostPhase);
        Assert.Equal(ResourcePhase.Ready, resource.Status.Phase);
        Assert.Single(resource.Status.Conditions);
    }

    [Fact]
    public void Runtime_host_execution_unit_and_process_lane_are_separate_contracts()
    {
        var hostRef = Ref<RuntimeHost>("host-1", "runtime-host");
        var rootfsRef = Ref<RootFilesystemView>("rootfs-1", "root-filesystem-view");
        var unitHandle = Handle<ExecutionUnit>("runtime-host", "host-1", "execution-unit", "unit-1");

        var process = new ProcessInvocationSpec
        {
            Target = unitHandle,
            Role = ProcessRole.Primary,
            Command = new ProcessCommandSpec
            {
                FileName = "/bin/sh",
                Arguments = ["-lc", "echo ready"],
                WorkingDirectory = "/workspace",
                Environment = new Dictionary<string, string?> { ["HPD_RUNTIME"] = "true" },
            },
            Policy = new ProcessInvocationPolicy
            {
                Timeout = TimeSpan.FromSeconds(30),
                Stop = new StopPolicy { Kind = StopKind.GracefulThenKill, GracePeriod = TimeSpan.FromSeconds(3) },
            },
        };

        var unit = new ExecutionUnitSpec
        {
            PreferredHost = hostRef,
            Rootfs = rootfsRef,
            DefaultProcess = process,
            LifecyclePolicy = new LifecyclePolicy
            {
                StopExecutionUnitOnPrimaryExit = true,
                StopHostWhenEmpty = false,
            },
        };

        Assert.Equal(hostRef, unit.PreferredHost);
        Assert.Equal(rootfsRef, unit.Rootfs);
        Assert.Equal(ProcessRole.Primary, unit.DefaultProcess?.Role);
        Assert.True(unit.DefaultProcess?.Policy.StopProcessTree ?? false);
        Assert.Equal(TimeSpan.FromSeconds(2), ProcessInvocationPolicy.Default.OutputDrainTimeout);
        Assert.False(unit.LifecyclePolicy.StopHostWhenEmpty);
    }

    [Fact]
    public void Process_results_model_output_drain_truncation_and_completion()
    {
        var result = new ProcessInvocationResult
        {
            ProcessId = new ResourceId<ProcessInvocation>("proc-1"),
            CompletionKind = ProcessCompletionKind.TimedOut,
            ExitCode = null,
            Output = new ProcessCapturedOutput
            {
                Stdout = new ProcessStreamOutput
                {
                    CapturedBytes = "hello"u8.ToArray(),
                    BytesObserved = 10,
                    BytesCaptured = 5,
                    BytesDiscarded = 5,
                    Truncated = true,
                },
                Stderr = new ProcessStreamOutput(),
                OutputDrainTimedOut = true,
                OutputDrainTimeout = TimeSpan.FromSeconds(2),
            },
            Violations = [new ProcessViolation("timeout", "process exceeded timeout")],
        };

        Assert.Equal(ProcessCompletionKind.TimedOut, result.CompletionKind);
        Assert.True(result.Output.Stdout.Truncated);
        Assert.Equal(5, result.Output.Stdout.BytesDiscarded);
        Assert.True(result.Output.OutputDrainTimedOut);
        Assert.Single(result.Violations);
    }

    [Fact]
    public void Artifact_rootfs_workspace_and_projection_contracts_are_distinct()
    {
        var artifact = Ref<ContentArtifact>("alpine", "content-artifact");
        var workspace = Ref<Workspace>("workspace-1", "workspace");

        var artifactSpec = new ContentArtifactSpec
        {
            Kind = ContentArtifactKind.ContainerRootfsImage,
            Reference = new ArtifactReference
            {
                Original = "docker.io/library/alpine:3.20",
                Registry = "docker.io",
                Repository = "library/alpine",
                Tag = "3.20",
            },
            RequestedPlatform = new PlatformSelector("linux", "arm64"),
        };
        var rootfs = new RootFilesystemViewSpec
        {
            Image = artifact,
            AccessMode = RootfsAccessMode.ReadOnlyBaseWithWritableOverlay,
            ReusePolicy = RootfsReusePolicy.ShareBaseLayers,
        };
        var projection = new ContentProjectionSpec
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.WorkspacePath,
                Workspace = workspace,
                WorkspaceRole = ContentProjectionRole.Workspace,
                PathPrefix = "/",
            },
            Target = new ContentProjectionTarget
            {
                Host = Ref<RuntimeHost>("host-1", "runtime-host"),
                TargetName = "workspace",
            },
            View = new ProjectionView { Kind = ProjectionViewKind.FilesystemTree, GuestPath = new GuestPath("/workspace") },
            Role = ContentProjectionRole.Workspace,
            AccessMode = AccessMode.ReadWrite,
            Realization = new ProjectionRealizationSpec
            {
                Kind = ProjectionRealizationKind.LiveProjection,
                WriteEffect = ProjectionWriteEffect.DirectSourceMutation,
                RequestedCoherence = CoherenceClass.CloseToOpen,
            },
            SyncPolicy = new SyncPolicy(SyncMode.Continuous, SyncDirection.Bidirectional, ConflictPolicy.RecordConflict, IncludeDeletes: true),
        };

        Assert.Equal(ContentArtifactKind.ContainerRootfsImage, artifactSpec.Kind);
        Assert.Equal(artifact, rootfs.Image);
        Assert.Equal(ProjectionViewKind.FilesystemTree, projection.View.Kind);
        Assert.Equal(ProjectionWriteEffect.DirectSourceMutation, projection.Realization.WriteEffect);
        Assert.Equal(SyncMode.Continuous, projection.SyncPolicy.Mode);
    }

    [Fact]
    public void Network_membership_publication_and_sensitive_authority_are_independent()
    {
        var networkRef = Ref<Network>("net-1", "network");
        var membershipRef = Ref<NetworkMembership>("member-1", "network-membership");

        var network = new NetworkSpec
        {
            Scope = NetworkScope.Runtime,
            ConnectivityIntent = NetworkConnectivityIntent.NatEgress,
            AddressFamilies = AddressFamilyRequirement.IPv4Required,
            ExposurePolicy = new NetworkExposurePolicy { AllowPublishedEndpoints = true },
        };
        var membership = new NetworkMembershipSpec
        {
            Network = networkRef,
            Target = new NetworkMembershipTarget(NetworkMembershipTargetKind.ExecutionUnit, Host: null, Unit: Handle<ExecutionUnit>("executionunit", "unit-1"), Process: null),
            Hostname = new ScopedName("app"),
            ServiceNames = [new ServiceName("web")],
        };
        var endpoint = new PublishedEndpointSpec
        {
            Listener = new EndpointListenerSpec(EndpointListenerKind.HostAddress, NetworkTransport.Tcp, Address: null, Ports: new PortRange(new NetworkPort(8080), 1), Socket: null),
            Target = new EndpointRouteTarget(EndpointTargetKind.NetworkMembership, Membership: membershipRef, Unit: null, Process: null, ServiceName: null, Transport: NetworkTransport.Tcp, Port: new NetworkPort(80), SocketPath: null),
            SensitivePolicy = new SensitiveEndpointPolicy
            {
                Kind = SensitiveEndpointKind.EngineSocket,
                AuthorityClass = SensitiveAuthorityClass.RootfulEngineControl,
                RequireAudit = true,
            },
        };

        Assert.Equal(NetworkConnectivityIntent.NatEgress, network.ConnectivityIntent);
        Assert.Equal(NetworkMembershipTargetKind.ExecutionUnit, membership.Target.Kind);
        Assert.Equal(membershipRef, endpoint.Target.Membership);
        Assert.True(endpoint.SensitivePolicy?.RequireAudit);
    }

    [Fact]
    public void Authority_binding_covers_host_functions_credentials_endpoints_and_revocation_policy()
    {
        var binding = new AuthorityBindingSpec
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
            Target = new AuthorityBindingTarget(AuthorityTargetKind.FunctionSandbox, FunctionSandbox: Handle<FunctionSandbox>("functionsandbox", "sandbox-1"), Locus: BoundaryLocus.FunctionSandbox),
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
                Lease = new SensitiveLeasePolicy { Lifetime = BindingLifetime.FunctionSandbox, RevokeOnTargetStop = true },
                RequireAudit = true,
            },
            AuditLabel = "host function callback",
        };

        Assert.Equal(AuthorityBindingKind.HostFunction, binding.Kind);
        Assert.Equal(AuthorityProjectionKind.TypedCallback, binding.Projection.Kind);
        Assert.Equal(AuthorityBindingDirection.FunctionGuestToHost, binding.Policy.Direction);
        Assert.True(binding.Policy.RequireAudit);
        Assert.True(binding.Policy.Lease.RevokeOnTargetStop);
    }

    [Fact]
    public void Function_sandbox_lane_has_typed_invocation_cancellation_poison_and_snapshot_contracts()
    {
        var sandboxHandle = Handle<FunctionSandbox>("function-sandbox", "sandbox-1");
        var invocation = new FunctionInvocationSpec
        {
            Sandbox = sandboxHandle,
            Function = new FunctionName("add"),
            Arguments =
            [
                new FunctionArgument("left", new FunctionValue(FunctionValueKind.Int32, Int32: 2)),
                new FunctionArgument("right", new FunctionValue(FunctionValueKind.Int32, Int32: 3)),
            ],
            ExpectedReturn = new FunctionReturnType(FunctionValueKind.Int32),
            Policy = new FunctionInvocationPolicy
            {
                Timeout = TimeSpan.FromSeconds(1),
                Cancellation = FunctionCancellationPolicy.DiscardSandbox,
                RestoreSandboxOnPoisonWhenPossible = true,
            },
        };
        var result = new FunctionInvocationResult
        {
            CompletionKind = FunctionInvocationCompletionKind.SandboxPoisoned,
            Poison = new FunctionSandboxPoisonStatus(true, FunctionPoisonReason.InvalidMemoryAccess, Restorable: true),
        };
        var snapshot = new FunctionSandboxSnapshotSpec
        {
            Sandbox = Ref<FunctionSandbox>("sandbox-1", "function-sandbox"),
            Label = "initialized",
            Retention = RetentionPolicy.Runtime,
        };

        Assert.Equal(FunctionValueKind.Int32, invocation.Arguments[0].Value.Kind);
        Assert.Equal(FunctionCancellationPolicy.DiscardSandbox, invocation.Policy.Cancellation);
        Assert.True(result.Poison?.Restorable);
        Assert.Equal(RetentionPolicy.Runtime, snapshot.Retention);
    }

    [Fact]
    public void Provider_model_exposes_descriptor_capabilities_activation_preflight_and_contract_bits()
    {
        var descriptor = new ProviderDescriptor
        {
            Id = new ProviderId("test-provider"),
            DisplayName = "Test Provider",
            ContractVersion = new SemanticVersion(1, 0, 0),
            ProviderVersion = new SemanticVersion(1, 2, 3),
            ContractKinds = ProviderContractKind.RuntimeHost | ProviderContractKind.ExecutionUnit | ProviderContractKind.ProcessInvocation | ProviderContractKind.FunctionSandbox | ProviderContractKind.AuthorityBinding,
            TrustLevel = ProviderTrustLevel.BuiltIn,
            SupportedActivationScopes = [ProviderActivationScope.Runtime, ProviderActivationScope.FunctionSandbox],
            ActivationModels = [new ProviderActivationModel(ProviderActivationKind.InProcess, ProviderActivationScope.Runtime, ProviderTransportKind.None)],
        };
        var report = new ProviderCapabilityReport
        {
            ProviderId = descriptor.Id,
            Capabilities =
            [
                new CapabilityFact
                {
                    Id = new CapabilityId("hpd.execution.function.invoke"),
                    Category = new CapabilityCategory("function"),
                    AppliesTo = ProviderContractKind.FunctionInvocation,
                    State = CapabilityState.RequiresPermission,
                    Constraints = [new CapabilityConstraint(CapabilityConstraintKind.ProviderDefined, "surrogate", Required: "available")],
                },
            ],
            RequiredPermissions =
            [
                new ProviderPermissionRequirement
                {
                    Id = new PermissionId("hypervisor"),
                    Capability = new CapabilityId("hpd.execution.function.invoke"),
                    Required = true,
                    State = PermissionGrantState.PromptRequired,
                    Severity = PermissionSeverity.Error,
                    Checks = [new PermissionCheck("hypervisor", PreflightCheckState.RequiresRemediation)],
                },
            ],
        };

        Assert.True(descriptor.ContractKinds.HasFlag(ProviderContractKind.RuntimeHost));
        Assert.True(descriptor.ContractKinds.HasFlag(ProviderContractKind.FunctionSandbox));
        Assert.True(descriptor.ContractKinds.HasFlag(ProviderContractKind.AuthorityBinding));
        Assert.Equal(CapabilityState.RequiresPermission, report.Capabilities[0].State);
        Assert.Equal(PermissionGrantState.PromptRequired, report.RequiredPermissions[0].State);
    }

    [Fact]
    public void Runtime_plan_can_model_activation_steps_unsupported_reasons_and_validation()
    {
        var request = new RuntimePlanRequest
        {
            TopologyPolicy = new RuntimeTopologyPolicy { Mode = RuntimeTopologyMode.OneHostPerExecutionUnit, StopHostOnPrimaryExit = true },
            RequestedPlatform = new PlatformSpec("linux", "x64"),
            RequiredContracts = ProviderContractKind.RuntimeHost | ProviderContractKind.ExecutionUnit | ProviderContractKind.ProcessInvocation,
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
        };
        var activation = new ProviderActivationSpec
        {
            ProviderId = new ProviderId("apple-container"),
            Scope = ProviderActivationScope.Runtime,
            ScopeKey = "runtime-1",
            RequiredContracts = request.RequiredContracts,
        };
        var plan = new RuntimePlan
        {
            Id = new RuntimePlanId("plan-1"),
            TopologyPolicy = request.TopologyPolicy,
            Compatibility = new PlatformCompatibilityPlan
            {
                RequestedPlatform = request.RequestedPlatform.Value,
                ExecutionMode = ExecutionMode.Native,
            },
            ActivationSteps =
            [
                new RuntimePlanActivationStep
                {
                    Id = new RuntimePlanStepId("activate-runtime"),
                    Activation = activation,
                    ExpectedComponents = [new ProviderComponentExpectation(ProviderComponentKind.Supervisor, "container")],
                },
            ],
            CapabilityCoverage =
            [
                new CapabilityCoverage(new CapabilityId("hpd.execution.host.lifecycle"), CapabilityRequirementStrength.Required, CapabilityState.Supported, activation.ProviderId),
            ],
        };
        var validation = new RuntimePlanValidationResult { IsSupported = true };

        Assert.Equal(RuntimeTopologyMode.OneHostPerExecutionUnit, plan.TopologyPolicy.Mode);
        Assert.Single(plan.ActivationSteps);
        Assert.Empty(plan.UnsupportedReasons);
        Assert.True(validation.IsSupported);
    }

    [Fact]
    public void Provider_module_registration_keeps_all_provider_families_explicit()
    {
        var builder = new RecordingProviderRegistrationBuilder();
        var module = new TestProviderModule();

        module.Register(builder);

        Assert.Contains(nameof(IProviderRegistrationBuilder.AddRuntimeHostProvider), builder.Calls);
        Assert.Contains(nameof(IProviderRegistrationBuilder.AddRuntimeHostWakeReconciliationProvider), builder.Calls);
        Assert.Contains(nameof(IProviderRegistrationBuilder.AddRuntimeHostResetProvider), builder.Calls);
        Assert.Contains(nameof(IProviderRegistrationBuilder.AddExecutionUnitProvider), builder.Calls);
        Assert.Contains(nameof(IProviderRegistrationBuilder.AddProcessProvider), builder.Calls);
        Assert.Contains(nameof(IProviderRegistrationBuilder.AddFunctionSandboxProvider), builder.Calls);
        Assert.Contains(nameof(IProviderRegistrationBuilder.AddAuthorityBindingProvider), builder.Calls);
        Assert.Contains(nameof(IProviderRegistrationBuilder.AddProviderCapabilityReporter), builder.Calls);
        Assert.Contains(nameof(IProviderRegistrationBuilder.AddProviderActivator), builder.Calls);
        Assert.Contains(nameof(IProviderRegistrationBuilder.AddEngineControlPlaneProvider), builder.Calls);
    }

    [Fact]
    public void Source_generated_json_metadata_covers_first_slice_dtos()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = ExecutionContractJsonContext.Default };
        var spec = new RuntimeHostSpec { Platform = new PlatformSpec("linux", "x64") };
        var json = JsonSerializer.Serialize(spec, ExecutionContractJsonContext.Default.RuntimeHostSpec);
        var roundTrip = JsonSerializer.Deserialize(json, ExecutionContractJsonContext.Default.RuntimeHostSpec);

        Assert.NotNull(roundTrip);
        Assert.Equal("linux", roundTrip.Platform.OperatingSystem);
        Assert.NotNull(options.TypeInfoResolver.GetTypeInfo(typeof(ProcessInvocationResult), options));
        Assert.NotNull(options.TypeInfoResolver.GetTypeInfo(typeof(FunctionInvocationResult), options));
        Assert.NotNull(options.TypeInfoResolver.GetTypeInfo(typeof(RuntimePlan), options));
        Assert.NotNull(options.TypeInfoResolver.GetTypeInfo(typeof(AuthorityBindingSpec), options));
        Assert.NotNull(options.TypeInfoResolver.GetTypeInfo(typeof(ResourceSnapshotEnvelope), options));
        Assert.NotNull(options.TypeInfoResolver.GetTypeInfo(typeof(ExecutionResourceQuery), options));
    }

    [Fact]
    public void Durable_storage_contracts_separate_capacity_physical_devices_and_app_data()
    {
        var volume = new DurableVolumeSpec
        {
            LogicalId = "app-installation/workload/postgres-data",
            OwnerScopeId = "app-installation",
            OwnerResourceId = "backend",
            DeclarationId = "postgres-data",
            Pool = Ref<StoragePool>("app-data", "storage-pool"),
            MinimumBytes = new ByteSize(1_073_741_824),
            MaximumBytes = new ByteSize(21_474_836_480),
            Retention = DurableVolumeRetention.RetainOnRemove,
            BackupEligible = true,
            Filesystem = GuestFilesystemProvisioning.Ext4,
            Encryption = StorageEncryptionRequirement.Required,
            CompatibilityDomain = "penpot-postgres-v1",
        };

        Assert.Equal("app-data", volume.Pool.Id.Value);
        Assert.True(volume.BackupEligible);
        Assert.Equal(DurableVolumeRetention.RetainOnRemove, volume.Retention);
        Assert.NotEqual(typeof(BlockVolume), typeof(DurableVolume));
        Assert.NotEqual(
            ProviderContractKind.BlockVolume,
            ProviderContractKind.DurableVolume);
    }

    [Fact]
    public void Durable_storage_specs_round_trip_through_source_generated_metadata()
    {
        var spec = new StorageReservationSpec
        {
            Pool = Ref<StoragePool>("app-data", "storage-pool"),
            OperationId = "restore-1",
            Owner = "io.penpot.penpot",
            RequestedBytes = new ByteSize(4096),
            EstimatedBytes = new ByteSize(2048),
            SafetyMultiplier = 2,
            ExpiresAt = DateTimeOffset.UnixEpoch.AddMinutes(5),
        };

        string json = JsonSerializer.Serialize(
            spec,
            ExecutionContractJsonContext.Default.StorageReservationSpec);
        StorageReservationSpec? roundTrip = JsonSerializer.Deserialize(
            json,
            ExecutionContractJsonContext.Default.StorageReservationSpec);

        Assert.NotNull(roundTrip);
        Assert.Equal("restore-1", roundTrip.OperationId);
        Assert.Equal(4096, roundTrip.RequestedBytes.Value);
        Assert.Equal(2, roundTrip.SafetyMultiplier);
    }

    [Fact]
    public void Public_contract_types_do_not_use_mutable_collection_properties()
    {
        var mutableCollectionDefinitions = new[]
        {
            typeof(List<>),
            typeof(Dictionary<,>),
            typeof(IList<>),
            typeof(ICollection<>),
            typeof(IDictionary<,>),
        };

        var offenders = ContractTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => (type, property)))
            .Where(item => item.property.PropertyType.IsGenericType)
            .Where(item => mutableCollectionDefinitions.Contains(item.property.PropertyType.GetGenericTypeDefinition()))
            .Select(item => $"{item.type.Name}.{item.property.Name}: {item.property.PropertyType.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static Type[] ContractTypes() =>
        typeof(RuntimeHost).Assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("HPD.Environment.Contracts", StringComparison.Ordinal) == true)
            .ToArray();

    private static ResourceMetadata<T> Metadata<T>(string id, string kind, ResourceLifetime lifetime)
        where T : IExecutionResourceMarker =>
        new()
        {
            Id = new ResourceId<T>(id),
            Kind = new ResourceKind(kind),
            Scope = new ResourceScope("runtime-1"),
            SchemaVersion = new SchemaVersion("v1"),
            Generation = new ResourceGeneration(7),
            CreatedAt = DateTimeOffset.UnixEpoch,
            Lifetime = lifetime,
        };

    private static ResourceRef<T> Ref<T>(string id, string kind)
        where T : IExecutionResourceMarker =>
        new(new ResourceId<T>(id), new ResourceScope("runtime-1"), new ResourceGeneration(1));

    private static TargetHandle<T> Handle<T>(params string[] segments)
        where T : IOperationTargetMarker
    {
        var routeSegments = new List<TargetRouteSegment>();
        for (var index = 0; index < segments.Length; index += 2)
        {
            routeSegments.Add(new TargetRouteSegment(ParseSegmentKind(segments[index]), segments[index + 1]));
        }

        return new TargetHandle<T>(
            new TargetRoute
            {
                Kind = new TargetKind(typeof(T).Name),
                Scope = new ResourceScope("runtime-1"),
                Segments = routeSegments,
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Observe | TargetHandleAuthority.Control);
    }

    private static TargetRouteSegmentKind ParseSegmentKind(string value)
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.Parse<TargetRouteSegmentKind>(normalized, ignoreCase: true);
    }

    private sealed class RecordingProviderRegistrationBuilder : IProviderRegistrationBuilder
    {
        public List<string> Calls { get; } = [];

        public void AddProviderCapabilityReporter(IProviderCapabilityReporter reporter) => Calls.Add(nameof(AddProviderCapabilityReporter));
        public void AddProviderActivator(IProviderActivator activator) => Calls.Add(nameof(AddProviderActivator));
        public void AddRuntimeHostProvider(IRuntimeHostProvider provider) => Calls.Add(nameof(AddRuntimeHostProvider));
        public void AddRuntimeHostWakeReconciliationProvider(IRuntimeHostWakeReconciliationProvider provider) => Calls.Add(nameof(AddRuntimeHostWakeReconciliationProvider));
        public void AddRuntimeHostResetProvider(IRuntimeHostResetProvider provider) => Calls.Add(nameof(AddRuntimeHostResetProvider));
        public void AddExecutionUnitProvider(IExecutionUnitProvider provider) => Calls.Add(nameof(AddExecutionUnitProvider));
        public void AddProcessProvider(IProcessProvider provider) => Calls.Add(nameof(AddProcessProvider));
        public void AddFunctionSandboxProvider(IFunctionSandboxProvider provider) => Calls.Add(nameof(AddFunctionSandboxProvider));
        public void AddFunctionSnapshotProvider(IFunctionSnapshotProvider provider) => Calls.Add(nameof(AddFunctionSnapshotProvider));
        public void AddArtifactProvider(IArtifactProvider provider) => Calls.Add(nameof(AddArtifactProvider));
        public void AddRootFilesystemProvider(IRootFilesystemProvider provider) => Calls.Add(nameof(AddRootFilesystemProvider));
        public void AddWorkspaceStore(IWorkspaceStore provider) => Calls.Add(nameof(AddWorkspaceStore));
        public void AddContentProjectionProvider(IContentProjectionProvider provider) => Calls.Add(nameof(AddContentProjectionProvider));
        public void AddNetworkProvider(INetworkProvider provider) => Calls.Add(nameof(AddNetworkProvider));
        public void AddNetworkMembershipProvider(INetworkMembershipProvider provider) => Calls.Add(nameof(AddNetworkMembershipProvider));
        public void AddServiceDiscoveryProvider(IServiceDiscoveryProvider provider) => Calls.Add(nameof(AddServiceDiscoveryProvider));
        public void AddEndpointPublicationProvider(IEndpointPublicationProvider provider) => Calls.Add(nameof(AddEndpointPublicationProvider));
        public void AddAuthorityBindingProvider(IAuthorityBindingProvider provider) => Calls.Add(nameof(AddAuthorityBindingProvider));
        public void AddCredentialProvider(ICredentialProvider provider) => Calls.Add(nameof(AddCredentialProvider));
        public void AddEngineControlPlaneProvider(IEngineControlPlaneProvider provider) => Calls.Add(nameof(AddEngineControlPlaneProvider));
        public void AddStoragePoolProvider(IStoragePoolProvider provider) => Calls.Add(nameof(AddStoragePoolProvider));
        public void AddDurableVolumeProvider(IDurableVolumeProvider provider) => Calls.Add(nameof(AddDurableVolumeProvider));
        public void AddStorageReservationProvider(IStorageReservationProvider provider) => Calls.Add(nameof(AddStorageReservationProvider));
        public void AddVolumeBackupProvider(IVolumeBackupProvider provider) => Calls.Add(nameof(AddVolumeBackupProvider));
        public void AddVolumeRestoreProvider(IVolumeRestoreProvider provider) => Calls.Add(nameof(AddVolumeRestoreProvider));
    }

    private sealed class TestProviderModule : IProviderModule
    {
        public ProviderDescriptor Descriptor { get; } = new()
        {
            Id = new ProviderId("test"),
            DisplayName = "Test",
            ContractVersion = new SemanticVersion(1, 0, 0),
            ProviderVersion = new SemanticVersion(1, 0, 0),
            ContractKinds = ProviderContractKind.RuntimeHost | ProviderContractKind.ExecutionUnit | ProviderContractKind.ProcessInvocation | ProviderContractKind.FunctionSandbox | ProviderContractKind.HostFunctionBinding | ProviderContractKind.AuthorityBinding | ProviderContractKind.EngineControlPlane,
            TrustLevel = ProviderTrustLevel.BuiltIn,
        };

        public void Register(IProviderRegistrationBuilder builder)
        {
            var provider = new TestProvider();
            builder.AddProviderCapabilityReporter(provider);
            builder.AddProviderActivator(provider);
            builder.AddRuntimeHostProvider(provider);
            builder.AddRuntimeHostWakeReconciliationProvider(provider);
            builder.AddRuntimeHostResetProvider(provider);
            builder.AddExecutionUnitProvider(provider);
            builder.AddProcessProvider(provider);
            builder.AddFunctionSandboxProvider(provider);
            builder.AddAuthorityBindingProvider(provider);
            builder.AddEngineControlPlaneProvider(provider);
        }

        public void RegisterJsonTypes(IProviderJsonTypeRegistry registry)
        {
            registry.Add(ExecutionContractJsonContext.Default.RuntimeHostSpec, "runtime-host-spec");
        }
    }

    private sealed class TestProvider :
        IRuntimeHostProvider,
        IRuntimeHostWakeReconciliationProvider,
        IRuntimeHostResetProvider,
        IExecutionUnitProvider,
        IProcessProvider,
        IFunctionSandboxProvider,
        IAuthorityBindingProvider,
        IEngineControlPlaneProvider,
        IProviderCapabilityReporter,
        IProviderActivator
    {
        public ProviderId ProviderId { get; } = new("test");

        public ValueTask<RuntimeHostStatus> EnsureAsync(ResourceMetadata<RuntimeHost> metadata, RuntimeHostSpec spec, RuntimeHostStatus? observed, CancellationToken cancellationToken = default) =>
            new(new RuntimeHostStatus { HostPhase = RuntimeHostPhase.Ready });

        public ValueTask<RuntimeHostStatus> StopAsync(TargetHandle<RuntimeHost> host, StopPolicy policy, CancellationToken cancellationToken = default) =>
            new(new RuntimeHostStatus { HostPhase = RuntimeHostPhase.Stopped });

        public ValueTask DeleteAsync(ResourceRef<RuntimeHost> host, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<RuntimeHostStatus> GetStatusAsync(TargetHandle<RuntimeHost> host, CancellationToken cancellationToken = default) =>
            new(new RuntimeHostStatus { HostPhase = RuntimeHostPhase.Ready });

        public ValueTask<RuntimeHostStatus> CompleteWakeReconciliationAsync(
            TargetHandle<RuntimeHost> host,
            RuntimeHostWakeReconciliationRequest request,
            CancellationToken cancellationToken = default) =>
            new(new RuntimeHostStatus
            {
                HostPhase = RuntimeHostPhase.Ready,
                Handle = host,
                Power = new RuntimeHostPowerStatus
                {
                    State = RuntimeHostPowerState.Active,
                    WakeGeneration = request.ObservedWakeGeneration,
                },
            });

        public ValueTask<RuntimeHostResetResult> ResetAsync(TargetHandle<RuntimeHost> host, RuntimeHostResetRequest request, CancellationToken cancellationToken = default) =>
            new(new RuntimeHostResetResult(request.Scope, Ref<RuntimeHost>("host-reset", "runtime-host"), DateTimeOffset.UtcNow));

        public ValueTask<ExecutionUnitStatus> EnsureAsync(ResourceMetadata<ExecutionUnit> metadata, ExecutionUnitSpec spec, ExecutionUnitStatus? observed, CancellationToken cancellationToken = default) =>
            new(new ExecutionUnitStatus { UnitPhase = ExecutionUnitPhase.Ready });

        public ValueTask<ExecutionUnitStatus> StopAsync(TargetHandle<ExecutionUnit> unit, StopPolicy policy, CancellationToken cancellationToken = default) =>
            new(new ExecutionUnitStatus { UnitPhase = ExecutionUnitPhase.Stopped });

        public ValueTask DeleteAsync(ResourceRef<ExecutionUnit> unit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<ExecutionUnitStatus> GetStatusAsync(TargetHandle<ExecutionUnit> unit, CancellationToken cancellationToken = default) =>
            new(new ExecutionUnitStatus { UnitPhase = ExecutionUnitPhase.Ready });

        public ValueTask<IProcessInvocationHandle> StartAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProcessInvocationResult> RunAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default) =>
            new(new ProcessInvocationResult
            {
                CompletionKind = ProcessCompletionKind.Completed,
                Output = new ProcessCapturedOutput { Stdout = new ProcessStreamOutput(), Stderr = new ProcessStreamOutput() },
            });

        public ValueTask SignalAsync(TargetHandle<ProcessInvocation> process, ProcessSignal signal, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ResizeTerminalAsync(TargetHandle<ProcessInvocation> process, TerminalSpec size, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<ProcessInvocationResult> WaitAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default) =>
            new(new ProcessInvocationResult
            {
                CompletionKind = ProcessCompletionKind.Completed,
                Output = new ProcessCapturedOutput { Stdout = new ProcessStreamOutput(), Stderr = new ProcessStreamOutput() },
            });

        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(TargetHandle<ProcessInvocation> process, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask<FunctionSandboxStatus> EnsureAsync(ResourceMetadata<FunctionSandbox> metadata, FunctionSandboxSpec spec, FunctionSandboxStatus? observed, CancellationToken cancellationToken = default) =>
            new(new FunctionSandboxStatus { SandboxPhase = FunctionSandboxPhase.Ready });

        public ValueTask<FunctionInvocationResult> InvokeAsync(FunctionInvocationSpec spec, IFunctionObservationSink? observations = null, CancellationToken cancellationToken = default) =>
            new(new FunctionInvocationResult { CompletionKind = FunctionInvocationCompletionKind.Returned });

        public ValueTask<FunctionSandboxStatus> GetStatusAsync(TargetHandle<FunctionSandbox> sandbox, CancellationToken cancellationToken = default) =>
            new(new FunctionSandboxStatus { SandboxPhase = FunctionSandboxPhase.Ready });

        public ValueTask ReleaseAsync(TargetHandle<FunctionSandbox> sandbox, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<AuthorityBindingStatus> EnsureAuthorityBindingAsync(ResourceMetadata<AuthorityBinding> metadata, AuthorityBindingSpec spec, AuthorityBindingStatus? observed, CancellationToken cancellationToken = default) =>
            new(new AuthorityBindingStatus { BindingPhase = AuthorityBindingPhase.Projected });

        public ValueTask<AuthorityBindingStatus> GetStatusAsync(ResourceRef<AuthorityBinding> binding, CancellationToken cancellationToken = default) =>
            new(new AuthorityBindingStatus { BindingPhase = AuthorityBindingPhase.Projected });

        public ValueTask RevokeAuthorityBindingAsync(ResourceRef<AuthorityBinding> binding, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<EngineControlPlaneStatus> EnsureEngineControlPlaneAsync(ResourceMetadata<EngineControlPlane> metadata, EngineControlPlaneSpec spec, EngineControlPlaneStatus? observed, CancellationToken cancellationToken = default) =>
            new(new EngineControlPlaneStatus { EnginePhase = EngineControlPlanePhase.Ready });

        public ValueTask<EngineAuthorityBindingPlan> PlanAuthorityBindingAsync(EngineControlPlaneStatus engine, EngineAuthorityBindingRequest request, CancellationToken cancellationToken = default) =>
            new(new EngineAuthorityBindingPlan { Accepted = false });

        public ValueTask<EngineControlPlaneStatus> GetStatusAsync(ResourceRef<EngineControlPlane> engine, CancellationToken cancellationToken = default) =>
            new(new EngineControlPlaneStatus { EnginePhase = EngineControlPlanePhase.Ready });

        public ValueTask<EngineControlPlaneStatus> StopAsync(TargetHandle<EngineControlPlane> engine, StopPolicy policy, CancellationToken cancellationToken = default) =>
            new(new EngineControlPlaneStatus { EnginePhase = EngineControlPlanePhase.Stopped });

        public ValueTask DeleteAsync(ResourceRef<EngineControlPlane> engine, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, CancellationToken cancellationToken = default) =>
            new(new ProviderCapabilityReport { ProviderId = providerId });

        public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, ProviderCapabilityQuery query, CancellationToken cancellationToken = default) =>
            new(new ProviderCapabilityReport { ProviderId = providerId });

        public ValueTask<ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus>> ActivateAsync(ProviderActivationSpec spec, CancellationToken cancellationToken = default) =>
            new(new ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus>(
                new ResourceMetadata<ProviderActivation>
                {
                    Id = new ResourceId<ProviderActivation>("activation-1"),
                    Kind = new ResourceKind("provider-activation"),
                    Scope = new ResourceScope("runtime-1"),
                    SchemaVersion = new SchemaVersion("v1"),
                },
                spec,
                new ProviderActivationStatus { ActivationPhase = ProviderActivationPhase.Ready, ProviderId = spec.ProviderId }));

        public ValueTask<ProviderActivationStatus> GetStatusAsync(ResourceRef<ProviderActivation> activation, CancellationToken cancellationToken = default) =>
            new(new ProviderActivationStatus { ActivationPhase = ProviderActivationPhase.Ready, ProviderId = ProviderId });

        public ValueTask StopAsync(TargetHandle<ProviderActivation> activation, ProviderStopOptions options, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

[JsonSerializable(typeof(RuntimeHostSpec))]
[JsonSerializable(typeof(RuntimeHostStatus))]
[JsonSerializable(typeof(ExecutionUnitSpec))]
[JsonSerializable(typeof(ExecutionUnitStatus))]
[JsonSerializable(typeof(ProcessInvocationSpec))]
[JsonSerializable(typeof(ProcessInvocationResult))]
[JsonSerializable(typeof(ContentArtifactSpec))]
[JsonSerializable(typeof(ContentArtifactStatus))]
[JsonSerializable(typeof(RootFilesystemViewSpec))]
[JsonSerializable(typeof(RootFilesystemViewStatus))]
[JsonSerializable(typeof(WorkspaceSpec))]
[JsonSerializable(typeof(WorkspaceStatus))]
[JsonSerializable(typeof(ContentProjectionSpec))]
[JsonSerializable(typeof(ContentProjectionStatus))]
[JsonSerializable(typeof(NetworkSpec))]
[JsonSerializable(typeof(NetworkMembershipSpec))]
[JsonSerializable(typeof(PublishedEndpointSpec))]
[JsonSerializable(typeof(AuthorityBindingSpec))]
[JsonSerializable(typeof(AuthorityBindingStatus))]
[JsonSerializable(typeof(FunctionSandboxSpec))]
[JsonSerializable(typeof(FunctionSandboxStatus))]
[JsonSerializable(typeof(FunctionInvocationSpec))]
[JsonSerializable(typeof(FunctionInvocationResult))]
[JsonSerializable(typeof(ProviderDescriptor))]
[JsonSerializable(typeof(ProviderCapabilityReport))]
[JsonSerializable(typeof(ProviderActivationSpec))]
[JsonSerializable(typeof(ProviderActivationStatus))]
[JsonSerializable(typeof(ResourceSnapshotEnvelope))]
[JsonSerializable(typeof(ExecutionResourceQuery))]
[JsonSerializable(typeof(ProcessOutputQuery))]
[JsonSerializable(typeof(EngineControlPlaneSpec))]
[JsonSerializable(typeof(EngineControlPlaneStatus))]
[JsonSerializable(typeof(StoragePoolSpec))]
[JsonSerializable(typeof(StoragePoolStatus))]
[JsonSerializable(typeof(DurableVolumeSpec))]
[JsonSerializable(typeof(DurableVolumeStatus))]
[JsonSerializable(typeof(StorageReservationSpec))]
[JsonSerializable(typeof(StorageReservationStatus))]
[JsonSerializable(typeof(VolumeBackupSpec))]
[JsonSerializable(typeof(VolumeBackupStatus))]
[JsonSerializable(typeof(VolumeRestoreSpec))]
[JsonSerializable(typeof(VolumeRestoreStatus))]
[JsonSerializable(typeof(RuntimePlanRequest))]
[JsonSerializable(typeof(RuntimePlan))]
[JsonSerializable(typeof(RuntimePlanValidationResult))]
internal sealed partial class ExecutionContractJsonContext : JsonSerializerContext;
