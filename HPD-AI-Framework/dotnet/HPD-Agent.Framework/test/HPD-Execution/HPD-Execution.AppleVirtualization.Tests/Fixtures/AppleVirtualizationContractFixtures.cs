namespace HPD.Execution.AppleVirtualization.Tests.Fixtures;

using HPD.Execution.Contracts;

public static class AppleVirtualizationContractFixtures
{
    public static ResourceScope RuntimeScope { get; } = new("acceptance-runtime");

    public static RuntimeHostSpec RuntimeHostSpec() =>
        new()
        {
            Platform = new PlatformSpec("linux", "arm64"),
            Capacity = new ResourceQuotaPolicy
            {
                CpuCores = 4,
                MemoryBytes = 4L * 1024 * 1024 * 1024,
                StorageBytes = 32L * 1024 * 1024 * 1024,
            },
            Bootstrap = new RuntimeHostBootstrapSpec
            {
                GuestComponents =
                [
                    new GuestComponentSpec(GuestComponentKind.GuestAgent, "hpd-guest-agent"),
                ],
                ReadinessGates =
                [
                    new ReadinessGateSpec(
                        "guest-agent-handshake",
                        ReadinessGateKind.GuestControlReachable,
                        ReadinessGateScope.GuestControl,
                        new RetryPolicy(MaxAttempts: 30, Delay: TimeSpan.FromSeconds(1)),
                        Timeout: TimeSpan.FromSeconds(30)),
                    new ReadinessGateSpec(
                        "command-probe",
                        ReadinessGateKind.Command,
                        ReadinessGateScope.GuestControl,
                        new RetryPolicy(MaxAttempts: 3, Delay: TimeSpan.FromSeconds(1)),
                        Timeout: TimeSpan.FromSeconds(5)),
                ],
            },
            TopologyPolicy = new RuntimeTopologyPolicy
            {
                Mode = RuntimeTopologyMode.OneHostPerRuntime,
            },
        };

    public static ResourceMetadata<TResource> Metadata<TResource>(string id, string kind)
        where TResource : IExecutionResourceMarker =>
        new()
        {
            Id = new ResourceId<TResource>(id),
            Kind = new ResourceKind(kind),
            Scope = RuntimeScope,
            SchemaVersion = new SchemaVersion("v1"),
            Generation = new ResourceGeneration(1),
            CreatedAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
        };

    public static ResourceRef<RuntimeHost> RuntimeHostRef(string id = "runtime-host-1") =>
        new(new ResourceId<RuntimeHost>(id), RuntimeScope, new ResourceGeneration(1));

    public static ResourceRef<ExecutionUnit> ExecutionUnitRef(string id = "unit-1") =>
        new(new ResourceId<ExecutionUnit>(id), RuntimeScope, new ResourceGeneration(1));

    public static ResourceRef<ContentProjection> ContentProjectionRef(string id = "projection-1") =>
        new(new ResourceId<ContentProjection>(id), RuntimeScope, new ResourceGeneration(1));

    public static TargetHandle<RuntimeHost> RuntimeHostHandle(string id = "runtime-host-1", ulong providerGeneration = 1) =>
        Handle<RuntimeHost>(TargetRouteSegmentKind.RuntimeHost, id, TargetHandleAuthority.Observe | TargetHandleAuthority.Control, providerGeneration);

    public static TargetHandle<ExecutionUnit> ExecutionUnitHandle(string id = "unit-1", ulong providerGeneration = 1) =>
        Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, id, TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Invoke, providerGeneration);

    public static TargetHandle<ContentProjection> ContentProjectionHandle(string id = "projection-1", ulong providerGeneration = 1) =>
        Handle<ContentProjection>(TargetRouteSegmentKind.ContentProjection, id, TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Read, providerGeneration);

    public static TargetHandle<ProcessInvocation> ProcessHandle(string id = "process-1", ulong providerGeneration = 1) =>
        Handle<ProcessInvocation>(TargetRouteSegmentKind.ProcessInvocation, id, TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Read, providerGeneration);

    public static ExecutionUnitSpec ExecutionUnitSpec(ResourceRef<RuntimeHost>? preferredHost = null) =>
        new()
        {
            PreferredHost = preferredHost ?? RuntimeHostRef(),
            ContentProjections = [ContentProjectionRef()],
            Identity = new ExecutionUnitIdentitySpec { User = "hpd", Group = "hpd" },
        };

    public static ContentProjectionSpec ReadOnlyWorkspaceProjection(TargetHandle<RuntimeHost>? host = null) =>
        new()
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.HostPath,
                HostPath = new HostPathSelection(new HostPath("/tmp/hpd-workspace"), HostPathKind.Directory),
            },
            Target = new ContentProjectionTarget
            {
                Host = RuntimeHostRef(),
                TargetName = "workspace",
            },
            View = new ProjectionView
            {
                Kind = ProjectionViewKind.FilesystemTree,
                GuestPath = new GuestPath("/workspace"),
            },
            Role = ContentProjectionRole.Workspace,
            AccessMode = AccessMode.ReadOnly,
            SecurityPolicy = new ContentProjectionSecurityPolicy
            {
                AllowHostPathSource = true,
                ReadOnlyEnforcement = ReadOnlyEnforcementPolicy.Required,
            },
            Realization = new ProjectionRealizationSpec
            {
                Kind = ProjectionRealizationKind.LiveProjection,
                WriteEffect = ProjectionWriteEffect.NoWrites,
                RequestedCoherence = CoherenceClass.CloseToOpen,
                Cache = CacheBehavior.ReadCache,
            },
        };

    public static ProcessInvocationSpec ProcessInvocationSpec(TargetHandle<ExecutionUnit>? unit = null, string fileName = "uname") =>
        new()
        {
            Target = unit ?? ExecutionUnitHandle(),
            Command = new ProcessCommandSpec
            {
                FileName = fileName,
                Arguments = fileName == "uname" ? ["-a"] : Array.Empty<string>(),
                WorkingDirectory = "/workspace",
                Environment = new Dictionary<string, string?>
                {
                    ["HPD_ACCEPTANCE"] = "1",
                },
            },
            Io = new ProcessIoSpec
            {
                StandardOutput = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = true,
                    MaxCapturedBytes = 64 * 1024,
                },
                StandardError = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = true,
                    MaxCapturedBytes = 64 * 1024,
                },
            },
            Isolation = ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Disabled,
                Network = new NetworkEgressPolicy { Mode = NetworkEgressMode.Blocked },
            },
            Policy = ProcessInvocationPolicy.Default with
            {
                Timeout = TimeSpan.FromSeconds(10),
                OutputDrainTimeout = TimeSpan.FromSeconds(2),
                StopOnRunCancellation = true,
            },
        };

    private static TargetHandle<TTarget> Handle<TTarget>(
        TargetRouteSegmentKind segmentKind,
        string id,
        TargetHandleAuthority authority,
        ulong providerGeneration)
        where TTarget : IOperationTargetMarker =>
        new(
            new TargetRoute
            {
                Kind = new TargetKind(typeof(TTarget).Name),
                Scope = RuntimeScope,
                Segments = [new TargetRouteSegment(segmentKind, id)],
                BackingResourceKind = new ResourceKind(segmentKind.ToString()),
                BackingResourceId = id,
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                ProviderHandle = new ProviderOpaqueHandle(
                    AppleVirtualizationProviderDescriptor.ProviderId,
                    $"{segmentKind}:{id}",
                    Generation: providerGeneration),
            },
            TargetHandleLifetime.LiveCapability,
            authority,
            providerGeneration);
}
