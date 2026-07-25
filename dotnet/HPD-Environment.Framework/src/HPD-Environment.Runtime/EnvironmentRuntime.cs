#nullable enable

namespace HPD.Environment.Runtime;

using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Environment.Contracts;

public sealed class EnvironmentProviderRegistry :
    IProviderRegistrationBuilder,
    IProviderJsonTypeRegistry,
    IProviderCatalog,
    IProviderCapabilityReporter
{
    private readonly List<ProviderDescriptor> _descriptors = [];
    private readonly List<JsonTypeInfoRegistration> _jsonTypes = [];

    public IReadOnlyList<IRuntimeHostProvider> RuntimeHostProviders => _runtimeHostProviders;
    public IReadOnlyList<IRuntimeHostResetProvider> RuntimeHostResetProviders => _runtimeHostResetProviders;
    public IReadOnlyList<IExecutionUnitProvider> ExecutionUnitProviders => _executionUnitProviders;
    public IReadOnlyList<IProcessProvider> ProcessProviders => _processProviders;
    public IReadOnlyList<IFunctionSandboxProvider> FunctionSandboxProviders => _functionSandboxProviders;
    public IReadOnlyList<IFunctionSnapshotProvider> FunctionSnapshotProviders => _functionSnapshotProviders;
    public IReadOnlyList<IArtifactProvider> ArtifactProviders => _artifactProviders;
    public IReadOnlyList<IRootFilesystemProvider> RootFilesystemProviders => _rootFilesystemProviders;
    public IReadOnlyList<IWorkspaceStore> WorkspaceStores => _workspaceStores;
    public IReadOnlyList<IContentProjectionProvider> ContentProjectionProviders => _contentProjectionProviders;
    public IReadOnlyList<INetworkProvider> NetworkProviders => _networkProviders;
    public IReadOnlyList<INetworkMembershipProvider> NetworkMembershipProviders => _networkMembershipProviders;
    public IReadOnlyList<IServiceDiscoveryProvider> ServiceDiscoveryProviders => _serviceDiscoveryProviders;
    public IReadOnlyList<IEndpointPublicationProvider> EndpointPublicationProviders => _endpointPublicationProviders;
    public IReadOnlyList<IAuthorityBindingProvider> AuthorityBindingProviders => _authorityBindingProviders;
    public IReadOnlyList<ICredentialProvider> CredentialProviders => _credentialProviders;
    public IReadOnlyList<IEngineControlPlaneProvider> EngineControlPlaneProviders => _engineControlPlaneProviders;
    public IReadOnlyList<IProviderCapabilityReporter> ProviderCapabilityReporters => _providerCapabilityReporters;
    public IReadOnlyList<IProviderActivator> ProviderActivators => _providerActivators;
    public IReadOnlyList<JsonTypeInfoRegistration> JsonTypes => _jsonTypes;

    private readonly List<IRuntimeHostProvider> _runtimeHostProviders = [];
    private readonly List<IRuntimeHostResetProvider> _runtimeHostResetProviders = [];
    private readonly List<IExecutionUnitProvider> _executionUnitProviders = [];
    private readonly List<IProcessProvider> _processProviders = [];
    private readonly List<IFunctionSandboxProvider> _functionSandboxProviders = [];
    private readonly List<IFunctionSnapshotProvider> _functionSnapshotProviders = [];
    private readonly List<IArtifactProvider> _artifactProviders = [];
    private readonly List<IRootFilesystemProvider> _rootFilesystemProviders = [];
    private readonly List<IWorkspaceStore> _workspaceStores = [];
    private readonly List<IContentProjectionProvider> _contentProjectionProviders = [];
    private readonly List<INetworkProvider> _networkProviders = [];
    private readonly List<INetworkMembershipProvider> _networkMembershipProviders = [];
    private readonly List<IServiceDiscoveryProvider> _serviceDiscoveryProviders = [];
    private readonly List<IEndpointPublicationProvider> _endpointPublicationProviders = [];
    private readonly List<IAuthorityBindingProvider> _authorityBindingProviders = [];
    private readonly List<ICredentialProvider> _credentialProviders = [];
    private readonly List<IEngineControlPlaneProvider> _engineControlPlaneProviders = [];
    private readonly List<IProviderCapabilityReporter> _providerCapabilityReporters = [];
    private readonly List<IProviderActivator> _providerActivators = [];

    public void RegisterModule(IProviderModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        _descriptors.Add(module.Descriptor);
        module.Register(this);
        module.RegisterJsonTypes(this);
    }

    public void Add(JsonTypeInfo jsonTypeInfo, string typeDiscriminator)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDiscriminator);
        _jsonTypes.Add(new JsonTypeInfoRegistration(jsonTypeInfo.Type, typeDiscriminator));
    }

    public ValueTask<IReadOnlyList<ProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ProviderDescriptor>>(_descriptors.ToArray());

    public ValueTask<ProviderDescriptor?> GetAsync(ProviderId providerId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_descriptors.FirstOrDefault(provider => provider.Id.Equals(providerId)));

    public async ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, CancellationToken cancellationToken = default)
    {
        ProviderCapabilityReport? providerReport = await TryGetProviderCapabilityReportAsync(providerId, null, cancellationToken).ConfigureAwait(false);
        if (providerReport is not null)
        {
            return providerReport;
        }

        ProviderDescriptor descriptor = await GetRequiredDescriptorAsync(providerId, cancellationToken).ConfigureAwait(false);
        return CreateCapabilityReport(descriptor, CapabilityRequirementSet.Empty);
    }

    public async ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, ProviderCapabilityQuery query, CancellationToken cancellationToken = default)
    {
        ProviderCapabilityReport? providerReport = await TryGetProviderCapabilityReportAsync(providerId, query, cancellationToken).ConfigureAwait(false);
        if (providerReport is not null)
        {
            return providerReport;
        }

        ProviderDescriptor descriptor = await GetRequiredDescriptorAsync(providerId, cancellationToken).ConfigureAwait(false);
        return CreateCapabilityReport(descriptor, query.Requirements ?? CapabilityRequirementSet.Empty);
    }

    public void AddProviderCapabilityReporter(IProviderCapabilityReporter reporter) => _providerCapabilityReporters.Add(reporter);
    public void AddProviderActivator(IProviderActivator activator) => _providerActivators.Add(activator);
    public void AddRuntimeHostProvider(IRuntimeHostProvider provider) => _runtimeHostProviders.Add(provider);
    public void AddRuntimeHostResetProvider(IRuntimeHostResetProvider provider) => _runtimeHostResetProviders.Add(provider);
    public void AddExecutionUnitProvider(IExecutionUnitProvider provider) => _executionUnitProviders.Add(provider);
    public void AddProcessProvider(IProcessProvider provider) => _processProviders.Add(provider);
    public void AddFunctionSandboxProvider(IFunctionSandboxProvider provider) => _functionSandboxProviders.Add(provider);
    public void AddFunctionSnapshotProvider(IFunctionSnapshotProvider provider) => _functionSnapshotProviders.Add(provider);
    public void AddArtifactProvider(IArtifactProvider provider) => _artifactProviders.Add(provider);
    public void AddRootFilesystemProvider(IRootFilesystemProvider provider) => _rootFilesystemProviders.Add(provider);
    public void AddWorkspaceStore(IWorkspaceStore provider) => _workspaceStores.Add(provider);
    public void AddContentProjectionProvider(IContentProjectionProvider provider) => _contentProjectionProviders.Add(provider);
    public void AddNetworkProvider(INetworkProvider provider) => _networkProviders.Add(provider);
    public void AddNetworkMembershipProvider(INetworkMembershipProvider provider) => _networkMembershipProviders.Add(provider);
    public void AddServiceDiscoveryProvider(IServiceDiscoveryProvider provider) => _serviceDiscoveryProviders.Add(provider);
    public void AddEndpointPublicationProvider(IEndpointPublicationProvider provider) => _endpointPublicationProviders.Add(provider);
    public void AddAuthorityBindingProvider(IAuthorityBindingProvider provider) => _authorityBindingProviders.Add(provider);
    public void AddCredentialProvider(ICredentialProvider provider) => _credentialProviders.Add(provider);
    public void AddEngineControlPlaneProvider(IEngineControlPlaneProvider provider) => _engineControlPlaneProviders.Add(provider);

    private async ValueTask<ProviderCapabilityReport?> TryGetProviderCapabilityReportAsync(ProviderId providerId, ProviderCapabilityQuery? query, CancellationToken cancellationToken)
    {
        foreach (IProviderCapabilityReporter reporter in _providerCapabilityReporters)
        {
            ProviderCapabilityReport report = query is null
                ? await reporter.GetCapabilitiesAsync(providerId, cancellationToken).ConfigureAwait(false)
                : await reporter.GetCapabilitiesAsync(providerId, query, cancellationToken).ConfigureAwait(false);

            if (report.ProviderId.Equals(providerId))
            {
                return report;
            }
        }

        return null;
    }

    private async ValueTask<ProviderDescriptor> GetRequiredDescriptorAsync(ProviderId providerId, CancellationToken cancellationToken)
    {
        ProviderDescriptor? descriptor = await GetAsync(providerId, cancellationToken).ConfigureAwait(false);
        return descriptor ?? throw new InvalidOperationException($"Provider '{providerId.Value}' is not registered.");
    }

    private static ProviderCapabilityReport CreateCapabilityReport(ProviderDescriptor descriptor, CapabilityRequirementSet requirements)
    {
        var capabilities = EnumerateContractKinds(descriptor.ContractKinds)
            .Select(kind => new CapabilityFact
            {
                Id = new CapabilityId($"hpd.environment.contract.{ToCapabilityName(kind)}"),
                Category = new CapabilityCategory("contract"),
                AppliesTo = kind,
                State = CapabilityState.Supported,
            })
            .Concat(requirements.Items.Select(requirement => new CapabilityFact
            {
                Id = requirement.Id,
                Category = new CapabilityCategory("requested"),
                AppliesTo = requirement.AppliesTo,
                State = descriptor.ContractKinds.HasFlag(requirement.AppliesTo) ? CapabilityState.Supported : CapabilityState.Unsupported,
            }))
            .ToArray();

        return new ProviderCapabilityReport
        {
            ProviderId = descriptor.Id,
            ObservedAt = DateTimeOffset.UtcNow,
            HostPlatform = DefaultPlatform(),
            Capabilities = capabilities,
            PreflightChecks =
            [
                new ProviderPreflightCheck("provider-registered", PreflightCheckState.Passed),
            ],
        };
    }

    private static IEnumerable<ProviderContractKind> EnumerateContractKinds(ProviderContractKind kinds)
    {
        foreach (ProviderContractKind kind in Enum.GetValues<ProviderContractKind>())
        {
            if (kind != ProviderContractKind.None && kinds.HasFlag(kind))
            {
                yield return kind;
            }
        }
    }

    private static string ToCapabilityName(ProviderContractKind kind) =>
        kind.ToString().Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();

    internal static PlatformSpec DefaultPlatform() =>
        new(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "unknown",
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
}

public sealed record JsonTypeInfoRegistration(Type Type, string TypeDiscriminator);

public sealed class DefaultRuntimePlanner(IProviderCatalog providers, IProviderCapabilityReporter capabilities) : IRuntimePlanner
{
    public async ValueTask<RuntimePlan> PlanAsync(RuntimePlanRequest request, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProviderDescriptor> descriptors = await providers.ListAsync(cancellationToken).ConfigureAwait(false);
        ProviderDescriptor? selected = SelectProvider(request, descriptors);
        var unsupported = new List<UnsupportedReason>();
        var coverage = new List<CapabilityCoverage>();
        var selectedProviders = new List<SelectedProvider>();
        var activations = new List<ProviderActivationSpec>();
        var steps = new List<RuntimePlanActivationStep>();
        var permissionPlan = new List<ProviderPermissionRequirement>();
        IReadOnlyList<Condition> planConditions = Array.Empty<Condition>();
        ProviderCapabilityReport? capabilityReport = null;

        if (selected is null)
        {
            unsupported.Add(new UnsupportedReason(
                new DiagnosticCode("hpd.execution.provider.missing"),
                UnsupportedSeverity.Error,
                ProviderId: null,
                request.RequiredContracts,
                $"No registered provider covers required contracts '{request.RequiredContracts}'."));
        }
        else
        {
            capabilityReport = await capabilities.GetCapabilitiesAsync(
                selected.Id,
                new ProviderCapabilityQuery(
                    request.Capabilities,
                    HostPlatform: EnvironmentProviderRegistry.DefaultPlatform(),
                    GuestPlatform: request.RequestedPlatform,
                    GuestAbi: request.RequestedGuestAbi,
                    Scope: null),
                cancellationToken).ConfigureAwait(false);
            permissionPlan.AddRange(capabilityReport.RequiredPermissions);
            planConditions = capabilityReport.Conditions;

            foreach (ProviderContractKind kind in EnumerateContractKinds(request.RequiredContracts))
            {
                CapabilityId[] coveredCapabilities = request.Capabilities.Items
                    .Where(requirement => requirement.AppliesTo.HasFlag(kind))
                    .Select(requirement => FindCapabilityFact(capabilityReport, requirement))
                    .Where(fact => fact is not null && IsCapabilitySatisfied(fact.State, request.CapabilityPolicy))
                    .Select(fact => fact!.Id)
                    .ToArray();

                selectedProviders.Add(new SelectedProvider(
                    kind,
                    selected.Id,
                    selected.DefaultActivationScope,
                    Required: true,
                    coveredCapabilities.Length == 0 ? null : coveredCapabilities));
            }

            foreach (CapabilityRequirement requirement in request.Capabilities.Items)
            {
                CapabilityFact? fact = FindCapabilityFact(capabilityReport, requirement);
                CapabilityState state = fact?.State
                    ?? (selected.ContractKinds.HasFlag(requirement.AppliesTo) ? CapabilityState.Supported : CapabilityState.Unsupported);
                bool supported = IsCapabilitySatisfied(state, capabilityReport.RequiredPermissions, requirement, request.CapabilityPolicy);
                string detail = fact?.Detail
                    ?? (selected.ContractKinds.HasFlag(requirement.AppliesTo)
                        ? "Covered by selected provider."
                        : "Selected provider does not cover the requested contract kind.");

                coverage.Add(new CapabilityCoverage(
                    requirement.Id,
                    requirement.Strength,
                    state,
                    selected.Id,
                    detail));

                if (!supported &&
                    requirement.Strength == CapabilityRequirementStrength.Required &&
                    request.CapabilityPolicy.FailOnMissingRequired)
                {
                    unsupported.Add(new UnsupportedReason(
                        DiagnosticCodeFor(state),
                        UnsupportedSeverityFor(state),
                        selected.Id,
                        requirement.AppliesTo,
                        $"Required capability '{requirement.Id.Value}' is {DescribeCapabilityState(state)}."));
                }
            }

            ProviderActivationModel activationModel = SelectActivationModel(selected);
            var activation = new ProviderActivationSpec
            {
                ProviderId = selected.Id,
                Scope = activationModel.Scope,
                ScopeKey = request.Profile?.Name ?? "runtime",
                RequiredContracts = request.RequiredContracts,
                ActivationKind = activationModel.Kind,
                RequiredCapabilities = request.Capabilities.Items
                    .Where(item => item.Strength == CapabilityRequirementStrength.Required)
                    .Select(item => item.Id)
                    .ToArray(),
                RequiredPermissions = capabilityReport.RequiredPermissions
                    .Where(permission => permission.Required)
                    .Select(permission => permission.Id)
                    .ToArray(),
                Supervisor = new ProviderSupervisorRequirement(
                    activationModel.RequiresSupervision,
                    RestartOnFailure: activationModel.RequiresSupervision,
                    StartupTimeout: TimeSpan.FromSeconds(5)),
                Transport = TransportRequirementFor(activationModel.Transport),
                AuthPolicy = new ProviderAuthPolicy("current-user", RequireSameUser: true, AllowRemoteIdentity: false),
                HealthPolicy = new ProviderHealthPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                LogPolicy = new ProviderLogPolicy("memory", CaptureStartupLogs: true, CaptureDiagnosticLogs: true),
            };

            activations.Add(activation);
            steps.Add(new RuntimePlanActivationStep
            {
                Id = new RuntimePlanStepId("activate-primary-provider"),
                Activation = activation,
                ExpectedComponents =
                [
                    new ProviderComponentExpectation(ProviderComponentKind.ProviderDefined, selected.DisplayName),
                ],
            });
        }

        PlatformSpec platform = request.RequestedPlatform ?? EnvironmentProviderRegistry.DefaultPlatform();
        return new RuntimePlan
        {
            Id = new RuntimePlanId($"plan-{Guid.NewGuid():N}"),
            TopologyPolicy = request.TopologyPolicy,
            Compatibility = new PlatformCompatibilityPlan
            {
                RequestedPlatform = platform,
                HostPlatform = capabilityReport?.HostPlatform ?? EnvironmentProviderRegistry.DefaultPlatform(),
                GuestAbi = request.RequestedGuestAbi,
                ExecutionMode = selected is null ? ExecutionMode.Unsupported : ExecutionMode.Native,
                PlacementProviderId = selected?.Id,
                Conditions = planConditions,
            },
            Providers = selectedProviders,
            Activations = activations,
            ActivationSteps = steps,
            CapabilityCoverage = coverage,
            PermissionPlan = permissionPlan,
            UnsupportedReasons = unsupported,
        };
    }

    public ValueTask<RuntimePlanValidationResult> ValidateAsync(RuntimePlan plan, CancellationToken cancellationToken = default)
    {
        var conditions = new List<Condition>();
        bool hasActivation = plan.Activations.Count > 0 || plan.UnsupportedReasons.Count > 0;
        if (!hasActivation)
        {
            conditions.Add(new Condition(
                "ActivationPlanPresent",
                ConditionStatus.False,
                "MissingActivation",
                "A supported runtime plan must contain at least one provider activation.",
                DateTimeOffset.UtcNow,
                default,
                DiagnosticSeverity.Error));
        }

        return ValueTask.FromResult(new RuntimePlanValidationResult
        {
            IsSupported = plan.UnsupportedReasons.Count == 0 && hasActivation,
            UnsupportedReasons = plan.UnsupportedReasons,
            Conditions = conditions,
        });
    }

    private static ProviderDescriptor? SelectProvider(RuntimePlanRequest request, IReadOnlyList<ProviderDescriptor> descriptors)
    {
        IEnumerable<ProviderDescriptor> ordered = descriptors;
        if (request.PreferredProviders.Count > 0)
        {
            ordered = descriptors.OrderBy(provider =>
            {
                int index = request.PreferredProviders.ToList().FindIndex(id => id.Equals(provider.Id));
                return index < 0 ? int.MaxValue : index;
            });
        }

        return ordered.FirstOrDefault(provider => (provider.ContractKinds & request.RequiredContracts) == request.RequiredContracts);
    }

    private static ProviderActivationModel SelectActivationModel(ProviderDescriptor descriptor)
    {
        ProviderActivationModel? preferred = descriptor.ActivationModels.FirstOrDefault(model => model.Scope == descriptor.DefaultActivationScope);
        return preferred ?? descriptor.ActivationModels.FirstOrDefault() ?? new ProviderActivationModel(
            ProviderActivationKind.InProcess,
            descriptor.DefaultActivationScope,
            ProviderTransportKind.None);
    }

    private static CapabilityFact? FindCapabilityFact(ProviderCapabilityReport report, CapabilityRequirement requirement) =>
        report.Capabilities.FirstOrDefault(fact =>
            fact.Id.Equals(requirement.Id) &&
            (fact.AppliesTo & requirement.AppliesTo) == requirement.AppliesTo);

    private static bool IsCapabilitySatisfied(CapabilityState state, RuntimeCapabilityPolicy policy) =>
        state switch
        {
            CapabilityState.Supported => true,
            CapabilityState.Degraded => policy.AllowPreferredDegradation,
            _ => false,
        };

    private static bool IsCapabilitySatisfied(
        CapabilityState state,
        IReadOnlyList<ProviderPermissionRequirement> permissions,
        CapabilityRequirement requirement,
        RuntimeCapabilityPolicy policy) =>
        state switch
        {
            CapabilityState.RequiresPermission => RequiredPermissionsGranted(permissions, requirement.Id),
            _ => IsCapabilitySatisfied(state, policy),
        };

    private static bool RequiredPermissionsGranted(IReadOnlyList<ProviderPermissionRequirement> permissions, CapabilityId capability)
    {
        ProviderPermissionRequirement[] required = permissions
            .Where(permission => permission.Required && permission.Capability.Equals(capability))
            .ToArray();

        return required.Length > 0 && required.All(permission => permission.State == PermissionGrantState.Granted);
    }

    private static DiagnosticCode DiagnosticCodeFor(CapabilityState state) =>
        state switch
        {
            CapabilityState.RequiresPermission => new DiagnosticCode("hpd.execution.capability.requires-permission"),
            CapabilityState.RequiresConfiguration => new DiagnosticCode("hpd.execution.capability.requires-configuration"),
            CapabilityState.DisabledByPolicy => new DiagnosticCode("hpd.execution.capability.disabled-by-policy"),
            CapabilityState.TemporarilyUnavailable => new DiagnosticCode("hpd.execution.capability.temporarily-unavailable"),
            CapabilityState.Planned => new DiagnosticCode("hpd.execution.capability.planned"),
            CapabilityState.Deferred => new DiagnosticCode("hpd.execution.capability.deferred"),
            _ => new DiagnosticCode("hpd.execution.capability.unsupported"),
        };

    private static UnsupportedSeverity UnsupportedSeverityFor(CapabilityState state) =>
        state is CapabilityState.Planned or CapabilityState.Deferred
            ? UnsupportedSeverity.Info
            : UnsupportedSeverity.Error;

    private static string DescribeCapabilityState(CapabilityState state) =>
        state switch
        {
            CapabilityState.RequiresPermission => "available only after permission is granted",
            CapabilityState.RequiresConfiguration => "available only after provider configuration",
            CapabilityState.DisabledByPolicy => "disabled by policy",
            CapabilityState.TemporarilyUnavailable => "temporarily unavailable",
            CapabilityState.Planned => "planned but not implemented",
            CapabilityState.Deferred => "deferred and not implemented",
            CapabilityState.Degraded => "available only in degraded form",
            _ => "unsupported",
        };

    private static ProviderTransportRequirement TransportRequirementFor(ProviderTransportKind transport) =>
        new(
            transport,
            RequiresStreaming: transport is ProviderTransportKind.StdIo or ProviderTransportKind.UnixSocket or ProviderTransportKind.Tcp or ProviderTransportKind.NamedPipe or ProviderTransportKind.Grpc or ProviderTransportKind.Vsock or ProviderTransportKind.HvSocket,
            RequiresHandlePassing: transport is ProviderTransportKind.UnixSocket,
            RequiresPeerAuthentication: transport is ProviderTransportKind.UnixSocket or ProviderTransportKind.NamedPipe or ProviderTransportKind.Grpc or ProviderTransportKind.Vsock or ProviderTransportKind.HvSocket);

    private static IEnumerable<ProviderContractKind> EnumerateContractKinds(ProviderContractKind kinds)
    {
        foreach (ProviderContractKind kind in Enum.GetValues<ProviderContractKind>())
        {
            if (kind != ProviderContractKind.None && kinds.HasFlag(kind))
            {
                yield return kind;
            }
        }
    }
}

public sealed class InMemoryEnvironmentRuntime(
    EnvironmentProviderRegistry registry,
    IRuntimePlanner? planner = null,
    TimeProvider? timeProvider = null,
    TimeSpan? engineAuthorityPlanLifetime = null,
    TimeSpan? executionUnitObservationTimeout = null,
    TimeSpan? processObservationTimeout = null) : IEnvironmentRuntime
{
    private readonly IRuntimePlanner _planner = planner ?? new DefaultRuntimePlanner(registry, registry);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _engineAuthorityPlanLifetime =
        engineAuthorityPlanLifetime is { } configured && configured > TimeSpan.Zero
            ? configured
            : TimeSpan.FromMinutes(1);
    private readonly TimeSpan _executionUnitObservationTimeout =
        executionUnitObservationTimeout is { } observationTimeout && observationTimeout > TimeSpan.Zero
            ? observationTimeout
            : TimeSpan.FromSeconds(5);
    private readonly TimeSpan _processObservationTimeout =
        processObservationTimeout is { } processTimeout && processTimeout > TimeSpan.Zero
            ? processTimeout
            : TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _reconciliationGate = new(1, 1);
    private readonly Dictionary<string, OwnedExecutionUnit> _units = new(StringComparer.Ordinal);
    private readonly Dictionary<ExecutionUnitIdentity, string> _unitIdsByIdentity = [];
    private readonly Dictionary<EngineIdentity, OwnedEngine> _engines = [];
    private readonly Dictionary<string, OwnedAuthorityBinding> _authorities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnedProcess> _processes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingEngineAuthorityPlan> _engineAuthorityPlans =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, ActiveRun> _activeRuns = [];
    private readonly ConcurrentDictionary<long, IProcessInvocationHandle> _activeHandles = [];
    private OwnedHost? _host;
    private long _generation;
    private long _processSequence;

    public ValueTask<RuntimePlan> PlanAsync(RuntimePlanRequest request, CancellationToken cancellationToken = default) =>
        _planner.PlanAsync(request, cancellationToken);

    public ValueTask<RuntimePlanValidationResult> ValidateAsync(RuntimePlan plan, CancellationToken cancellationToken = default) =>
        _planner.ValidateAsync(plan, cancellationToken);

    public async ValueTask<ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>> EnsureHostAsync(
        RuntimeHostSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_host is not null &&
                spec.PreferredProvider is { } requestedProvider &&
                !requestedProvider.Equals(_host.ProviderId))
            {
                throw OwnershipFailure(
                    "hpd.environment.runtime-host.provider-migration-requires-replacement",
                    $"Runtime host '{_host.Snapshot.Metadata.Id.Value}' is owned by provider " +
                    $"'{_host.ProviderId.Value}'; migration to '{requestedProvider.Value}' requires explicit deletion and recreation.");
            }
            IRuntimeHostProvider provider = _host is null
                ? SelectProvider(registry.RuntimeHostProviders, spec.PreferredProvider, "runtime host")
                : ProviderById(registry.RuntimeHostProviders, _host.ProviderId, "runtime host");
            string fingerprint = Fingerprint(spec);
            if (_host is not null &&
                !string.Equals(_host.SpecFingerprint, fingerprint, StringComparison.Ordinal) &&
                HasCurrentHostDependents())
            {
                Diagnostic diagnostic = RuntimeDiagnostic(
                    "hpd.environment.runtime-host.replacement-dependents-active",
                    $"Runtime host '{_host.Snapshot.Metadata.Id.Value}' cannot accept a material configuration " +
                    "change while dependent execution units, engines, or authorities remain owned. Delete the " +
                    "host explicitly to perform ordered dependent cleanup before replacement.");
                RuntimeHostStatus rejected = _host.Snapshot.Status with
                {
                    ReconciliationOutcome = ResourceReconciliationOutcome.ImmutableConflict,
                    Diagnostics = [.. _host.Snapshot.Status.Diagnostics, diagnostic],
                };
                return _host.Snapshot with { Status = rejected };
            }
            ResourceMetadata<RuntimeHost> metadata = _host is null
                ? Metadata<RuntimeHost>("runtime-host")
                : string.Equals(_host.SpecFingerprint, fingerprint, StringComparison.Ordinal)
                    ? _host.Snapshot.Metadata
                    : Advance(_host.Snapshot.Metadata);
            RuntimeHostStatus status = await provider
                .EnsureAsync(metadata, spec, _host?.Snapshot.Status, cancellationToken)
                .ConfigureAwait(false);
            var proposed = new ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>(metadata, spec, status);
            if (status.ReconciliationOutcome == ResourceReconciliationOutcome.Accepted)
            {
                _host = new OwnedHost(provider.ProviderId, fingerprint, proposed);
            }
            return proposed;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<RuntimeHostDeletionResult> DeleteHostAsync(CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_host is null)
            {
                return new RuntimeHostDeletionResult { Deleted = true };
            }
            if (_host.Snapshot.Spec.HostPolicy.ProtectFromDelete)
            {
                throw OwnershipFailure(
                    "hpd.environment.runtime-host.delete-protected",
                    $"Runtime host '{_host.Snapshot.Metadata.Id.Value}' is protected from deletion by policy.");
            }

            CleanupPolicy policy = _host.Snapshot.Spec.LifecyclePolicy.Cleanup;
            using var overallCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overallCancellation.CancelAfter(PositiveTimeout(policy.OverallTimeout, TimeSpan.FromSeconds(30)));
            var cleanup = new CleanupContext(policy, cancellationToken, overallCancellation.Token);
            try
            {
                await StopActiveProcessesAsync(cleanup).ConfigureAwait(false);
                foreach (OwnedProcess process in ProcessesForCurrentHost())
                {
                    bool released = await ExecuteCleanupStepAsync(
                        $"process release '{process.Snapshot.Metadata.Id.Value}'",
                        async stepToken =>
                        {
                            await ReleaseOwnedProcessAsync(process, stepToken).ConfigureAwait(false);
                        },
                        cleanup).ConfigureAwait(false);
                    if (released)
                    {
                        _activeHandles.TryRemove(process.ActiveHandleId, out _);
                        _processes.Remove(process.Snapshot.Metadata.Id.Value);
                        DetachProcessFromUnit(process);
                    }
                }
                if (policy.FinalizeBeforeRelease)
                {
                    _ = await ExecuteCleanupStepAsync(
                        "content projection finalization",
                        async stepToken =>
                        {
                            _ = await FinalizeContentAsync(
                                new RuntimeFinalizationRequest(
                                    _host.Snapshot.Metadata.Scope,
                                    PromoteMemory: false,
                                    policy),
                                stepToken).ConfigureAwait(false);
                        },
                        cleanup).ConfigureAwait(false);
                }

                foreach (OwnedAuthorityBinding authority in AuthoritiesForCurrentHost())
                {
                    bool revoked = await ExecuteCleanupStepAsync(
                        $"authority revocation '{authority.Snapshot.Metadata.Id.Value}'",
                        stepToken => ProviderById(
                                registry.AuthorityBindingProviders,
                                authority.ProviderId,
                                "authority binding")
                            .RevokeAuthorityBindingAsync(Ref(authority.Snapshot.Metadata), stepToken),
                        cleanup).ConfigureAwait(false);
                    if (revoked)
                    {
                        _authorities.Remove(authority.Snapshot.Metadata.Id.Value);
                    }
                }

                foreach (OwnedExecutionUnit unit in UnitsForCurrentHost())
                {
                    bool deleted = await ExecuteCleanupStepAsync(
                        $"execution-unit deletion '{unit.Snapshot.Metadata.Id.Value}'",
                        stepToken => ProviderById(registry.ExecutionUnitProviders, unit.ProviderId, "execution unit")
                            .DeleteAsync(Ref(unit.Snapshot.Metadata), stepToken),
                        cleanup).ConfigureAwait(false);
                    if (deleted)
                    {
                        _units.Remove(unit.Snapshot.Metadata.Id.Value);
                        if (unit.Identity is { } identity)
                        {
                            _unitIdsByIdentity.Remove(identity);
                        }
                    }
                }

                foreach ((EngineIdentity key, OwnedEngine engine) in EnginesForCurrentHost())
                {
                    bool deleted = await ExecuteCleanupStepAsync(
                        $"engine control-plane deletion '{engine.Snapshot.Metadata.Id.Value}'",
                        stepToken => ProviderById(
                                registry.EngineControlPlaneProviders,
                                engine.ProviderId,
                                "engine control plane")
                            .DeleteAsync(Ref(engine.Snapshot.Metadata), stepToken),
                        cleanup).ConfigureAwait(false);
                    if (deleted)
                    {
                        _engines.Remove(key);
                    }
                }

                IRuntimeHostProvider hostProvider =
                    ProviderById(registry.RuntimeHostProviders, _host.ProviderId, "runtime host");
                bool hostDeleted = await ExecuteCleanupStepAsync(
                    $"runtime-host deletion '{_host.Snapshot.Metadata.Id.Value}'",
                    stepToken => hostProvider.DeleteAsync(Ref(_host.Snapshot.Metadata), stepToken),
                    cleanup).ConfigureAwait(false);
                if (!hostDeleted)
                {
                    return RetainedDeletionResult(cleanup.Diagnostics);
                }

                RemoveCurrentHostDependentOwnership();
                _host = null;
                return new RuntimeHostDeletionResult
                {
                    Deleted = true,
                    Diagnostics = cleanup.Diagnostics.ToArray(),
                };
            }
            catch (CleanupRetainedException)
            {
                return RetainedDeletionResult(cleanup.Diagnostics);
            }
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>> EnsureExecutionUnitAsync(
        ExecutionUnitSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ProviderId owner = HostOwner(spec.PreferredHost);
            IExecutionUnitProvider provider =
                ProviderById(registry.ExecutionUnitProviders, owner, "execution unit");
            ExecutionUnitIdentity? identity = UnitIdentity(spec);
            OwnedExecutionUnit? existing = null;
            if (identity is { } stableIdentity &&
                _unitIdsByIdentity.TryGetValue(stableIdentity, out string? existingId))
            {
                if (!_units.TryGetValue(existingId, out existing))
                {
                    _unitIdsByIdentity.Remove(stableIdentity);
                    throw OwnershipFailure(
                        "hpd.environment.execution-unit.identity-corrupt",
                        $"Execution-unit identity '{stableIdentity.Key}' refers to missing runtime ownership.");
                }
                if (!existing.ProviderId.Equals(owner))
                {
                    throw OwnershipFailure(
                        "hpd.environment.execution-unit.provider-conflict",
                        $"Execution-unit identity '{stableIdentity.Key}' is already owned by provider " +
                        $"'{existing.ProviderId.Value}', not '{owner.Value}'.");
                }
            }

            string fingerprint = Fingerprint(spec);
            if (existing is not null &&
                !string.Equals(existing.SpecFingerprint, fingerprint, StringComparison.Ordinal))
            {
                existing = await RefreshUnitAsync(existing, cancellationToken).ConfigureAwait(false);
                if (HasActiveUnitDependents(existing))
                {
                    Diagnostic diagnostic = RuntimeDiagnostic(
                        "hpd.environment.execution-unit.replacement-dependents-active",
                        $"Execution unit '{existing.Snapshot.Metadata.Id.Value}' cannot accept a material " +
                        "configuration change while processes, authorities, projections, network memberships, " +
                        "or published endpoints remain active. Delete and recreate the unit after ordered cleanup.");
                    ExecutionUnitStatus rejected = existing.Snapshot.Status with
                    {
                        ReconciliationOutcome = ResourceReconciliationOutcome.ImmutableConflict,
                        Diagnostics = [.. existing.Snapshot.Status.Diagnostics, diagnostic],
                    };
                    return existing.Snapshot with { Status = rejected };
                }
            }
            ResourceMetadata<ExecutionUnit> metadata = existing is null
                ? Metadata<ExecutionUnit>("execution-unit") with
                {
                    Lifetime = ResourceLifetime.ExecutionUnit,
                    OwnerRefs = spec.PreferredHost is { } host ? [Untyped(host)] : Array.Empty<UntypedResourceRef>(),
                }
                : string.Equals(existing.SpecFingerprint, fingerprint, StringComparison.Ordinal)
                    ? existing.Snapshot.Metadata
                    : Advance(existing.Snapshot.Metadata);
            ExecutionUnitStatus status = await provider
                .EnsureAsync(metadata, spec, existing?.Snapshot.Status, cancellationToken)
                .ConfigureAwait(false);
            var snapshot = new ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>(metadata, spec, status);
            if (status.ReconciliationOutcome == ResourceReconciliationOutcome.Accepted)
            {
                _units[metadata.Id.Value] = new OwnedExecutionUnit(owner, identity, fingerprint, snapshot);
                if (identity is { } acceptedIdentity)
                {
                    _unitIdsByIdentity[acceptedIdentity] = metadata.Id.Value;
                }
            }
            if (status.ReconciliationOutcome == ResourceReconciliationOutcome.Accepted || existing is null)
            {
                return snapshot;
            }
            return existing.Snapshot with
            {
                Status = status with
                {
                    ObservedGeneration = existing.Snapshot.Metadata.Generation,
                },
            };
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>>>
        ListExecutionUnitsAsync(CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var refreshed = new List<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>>(
                _units.Count);
            foreach (OwnedExecutionUnit unit in _units.Values.ToArray())
            {
                refreshed.Add((await RefreshUnitAsync(unit, cancellationToken).ConfigureAwait(false)).Snapshot);
            }
            return refreshed
                .OrderBy(unit => unit.Metadata.CreatedAt)
                .ThenBy(unit => unit.Metadata.Id.Value, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>>
        GetExecutionUnitAsync(
            ResourceRef<ExecutionUnit> unit,
            CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OwnedExecutionUnit owned = FindUnit(unit);
            return (await RefreshUnitAsync(owned, cancellationToken).ConfigureAwait(false)).Snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus>> EnsureEngineControlPlaneAsync(
        EngineControlPlaneSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ResourceRef<RuntimeHost> host = spec.Host ??
                throw OwnershipFailure("hpd.environment.engine.host-required", "Engine control planes require an owned runtime host.");
            ProviderId owner = HostOwner(host);
            var identity = new EngineIdentity(host.Scope.Value, host.Id.Value, spec.Kind, spec.Api);
            _engines.TryGetValue(identity, out OwnedEngine? existing);
            string fingerprint = Fingerprint(spec);
            if (existing is not null &&
                !string.Equals(existing.SpecFingerprint, fingerprint, StringComparison.Ordinal) &&
                _authorities.Values.Any(authority =>
                    authority.SourceEngine is { } source &&
                    SameResource(source, Ref(existing.Snapshot.Metadata))))
            {
                throw OwnershipFailure(
                    "hpd.environment.engine.reconfiguration-authority-active",
                    $"Engine '{existing.Snapshot.Metadata.Id.Value}' cannot be reconfigured while authority bindings " +
                    $"derived from generation {existing.Snapshot.Metadata.Generation.Value} remain active.");
            }
            ResourceMetadata<EngineControlPlane> metadata = existing is null
                ? Metadata<EngineControlPlane>("engine-control-plane") with
                {
                    OwnerRefs = [Untyped(host)],
                }
                : string.Equals(existing.SpecFingerprint, fingerprint, StringComparison.Ordinal)
                    ? existing.Snapshot.Metadata
                    : Advance(existing.Snapshot.Metadata);
            IEngineControlPlaneProvider provider =
                ProviderById(registry.EngineControlPlaneProviders, owner, "engine control plane");
            EngineControlPlaneStatus status = await provider
                .EnsureEngineControlPlaneAsync(metadata, spec, existing?.Snapshot.Status, cancellationToken)
                .ConfigureAwait(false);
            var proposed = new ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus>(
                metadata,
                spec,
                status);
            if (status.ReconciliationOutcome == ResourceReconciliationOutcome.Accepted)
            {
                if (existing is not null &&
                    !string.Equals(existing.SpecFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    RemovePendingPlansForEngine(Ref(existing.Snapshot.Metadata));
                }
                _engines[identity] = new OwnedEngine(owner, fingerprint, proposed);
            }
            return proposed;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<EngineAuthorityBindingPlan> PlanEngineAuthorityBindingAsync(
        EngineAuthorityBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OwnedEngine engine = FindEngine(request.Engine);
            OwnedExecutionUnit unit = FindUnit(request.TargetUnit);
            if (!engine.ProviderId.Equals(unit.ProviderId))
            {
                throw OwnershipFailure(
                    "hpd.environment.engine-authority.provider-mismatch",
                    "The engine and target execution unit are owned by different providers.");
            }
            EngineAuthorityBindingPlan plan =
                await ProviderById(registry.EngineControlPlaneProviders, engine.ProviderId, "engine control plane")
                .PlanAuthorityBindingAsync(engine.Snapshot.Status, request, cancellationToken)
                .ConfigureAwait(false);
            if (!plan.Accepted)
            {
                return plan with { SourceEngine = request.Engine };
            }
            if (plan.Spec is null)
            {
                throw OwnershipFailure(
                    "hpd.environment.engine-authority.provider-plan-malformed",
                    "The engine provider accepted an authority plan without an approved specification.");
            }

            PruneExpiredAuthorityPlans();
            if (_engineAuthorityPlans.Count >= 256)
            {
                return new EngineAuthorityBindingPlan
                {
                    Accepted = false,
                    SourceEngine = request.Engine,
                    Diagnostics =
                    [
                        new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Code = new DiagnosticCode("hpd.environment.engine-authority.plan-capacity-exceeded"),
                            Message = "The runtime has reached its bounded pending engine-authority approval capacity.",
                        },
                    ],
                };
            }
            var planId = new EngineAuthorityBindingPlanId(Guid.NewGuid().ToString("N"));
            DateTimeOffset expiresAt = _timeProvider.GetUtcNow() + _engineAuthorityPlanLifetime;
            var approved = plan with
            {
                PlanId = planId,
                ExpiresAt = expiresAt,
                SourceEngine = request.Engine,
            };
            _engineAuthorityPlans.Add(
                planId.Value,
                new PendingEngineAuthorityPlan(
                    request.Engine,
                    Fingerprint(plan.Spec),
                    expiresAt));
            return approved;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<AuthorityBinding, AuthorityBindingSpec, AuthorityBindingStatus>>
        EnsureEngineAuthorityBindingAsync(
            EngineAuthorityBindingPlan plan,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!plan.Accepted ||
                string.IsNullOrWhiteSpace(plan.PlanId.Value) ||
                plan.Spec is null ||
                plan.SourceEngine is not { } sourceEngine)
            {
                throw OwnershipFailure(
                    "hpd.environment.engine-authority.plan-not-accepted",
                    "Only an accepted engine-authority plan with source-engine identity can be realized.");
            }

            if (!_engineAuthorityPlans.TryGetValue(plan.PlanId.Value, out PendingEngineAuthorityPlan? pending))
            {
                throw OwnershipFailure(
                    "hpd.environment.engine-authority.plan-unknown-or-consumed",
                    "The engine-authority plan is unknown or has already been consumed.");
            }
            if (_timeProvider.GetUtcNow() >= pending.ExpiresAt)
            {
                _engineAuthorityPlans.Remove(plan.PlanId.Value);
                throw OwnershipFailure(
                    "hpd.environment.engine-authority.plan-expired",
                    "The engine-authority plan has expired.");
            }
            if (!SameResource(sourceEngine, pending.SourceEngine) ||
                !string.Equals(Fingerprint(plan.Spec), pending.SpecFingerprint, StringComparison.Ordinal))
            {
                throw OwnershipFailure(
                    "hpd.environment.engine-authority.plan-altered",
                    "The engine-authority plan no longer matches the exact provider-approved engine and specification.");
            }

            _ = FindEngine(pending.SourceEngine);
            _engineAuthorityPlans.Remove(plan.PlanId.Value);
            return await EnsureAuthorityBindingCoreAsync(plan.Spec, sourceEngine, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask DeleteExecutionUnitAsync(
        ResourceRef<ExecutionUnit> unit,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OwnedExecutionUnit owned = FindUnit(unit);
            foreach (OwnedProcess process in ProcessesForUnit(Ref(owned.Snapshot.Metadata)))
            {
                OwnedProcess current = process;
                if (!IsTerminal(current.Snapshot.Status.ProcessPhase))
                {
                    await RetainedProcessProvider(current)
                        .StopAsync(
                            current.Handle.Handle,
                            new ProcessStopRequest(
                                StopKind.GracefulThenKill,
                                "execution unit deletion",
                                owned.Snapshot.Spec.LifecyclePolicy.Cleanup.OperationTimeout),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                current = await RefreshProcessAsync(current, cancellationToken).ConfigureAwait(false);
                if (!IsTerminal(current.Snapshot.Status.ProcessPhase))
                {
                    throw OwnershipFailure(
                        "hpd.environment.process.release-nonterminal",
                        $"Process '{current.Snapshot.Metadata.Id.Value}' did not reach a terminal state.");
                }
                await ReleaseOwnedProcessAsync(current, cancellationToken).ConfigureAwait(false);
                _activeHandles.TryRemove(current.ActiveHandleId, out _);
                _processes.Remove(current.Snapshot.Metadata.Id.Value);
            }
            await ProviderById(registry.ExecutionUnitProviders, owned.ProviderId, "execution unit")
                .DeleteAsync(unit, cancellationToken).ConfigureAwait(false);
            _units.Remove(unit.Id.Value);
            if (owned.Identity is { } identity)
            {
                _unitIdsByIdentity.Remove(identity);
            }
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<AuthorityBinding, AuthorityBindingSpec, AuthorityBindingStatus>> EnsureAuthorityBindingAsync(
        AuthorityBindingSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (spec.Policy.EffectiveAuthorityClass is
                SensitiveAuthorityClass.RootlessEngineControl or
                SensitiveAuthorityClass.RootfulEngineControl)
            {
                throw OwnershipFailure(
                    "hpd.environment.engine-authority.plan-required",
                    "Engine authority must be realized from an accepted, generation-bound engine-authority plan.");
            }
            return await EnsureAuthorityBindingCoreAsync(spec, sourceEngine: null, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    private async ValueTask<ResourceSnapshot<AuthorityBinding, AuthorityBindingSpec, AuthorityBindingStatus>>
        EnsureAuthorityBindingCoreAsync(
            AuthorityBindingSpec spec,
            ResourceRef<EngineControlPlane>? sourceEngine,
            CancellationToken cancellationToken)
    {
        OwnedExecutionUnit unit = spec.Target.Unit is { } target
            ? FindUnit(target)
            : throw OwnershipFailure(
                "hpd.environment.authority.target-required",
                "Runtime-owned authority bindings require an owned execution-unit target.");
        if (sourceEngine is { } source)
        {
            OwnedEngine engine = FindEngine(source);
            if (!engine.ProviderId.Equals(unit.ProviderId))
            {
                throw OwnershipFailure(
                    "hpd.environment.engine-authority.provider-mismatch",
                    "The source engine and target execution unit are owned by different providers.");
            }
        }

        IAuthorityBindingProvider provider =
            ProviderById(registry.AuthorityBindingProviders, unit.ProviderId, "authority binding");
        ResourceMetadata<AuthorityBinding> metadata = Metadata<AuthorityBinding>("authority-binding") with
        {
            Lifetime = ResourceLifetime.ExecutionUnit,
            OwnerRefs = [Untyped(Ref(unit.Snapshot.Metadata))],
        };
        AuthorityBindingStatus status = await provider
            .EnsureAuthorityBindingAsync(metadata, spec, observed: null, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = new ResourceSnapshot<AuthorityBinding, AuthorityBindingSpec, AuthorityBindingStatus>(
            metadata,
            spec,
            status);
        _authorities.Add(
            metadata.Id.Value,
            new OwnedAuthorityBinding(unit.ProviderId, unit.Snapshot.Spec.PreferredHost, sourceEngine, snapshot));
        return snapshot;
    }

    public async ValueTask RevokeAuthorityBindingAsync(
        ResourceRef<AuthorityBinding> binding,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OwnedAuthorityBinding owned = FindAuthority(binding);
            await ProviderById(registry.AuthorityBindingProviders, owned.ProviderId, "authority binding")
                .RevokeAuthorityBindingAsync(binding, cancellationToken).ConfigureAwait(false);
            _authorities.Remove(binding.Id.Value);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>>
        StartProcessAsync(
        ProcessInvocationSpec spec,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OwnedExecutionUnit unit = FindUnit(spec.Target);
            IProcessProvider provider =
                ProviderById(registry.ProcessProviders, unit.ProviderId, "process");
            IRetainedProcessProvider retainedProvider = RequireRetainedProcessProvider(provider);
            IProcessInvocationHandle handle =
                await provider.StartAsync(
                    spec with
                    {
                        PersistResource = true,
                        ObservationRetention = ObservationRetentionPolicy.DurableResource,
                    },
                    output: null,
                    cancellationToken).ConfigureAwait(false);
            if (handle.Resource is not { } providerResource)
            {
                await handle.DisposeAsync().ConfigureAwait(false);
                throw OwnershipFailure(
                    "hpd.environment.process.retained-resource-required",
                    $"Provider '{provider.ProviderId.Value}' did not return a durable process resource.");
            }
            ProcessInvocationStatus providerStatus;
            using var observationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            observationCancellation.CancelAfter(_processObservationTimeout);
            try
            {
                providerStatus = await retainedProvider
                    .GetStatusAsync(handle.Handle, observationCancellation.Token)
                    .AsTask()
                    .WaitAsync(observationCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await CleanupUnownedProcessAsync(
                    provider,
                    retainedProvider,
                    handle,
                    providerResource).ConfigureAwait(false);
                throw new TimeoutException(
                    $"Process start observation exceeded {_processObservationTimeout.TotalMilliseconds:0} milliseconds.");
            }
            catch
            {
                await CleanupUnownedProcessAsync(
                    provider,
                    retainedProvider,
                    handle,
                    providerResource).ConfigureAwait(false);
                throw;
            }
            ResourceMetadata<ProcessInvocation> metadata = Metadata<ProcessInvocation>("process-invocation") with
            {
                Lifetime = ResourceLifetime.Process,
                OwnerRefs = [Untyped(Ref(unit.Snapshot.Metadata))],
            };
            ProcessInvocationStatus status = providerStatus with
            {
                ObservedGeneration = metadata.Generation,
                Handle = handle.Handle,
            };
            var snapshot =
                new ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>(
                    metadata,
                    spec,
                    status);
            long id = Interlocked.Increment(ref _processSequence);
            _activeHandles[id] = handle;
            var output = new ProcessOutputRetention(
                spec.Io.LogPolicy.MaxRetainedBytesPerStream ?? 64 * 1024);
            var outputCancellation = new CancellationTokenSource();
            Task outputPump = PumpProcessOutputAsync(
                provider,
                retainedProvider,
                handle.Handle,
                output,
                outputCancellation.Token);
            _processes.Add(
                metadata.Id.Value,
                new OwnedProcess(
                    provider.ProviderId,
                    Ref(unit.Snapshot.Metadata),
                    providerResource,
                    handle,
                    id,
                    output,
                    outputCancellation,
                    outputPump,
                    snapshot));
            AttachProcessToUnit(unit, Ref(metadata), spec.Role);
            return snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>>>
        ListProcessesAsync(
            ResourceRef<ExecutionUnit>? unit = null,
            CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ResourceRef<ExecutionUnit>? owner = unit is { } requested
                ? Ref(FindUnit(requested).Snapshot.Metadata)
                : null;
            var snapshots = new List<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>>();
            foreach (OwnedProcess process in _processes.Values.ToArray())
            {
                if (owner is not null && !SameResource(owner.Value, process.Unit))
                {
                    continue;
                }
                snapshots.Add((await RefreshProcessAsync(process, cancellationToken).ConfigureAwait(false)).Snapshot);
            }
            return snapshots
                .OrderBy(process => process.Metadata.CreatedAt)
                .ThenBy(process => process.Metadata.Id.Value, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>>
        GetProcessAsync(
            ResourceRef<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await RefreshProcessAsync(FindProcess(process), cancellationToken).ConfigureAwait(false)).Snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>>
        StopProcessAsync(
            ResourceRef<ProcessInvocation> process,
            ProcessStopRequest request,
            CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OwnedProcess owned = FindProcess(process);
            await RetainedProcessProvider(owned)
                .StopAsync(owned.Handle.Handle, request, cancellationToken)
                .ConfigureAwait(false);
            OwnedProcess stopped = await RefreshProcessAsync(owned, cancellationToken).ConfigureAwait(false);
            if (IsTerminal(stopped.Snapshot.Status.ProcessPhase))
            {
                DetachProcessFromUnit(stopped);
            }
            return stopped.Snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus>>
        WaitProcessAsync(
            ResourceRef<ProcessInvocation> process,
            CancellationToken cancellationToken = default)
    {
        OwnedProcess owned;
        IProcessProvider provider;
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            owned = FindProcess(process);
            provider = ProviderById(registry.ProcessProviders, owned.ProviderId, "process");
        }
        finally
        {
            _reconciliationGate.Release();
        }

        ProcessInvocationResult result =
            await provider.WaitAsync(owned.Handle.Handle, cancellationToken).ConfigureAwait(false);

        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OwnedProcess current = FindProcess(process);
            ProcessInvocationStatus status = current.Snapshot.Status with
            {
                Phase = result.CompletionKind is ProcessCompletionKind.FailedToStart or
                    ProcessCompletionKind.Faulted
                        ? ResourcePhase.Failed
                        : ResourcePhase.Ready,
                ProcessPhase = ProcessPhase(result.CompletionKind),
                IoState = ProcessIoState.Closed,
                Result = result,
                StartedAt = result.StartedAt ?? current.Snapshot.Status.StartedAt,
                ExitedAt = result.ExitedAt ?? _timeProvider.GetUtcNow(),
                LastTransitionAt = _timeProvider.GetUtcNow(),
            };
            OwnedProcess completed = current with
            {
                Snapshot = current.Snapshot with { Status = status },
            };
            _processes[process.Id.Value] = completed;
            DetachProcessFromUnit(completed);
            return completed.Snapshot;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async IAsyncEnumerable<ProcessOutputChunk> ReadProcessOutputAsync(
        ResourceRef<ProcessInvocation> process,
        long? afterSequence = null,
        int? limit = null,
        bool follow = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        OwnedProcess owned;
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            owned = FindProcess(process);
        }
        finally
        {
            _reconciliationGate.Release();
        }

        int remaining = limit is > 0 ? limit.Value : int.MaxValue;
        long cursor = afterSequence ?? 0;
        while (remaining > 0)
        {
            ProcessOutputChunk[] retained = owned.Output.Snapshot(cursor, remaining);
            foreach (ProcessOutputChunk chunk in retained)
            {
                cursor = Math.Max(cursor, chunk.Sequence);
                yield return chunk;
                if (--remaining == 0)
                {
                    yield break;
                }
            }
            if (!follow || owned.Output.IsComplete)
            {
                yield break;
            }
            await owned.Output.WaitForChangeAsync(cursor, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DeleteProcessAsync(
        ResourceRef<ProcessInvocation> process,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OwnedProcess owned = FindProcess(process);
            owned = await RefreshProcessAsync(owned, cancellationToken).ConfigureAwait(false);
            if (!IsTerminal(owned.Snapshot.Status.ProcessPhase))
            {
                throw OwnershipFailure(
                    "hpd.environment.process.delete-running",
                    $"Process '{process.Id.Value}' must stop before deletion.");
            }
            await ReleaseOwnedProcessAsync(owned, cancellationToken).ConfigureAwait(false);
            _activeHandles.TryRemove(owned.ActiveHandleId, out _);
            _processes.Remove(process.Id.Value);
            DetachProcessFromUnit(owned);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async ValueTask<ProcessInvocationResult> RunProcessAsync(
        ProcessInvocationSpec spec,
        IProcessOutputSink? output = null,
        CancellationToken cancellationToken = default)
    {
        IProcessProvider provider;
        long id;
        CancellationTokenSource linkedCancellation;
        TaskCompletionSource completion;
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            provider = ProcessProvider(spec.Target);
            id = Interlocked.Increment(ref _processSequence);
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRuns[id] = new ActiveRun(spec, linkedCancellation, completion.Task);
        }
        finally
        {
            _reconciliationGate.Release();
        }

        try
        {
            return await provider.RunAsync(spec, output, linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            completion.TrySetResult();
            _activeRuns.TryRemove(id, out _);
            linkedCancellation.Dispose();
        }
    }

    public async ValueTask<ResourceSnapshot<FunctionSandbox, FunctionSandboxSpec, FunctionSandboxStatus>> EnsureFunctionSandboxAsync(FunctionSandboxSpec spec, CancellationToken cancellationToken = default)
    {
        ResourceMetadata<FunctionSandbox> metadata = Metadata<FunctionSandbox>("function-sandbox");
        FunctionSandboxStatus status = await SelectProvider(registry.FunctionSandboxProviders, null, "function sandbox")
            .EnsureAsync(metadata, spec, observed: null, cancellationToken).ConfigureAwait(false);
        return new ResourceSnapshot<FunctionSandbox, FunctionSandboxSpec, FunctionSandboxStatus>(metadata, spec, status);
    }

    public ValueTask<FunctionInvocationResult> InvokeFunctionAsync(FunctionInvocationSpec spec, IFunctionObservationSink? observations = null, CancellationToken cancellationToken = default) =>
        ProviderForRoute(registry.FunctionSandboxProviders, spec.Sandbox.Route, "function sandbox")
            .InvokeAsync(spec, observations, cancellationToken);

    public async ValueTask<RuntimeFinalizationResult> FinalizeRuntimeAsync(
        RuntimeFinalizationRequest request,
        CancellationToken cancellationToken = default)
    {
        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await FinalizeRuntimeCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    private async ValueTask<RuntimeFinalizationResult> FinalizeRuntimeCoreAsync(
        RuntimeFinalizationRequest request,
        CancellationToken cancellationToken)
    {
        var contentProjections = new List<FinalizationResult>();
        var retainedResources = new List<UntypedResourceRef>();
        var conflicts = new List<WorkspaceConflict>();
        var diagnostics = new List<Diagnostic>();

        RuntimeFinalizationResult content = await FinalizeContentAsync(request, cancellationToken).ConfigureAwait(false);
        contentProjections.AddRange(content.ContentProjections);
        retainedResources.AddRange(content.RetainedResources);
        conflicts.AddRange(content.Conflicts);
        diagnostics.AddRange(content.Diagnostics);

        if (request.CleanupPolicy.RevokeAuthorityBindingsFirst)
        {
            foreach (OwnedAuthorityBinding authority in _authorities.Values.ToArray())
            {
                await ProviderById(registry.AuthorityBindingProviders, authority.ProviderId, "authority binding")
                    .RevokeAuthorityBindingAsync(Ref(authority.Snapshot.Metadata), cancellationToken)
                    .ConfigureAwait(false);
                _authorities.Remove(authority.Snapshot.Metadata.Id.Value);
            }
        }

        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Info,
            Code = new DiagnosticCode("hpd.execution.runtime.finalized"),
            Message = request.CleanupPolicy.RevokeAuthorityBindingsFirst
                ? "Runtime finalized after authority revocation ordering."
                : "Runtime finalized without authority-first revocation ordering.",
        });

        return new RuntimeFinalizationResult
        {
            RuntimeScope = request.RuntimeScope,
            ContentProjections = contentProjections.Count == 0 ? Array.Empty<FinalizationResult>() : contentProjections.ToArray(),
            RetainedResources = retainedResources.Count == 0 ? Array.Empty<UntypedResourceRef>() : retainedResources.ToArray(),
            Conflicts = conflicts.Count == 0 ? Array.Empty<WorkspaceConflict>() : conflicts.ToArray(),
            Diagnostics = diagnostics,
        };
    }

    private async ValueTask<RuntimeFinalizationResult> FinalizeContentAsync(
        RuntimeFinalizationRequest request,
        CancellationToken cancellationToken)
    {
        var contentProjections = new List<FinalizationResult>();
        var retainedResources = new List<UntypedResourceRef>();
        var conflicts = new List<WorkspaceConflict>();
        var diagnostics = new List<Diagnostic>();
        foreach (IContentProjectionProvider provider in registry.ContentProjectionProviders)
        {
            if (_host is not null && !provider.ProviderId.Equals(_host.ProviderId))
            {
                continue;
            }
            if (provider is not IRuntimeFinalizationParticipant participant)
            {
                continue;
            }

            RuntimeFinalizationResult result =
                await participant.FinalizeRuntimeAsync(request, cancellationToken).ConfigureAwait(false);
            contentProjections.AddRange(result.ContentProjections);
            retainedResources.AddRange(result.RetainedResources);
            conflicts.AddRange(result.Conflicts);
            diagnostics.AddRange(result.Diagnostics);
        }

        return new RuntimeFinalizationResult
        {
            RuntimeScope = request.RuntimeScope,
            ContentProjections = contentProjections,
            RetainedResources = retainedResources,
            Conflicts = conflicts,
            Diagnostics = diagnostics,
        };
    }

    private async ValueTask StopActiveProcessesAsync(CleanupContext cleanup)
    {
        foreach (ActiveRun run in _activeRuns.Values)
        {
            run.Cancellation.Cancel();
        }
        foreach ((long id, IProcessInvocationHandle handle) in _activeHandles)
        {
            bool stopped = await ExecuteCleanupStepAsync(
                $"active process stop '{id}'",
                stepToken => handle.StopAsync(new ProcessStopRequest(
                    StopKind.GracefulThenKill,
                    "runtime host deletion",
                    cleanup.Policy.OperationTimeout), stepToken),
                cleanup).ConfigureAwait(false);
            bool disposed = await ExecuteCleanupStepAsync(
                $"active process handle disposal '{id}'",
                _ => handle.DisposeAsync(),
                cleanup).ConfigureAwait(false);
            if (stopped && disposed)
            {
                _activeHandles.TryRemove(id, out _);
            }
        }
        foreach ((long id, ActiveRun run) in _activeRuns)
        {
            _ = await ExecuteCleanupStepAsync(
                $"active process completion '{id}'",
                stepToken => new ValueTask(run.Completion.WaitAsync(stepToken)),
                cleanup).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> ExecuteCleanupStepAsync(
        string step,
        Func<CancellationToken, ValueTask> action,
        CleanupContext cleanup)
    {
        using var stepCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cleanup.OverallToken);
        stepCancellation.CancelAfter(PositiveTimeout(cleanup.Policy.OperationTimeout, TimeSpan.FromSeconds(5)));
        try
        {
            await action(stepCancellation.Token).AsTask().WaitAsync(stepCancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cleanup.CallerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Exception failure = exception is OperationCanceledException
                ? new TimeoutException($"Runtime cleanup exceeded its deadline during {step}.", exception)
                : exception;
            Diagnostic diagnostic = CleanupDiagnostic(step, failure);
            cleanup.Diagnostics.Add(diagnostic);
            switch (cleanup.Policy.FailureMode)
            {
                case CleanupFailureMode.FailOperation:
                    throw new RuntimeCleanupException(step, failure);
                case CleanupFailureMode.MarkDegradedAndRetain:
                    MarkHostDegraded(diagnostic);
                    throw new CleanupRetainedException();
                case CleanupFailureMode.BestEffortRelease:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cleanup.Policy.FailureMode));
            }
        }
    }

    private RuntimeHostDeletionResult RetainedDeletionResult(IReadOnlyList<Diagnostic> diagnostics) =>
        new()
        {
            Deleted = false,
            RetainedHostStatus = _host?.Snapshot.Status,
            Diagnostics = diagnostics.ToArray(),
        };

    private void MarkHostDegraded(Diagnostic diagnostic)
    {
        if (_host is null)
        {
            return;
        }

        RuntimeHostStatus degraded = _host.Snapshot.Status with
        {
            Phase = ResourcePhase.Degraded,
            HostPhase = RuntimeHostPhase.Degraded,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Diagnostics = [.. _host.Snapshot.Status.Diagnostics, diagnostic],
        };
        _host = _host with
        {
            Snapshot = _host.Snapshot with { Status = degraded },
        };
    }

    private static Diagnostic CleanupDiagnostic(string step, Exception exception) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(exception is TimeoutException
                ? "hpd.environment.runtime-cleanup.timeout"
                : "hpd.environment.runtime-cleanup.failed"),
            Message = $"Runtime cleanup failed during {step}: {exception.Message}",
        };

    private void RemoveCurrentHostDependentOwnership()
    {
        _engineAuthorityPlans.Clear();
        foreach (OwnedAuthorityBinding authority in AuthoritiesForCurrentHost())
        {
            _authorities.Remove(authority.Snapshot.Metadata.Id.Value);
        }
        foreach (OwnedExecutionUnit unit in UnitsForCurrentHost())
        {
            _units.Remove(unit.Snapshot.Metadata.Id.Value);
            if (unit.Identity is { } identity)
            {
                _unitIdsByIdentity.Remove(identity);
            }
        }
        foreach ((EngineIdentity key, _) in EnginesForCurrentHost())
        {
            _engines.Remove(key);
        }
    }

    private void RemovePendingPlansForEngine(ResourceRef<EngineControlPlane> engine)
    {
        foreach ((string id, PendingEngineAuthorityPlan plan) in _engineAuthorityPlans.ToArray())
        {
            if (SameResource(plan.SourceEngine, engine))
            {
                _engineAuthorityPlans.Remove(id);
            }
        }
    }

    private void PruneExpiredAuthorityPlans()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach ((string id, PendingEngineAuthorityPlan plan) in _engineAuthorityPlans.ToArray())
        {
            if (now >= plan.ExpiresAt)
            {
                _engineAuthorityPlans.Remove(id);
            }
        }
    }

    private static bool SameResource<TResource>(
        ResourceRef<TResource> left,
        ResourceRef<TResource> right)
        where TResource : IExecutionResourceMarker =>
        left.Id.Equals(right.Id) &&
        left.Scope.Equals(right.Scope) &&
        left.Generation.Equals(right.Generation);

    private static TimeSpan PositiveTimeout(TimeSpan value, TimeSpan fallback) =>
        value > TimeSpan.Zero && value != Timeout.InfiniteTimeSpan ? value : fallback;

    private IProcessProvider ProcessProvider(TargetHandle<ExecutionUnit> target)
    {
        OwnedExecutionUnit unit = FindUnit(target);
        return ProviderById(registry.ProcessProviders, unit.ProviderId, "process");
    }

    private ProviderId HostOwner(ResourceRef<RuntimeHost>? host)
    {
        if (_host is null || host is null)
        {
            throw OwnershipFailure(
                "hpd.environment.runtime-host.unknown",
                "The requested runtime host is not owned by this runtime.");
        }
        ValidateRef(host.Value, _host.Snapshot.Metadata, "runtime host");
        return _host.ProviderId;
    }

    private OwnedEngine FindEngine(ResourceRef<EngineControlPlane> reference)
    {
        OwnedEngine? owned = _engines.Values.FirstOrDefault(candidate =>
            candidate.Snapshot.Metadata.Id.Equals(reference.Id) &&
            candidate.Snapshot.Metadata.Scope.Equals(reference.Scope));
        if (owned is null)
        {
            throw OwnershipFailure(
                "hpd.environment.engine.unknown",
                $"Engine control plane '{reference.Id.Value}' is not owned by this runtime.");
        }
        ValidateRef(reference, owned.Snapshot.Metadata, "engine control plane");
        return owned;
    }

    private OwnedExecutionUnit FindUnit(ResourceRef<ExecutionUnit> reference)
    {
        if (!_units.TryGetValue(reference.Id.Value, out OwnedExecutionUnit? owned) ||
            !owned.Snapshot.Metadata.Scope.Equals(reference.Scope))
        {
            throw OwnershipFailure(
                "hpd.environment.execution-unit.unknown",
                $"Execution unit '{reference.Id.Value}' is not owned by this runtime.");
        }
        ValidateRef(reference, owned.Snapshot.Metadata, "execution unit");
        return owned;
    }

    private OwnedExecutionUnit FindUnit(TargetHandle<ExecutionUnit> handle)
    {
        OwnedExecutionUnit? unit = _units.Values.FirstOrDefault(candidate =>
            candidate.Snapshot.Status.Handle is { } ownedHandle &&
            ownedHandle.Equals(handle));
        return unit ?? throw OwnershipFailure(
            "hpd.environment.execution-unit.handle-unknown",
            "The execution-unit handle is unknown, stale, or owned by another runtime.");
    }

    private OwnedAuthorityBinding FindAuthority(ResourceRef<AuthorityBinding> reference)
    {
        if (!_authorities.TryGetValue(reference.Id.Value, out OwnedAuthorityBinding? owned) ||
            !owned.Snapshot.Metadata.Scope.Equals(reference.Scope))
        {
            throw OwnershipFailure(
                "hpd.environment.authority.unknown",
                $"Authority binding '{reference.Id.Value}' is not owned by this runtime.");
        }
        ValidateRef(reference, owned.Snapshot.Metadata, "authority binding");
        return owned;
    }

    private OwnedProcess FindProcess(ResourceRef<ProcessInvocation> reference)
    {
        if (!_processes.TryGetValue(reference.Id.Value, out OwnedProcess? owned) ||
            !owned.Snapshot.Metadata.Scope.Equals(reference.Scope))
        {
            throw OwnershipFailure(
                "hpd.environment.process.unknown",
                $"Process invocation '{reference.Id.Value}' is not owned by this runtime.");
        }
        ValidateRef(reference, owned.Snapshot.Metadata, "process invocation");
        return owned;
    }

    private IRetainedProcessProvider RetainedProcessProvider(OwnedProcess process) =>
        RequireRetainedProcessProvider(
            ProviderById(registry.ProcessProviders, process.ProviderId, "process"));

    private static IRetainedProcessProvider RequireRetainedProcessProvider(IProcessProvider provider)
    {
        if (provider is not IRetainedProcessProvider retained ||
            !retained.ProviderId.Equals(provider.ProviderId))
        {
            throw OwnershipFailure(
                "hpd.environment.process.retention-unsupported",
                $"Provider '{provider.ProviderId.Value}' does not implement retained process lifecycle.");
        }
        return retained;
    }

    private static async Task PumpProcessOutputAsync(
        IProcessProvider provider,
        IRetainedProcessProvider retainedProvider,
        TargetHandle<ProcessInvocation> handle,
        ProcessOutputRetention output,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                bool final = false;
                await foreach (ProcessOutputChunk chunk in provider
                    .ReadOutputAsync(handle, cancellationToken)
                    .ConfigureAwait(false))
                {
                    _ = output.Add(chunk);
                    final |= chunk.Flags.HasFlag(ProcessOutputChunkFlags.Final);
                }
                if (final)
                {
                    break;
                }
                ProcessInvocationStatus status = await retainedProvider
                    .GetStatusAsync(handle, cancellationToken)
                    .ConfigureAwait(false);
                if (IsTerminal(status.ProcessPhase))
                {
                    break;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            output.Fail(RuntimeDiagnostic(
                "hpd.environment.process.output-pump-failed",
                $"Retained process output failed: {exception.Message}"));
        }
        finally
        {
            output.Complete();
        }
    }

    private async ValueTask ReleaseOwnedProcessAsync(
        OwnedProcess process,
        CancellationToken cancellationToken)
    {
        process.OutputCancellation.Cancel();
        await process.OutputPump
            .WaitAsync(_processObservationTimeout, cancellationToken)
            .ConfigureAwait(false);
        await process.Handle.DisposeAsync().ConfigureAwait(false);
        await RetainedProcessProvider(process)
            .ReleaseAsync(process.ProviderResource, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask CleanupUnownedProcessAsync(
        IProcessProvider provider,
        IRetainedProcessProvider retainedProvider,
        IProcessInvocationHandle handle,
        ResourceRef<ProcessInvocation> providerResource)
    {
        using var cleanupCancellation = new CancellationTokenSource(_processObservationTimeout);
        try
        {
            await retainedProvider.StopAsync(
                    handle.Handle,
                    new ProcessStopRequest(
                        StopKind.GracefulThenKill,
                        "retained process start observation failed",
                        _processObservationTimeout),
                    cleanupCancellation.Token)
                .AsTask()
                .WaitAsync(cleanupCancellation.Token)
                .ConfigureAwait(false);
            _ = await provider.WaitAsync(handle.Handle, cleanupCancellation.Token)
                .AsTask()
                .WaitAsync(cleanupCancellation.Token)
                .ConfigureAwait(false);
            await retainedProvider.ReleaseAsync(providerResource, cleanupCancellation.Token)
                .AsTask()
                .WaitAsync(cleanupCancellation.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // The start operation preserves its original failure; providers remain
            // responsible for bounded cleanup of a resource they created but could
            // not make observable.
        }
        finally
        {
            try
            {
                await handle.DisposeAsync()
                    .AsTask()
                    .WaitAsync(_processObservationTimeout)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private async ValueTask<OwnedProcess> RefreshProcessAsync(
        OwnedProcess process,
        CancellationToken cancellationToken)
    {
        using var observationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        observationCancellation.CancelAfter(_processObservationTimeout);
        try
        {
            ProcessInvocationStatus observed = await RetainedProcessProvider(process)
                .GetStatusAsync(process.Handle.Handle, observationCancellation.Token)
                .AsTask()
                .WaitAsync(observationCancellation.Token)
                .ConfigureAwait(false);
            if (observed.Handle is not { } handle || !handle.Equals(process.Handle.Handle))
            {
                return StoreProcessObservationFailure(
                    process,
                    RuntimeDiagnostic(
                        "hpd.environment.process.observe-handle-mismatch",
                        $"Process invocation '{process.Snapshot.Metadata.Id.Value}' returned a mismatched provider handle."));
            }
            ProcessInvocationStatus normalized = observed with
            {
                ObservedGeneration = process.Snapshot.Metadata.Generation,
                Diagnostics = process.Output.Failure is { } outputFailure
                    ? [.. observed.Diagnostics, outputFailure]
                    : observed.Diagnostics,
            };
            OwnedProcess refreshed = process with
            {
                Snapshot = process.Snapshot with { Status = normalized },
            };
            _processes[process.Snapshot.Metadata.Id.Value] = refreshed;
            if (IsTerminal(normalized.ProcessPhase))
            {
                DetachProcessFromUnit(refreshed);
            }
            return refreshed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StoreProcessObservationFailure(
                process,
                RuntimeDiagnostic(
                    "hpd.environment.process.observe-timeout",
                    $"Process invocation '{process.Snapshot.Metadata.Id.Value}' could not be observed within " +
                    $"{_processObservationTimeout.TotalMilliseconds:0} milliseconds."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return StoreProcessObservationFailure(
                process,
                RuntimeDiagnostic(
                    "hpd.environment.process.observe-failed",
                    $"Process invocation '{process.Snapshot.Metadata.Id.Value}' observation failed: {exception.Message}"));
        }
    }

    private OwnedProcess StoreProcessObservationFailure(OwnedProcess process, Diagnostic diagnostic)
    {
        ProcessInvocationStatus status = process.Snapshot.Status with
        {
            Phase = ResourcePhase.Degraded,
            Diagnostics = [.. process.Snapshot.Status.Diagnostics, diagnostic],
            LastTransitionAt = _timeProvider.GetUtcNow(),
        };
        OwnedProcess degraded = process with
        {
            Snapshot = process.Snapshot with { Status = status },
        };
        _processes[process.Snapshot.Metadata.Id.Value] = degraded;
        return degraded;
    }

    private void AttachProcessToUnit(
        OwnedExecutionUnit unit,
        ResourceRef<ProcessInvocation> process,
        ProcessRole role)
    {
        ResourceRef<ProcessInvocation>[] active =
            [.. unit.Snapshot.Status.ActiveProcesses.Where(existing => !SameResource(existing, process)), process];
        ExecutionUnitStatus status = unit.Snapshot.Status with
        {
            Phase = ResourcePhase.Ready,
            UnitPhase = ExecutionUnitPhase.Running,
            PrimaryProcess = role == ProcessRole.Primary
                ? process
                : unit.Snapshot.Status.PrimaryProcess,
            ActiveProcesses = active,
            LastTransitionAt = _timeProvider.GetUtcNow(),
        };
        _units[unit.Snapshot.Metadata.Id.Value] = unit with
        {
            Snapshot = unit.Snapshot with { Status = status },
        };
    }

    private void DetachProcessFromUnit(OwnedProcess process)
    {
        if (!_units.TryGetValue(process.Unit.Id.Value, out OwnedExecutionUnit? unit))
        {
            return;
        }
        ResourceRef<ProcessInvocation> processRef = Ref(process.Snapshot.Metadata);
        ResourceRef<ProcessInvocation>[] active = unit.Snapshot.Status.ActiveProcesses
            .Where(existing => !SameResource(existing, processRef))
            .ToArray();
        bool primary = unit.Snapshot.Status.PrimaryProcess is { } primaryProcess &&
            SameResource(primaryProcess, processRef);
        ExecutionUnitStatus status = unit.Snapshot.Status with
        {
            UnitPhase = active.Length == 0 ? ExecutionUnitPhase.Ready : ExecutionUnitPhase.Running,
            ActiveProcesses = active,
            PrimaryProcessResult = primary
                ? process.Snapshot.Status.Result
                : unit.Snapshot.Status.PrimaryProcessResult,
            LastTransitionAt = _timeProvider.GetUtcNow(),
        };
        _units[unit.Snapshot.Metadata.Id.Value] = unit with
        {
            Snapshot = unit.Snapshot with { Status = status },
        };
    }

    private static ProcessInvocationPhase ProcessPhase(ProcessCompletionKind completionKind) =>
        completionKind switch
        {
            ProcessCompletionKind.FailedToStart or ProcessCompletionKind.Faulted =>
                ProcessInvocationPhase.Failed,
            ProcessCompletionKind.Stopped or ProcessCompletionKind.Killed or
                ProcessCompletionKind.Cancelled or ProcessCompletionKind.TimedOut =>
                ProcessInvocationPhase.Stopped,
            _ => ProcessInvocationPhase.Exited,
        };

    private static bool IsTerminal(ProcessInvocationPhase phase) =>
        phase is ProcessInvocationPhase.Exited or
            ProcessInvocationPhase.Stopped or
            ProcessInvocationPhase.Failed;

    private IEnumerable<OwnedExecutionUnit> UnitsForCurrentHost() =>
        _host is null
            ? []
            : _units.Values.Where(unit => unit.Snapshot.Spec.PreferredHost is { } host &&
                host.Id.Equals(_host.Snapshot.Metadata.Id) &&
                host.Scope.Equals(_host.Snapshot.Metadata.Scope)).ToArray();

    private bool HasCurrentHostDependents() =>
        UnitsForCurrentHost().Any() ||
        EnginesForCurrentHost().Any() ||
        AuthoritiesForCurrentHost().Any();

    private bool HasActiveUnitDependents(OwnedExecutionUnit unit)
    {
        ExecutionUnitStatus status = unit.Snapshot.Status;
        if (status.Phase is ResourcePhase.Degraded or ResourcePhase.Failed ||
            status.UnitPhase is ExecutionUnitPhase.Starting or ExecutionUnitPhase.Running or
                ExecutionUnitPhase.Stopping or ExecutionUnitPhase.Deleting ||
            status.ActiveProcesses.Count > 0 ||
            status.AuthorityBindings.Count > 0 ||
            status.RealizedContentProjections.Count > 0 ||
            status.NetworkMemberships.Count > 0 ||
            status.PublishedEndpoints.Count > 0)
        {
            return true;
        }

        TargetHandle<ExecutionUnit>? handle = status.Handle;
        return handle is not null &&
            (_authorities.Values.Any(authority => authority.Snapshot.Spec.Target.Unit is { } target &&
                target.Equals(handle.Value)) ||
             _activeHandles.Values.Any(process => process.Spec.Target.Equals(handle.Value)) ||
             _activeRuns.Values.Any(process => process.Spec.Target.Equals(handle.Value)));
    }

    private async ValueTask<OwnedExecutionUnit> RefreshUnitAsync(
        OwnedExecutionUnit unit,
        CancellationToken cancellationToken)
    {
        if (unit.Snapshot.Status.Handle is not { } handle)
        {
            return StoreUnitObservationFailure(
                unit,
                RuntimeDiagnostic(
                    "hpd.environment.execution-unit.observe-handle-missing",
                    $"Execution unit '{unit.Snapshot.Metadata.Id.Value}' has no provider handle and cannot be observed."));
        }

        using var observationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        observationCancellation.CancelAfter(_executionUnitObservationTimeout);
        try
        {
            ExecutionUnitStatus status = await ProviderById(
                    registry.ExecutionUnitProviders,
                    unit.ProviderId,
                    "execution unit")
                .GetStatusAsync(handle, observationCancellation.Token)
                .AsTask()
                .WaitAsync(observationCancellation.Token)
                .ConfigureAwait(false);
            Diagnostic? identityDiagnostic = ValidateUnitObservation(unit, status);
            if (identityDiagnostic is not null)
            {
                return StoreUnitObservationFailure(unit, identityDiagnostic);
            }

            ResourceRef<ProcessInvocation>[] runtimeProcesses = _processes.Values
                .Where(process => SameResource(process.Unit, Ref(unit.Snapshot.Metadata)) &&
                    !IsTerminal(process.Snapshot.Status.ProcessPhase))
                .Select(process => Ref(process.Snapshot.Metadata))
                .ToArray();
            ResourceRef<ProcessInvocation>[] providerProcesses = status.ActiveProcesses
                .Select(providerProcess =>
                    _processes.Values.FirstOrDefault(process =>
                        SameResource(process.Unit, Ref(unit.Snapshot.Metadata)) &&
                        SameResource(process.ProviderResource, providerProcess)) is { } runtimeProcess
                            ? Ref(runtimeProcess.Snapshot.Metadata)
                            : providerProcess)
                .ToArray();
            IReadOnlyList<ResourceRef<ProcessInvocation>> activeProcesses =
                runtimeProcesses.Length == 0
                    ? providerProcesses
                    : providerProcesses.Concat(runtimeProcesses).Distinct().ToArray();
            status = status with
            {
                UnitPhase = activeProcesses.Count > 0
                    ? ExecutionUnitPhase.Running
                    : status.UnitPhase,
                ActiveProcesses = activeProcesses,
                PrimaryProcess = unit.Snapshot.Status.PrimaryProcess,
                PrimaryProcessResult = unit.Snapshot.Status.PrimaryProcessResult,
            };
            OwnedExecutionUnit refreshed = unit with
            {
                Snapshot = unit.Snapshot with { Status = status },
            };
            _units[unit.Snapshot.Metadata.Id.Value] = refreshed;
            return refreshed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StoreUnitObservationFailure(
                unit,
                RuntimeDiagnostic(
                    "hpd.environment.execution-unit.observe-timeout",
                    $"Execution unit '{unit.Snapshot.Metadata.Id.Value}' could not be observed within " +
                    $"{_executionUnitObservationTimeout.TotalMilliseconds:0} milliseconds."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return StoreUnitObservationFailure(
                unit,
                RuntimeDiagnostic(
                    "hpd.environment.execution-unit.observe-failed",
                    $"Execution unit '{unit.Snapshot.Metadata.Id.Value}' observation failed: {exception.Message}"));
        }
    }

    private Diagnostic? ValidateUnitObservation(
        OwnedExecutionUnit unit,
        ExecutionUnitStatus status)
    {
        if (!status.ObservedGeneration.Equals(unit.Snapshot.Metadata.Generation))
        {
            return RuntimeDiagnostic(
                "hpd.environment.execution-unit.observe-generation-mismatch",
                $"Execution unit '{unit.Snapshot.Metadata.Id.Value}' returned observed generation " +
                $"'{status.ObservedGeneration.Value}', expected '{unit.Snapshot.Metadata.Generation.Value}'.");
        }
        if (unit.Snapshot.Status.Handle is { } expectedHandle &&
            (status.Handle is not { } actualHandle || !actualHandle.Equals(expectedHandle)))
        {
            return RuntimeDiagnostic(
                "hpd.environment.execution-unit.observe-handle-mismatch",
                $"Execution unit '{unit.Snapshot.Metadata.Id.Value}' returned a mismatched provider handle.");
        }
        if (!Nullable.Equals(
                unit.Snapshot.Status.NamespaceHandle,
                status.NamespaceHandle))
        {
            return RuntimeDiagnostic(
                "hpd.environment.execution-unit.observe-namespace-handle-mismatch",
                $"Execution unit '{unit.Snapshot.Metadata.Id.Value}' returned a missing or mismatched " +
                "provider namespace handle.");
        }
        if (unit.Snapshot.Spec.PreferredHost is { } expectedHost &&
            (status.AssignedHost is not { } actualHost ||
             !SameResource(expectedHost, actualHost)))
        {
            return RuntimeDiagnostic(
                "hpd.environment.execution-unit.observe-host-mismatch",
                $"Execution unit '{unit.Snapshot.Metadata.Id.Value}' returned a mismatched assigned host.");
        }
        return null;
    }

    private OwnedExecutionUnit StoreUnitObservationFailure(
        OwnedExecutionUnit unit,
        Diagnostic diagnostic)
    {
        ExecutionUnitStatus degraded = unit.Snapshot.Status with
        {
            Phase = ResourcePhase.Degraded,
            LastTransitionAt = _timeProvider.GetUtcNow(),
            Diagnostics = [.. unit.Snapshot.Status.Diagnostics, diagnostic],
        };
        OwnedExecutionUnit retained = unit with
        {
            Snapshot = unit.Snapshot with { Status = degraded },
        };
        _units[unit.Snapshot.Metadata.Id.Value] = retained;
        return retained;
    }

    private IEnumerable<OwnedAuthorityBinding> AuthoritiesForCurrentHost() =>
        _host is null
            ? []
            : _authorities.Values.Where(authority => authority.Host is { } host &&
                host.Id.Equals(_host.Snapshot.Metadata.Id) &&
                host.Scope.Equals(_host.Snapshot.Metadata.Scope)).ToArray();

    private IEnumerable<OwnedProcess> ProcessesForUnit(ResourceRef<ExecutionUnit> unit) =>
        _processes.Values.Where(process => SameResource(process.Unit, unit)).ToArray();

    private IEnumerable<OwnedProcess> ProcessesForCurrentHost() =>
        _host is null
            ? []
            : _processes.Values.Where(process =>
                _units.TryGetValue(process.Unit.Id.Value, out OwnedExecutionUnit? unit) &&
                unit.Snapshot.Spec.PreferredHost is { } host &&
                host.Id.Equals(_host.Snapshot.Metadata.Id) &&
                host.Scope.Equals(_host.Snapshot.Metadata.Scope)).ToArray();

    private IEnumerable<KeyValuePair<EngineIdentity, OwnedEngine>> EnginesForCurrentHost() =>
        _host is null
            ? []
            : _engines.Where(pair =>
                pair.Key.HostId == _host.Snapshot.Metadata.Id.Value &&
                pair.Key.Scope == _host.Snapshot.Metadata.Scope.Value).ToArray();

    private ResourceMetadata<TResource> Metadata<TResource>(string kind)
        where TResource : IExecutionResourceMarker
    {
        long generation = Interlocked.Increment(ref _generation);
        return new ResourceMetadata<TResource>
        {
            Id = new ResourceId<TResource>($"{kind}-{generation}"),
            Kind = new ResourceKind(kind),
            Scope = new ResourceScope("in-memory-runtime"),
            SchemaVersion = new SchemaVersion("v1"),
            Generation = new ResourceGeneration(generation),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private ResourceMetadata<TResource> Advance<TResource>(ResourceMetadata<TResource> metadata)
        where TResource : IExecutionResourceMarker =>
        metadata with
        {
            Generation = new ResourceGeneration(Interlocked.Increment(ref _generation)),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static string Fingerprint(RuntimeHostSpec spec)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            spec,
            RuntimeSpecJsonContext.Default.RuntimeHostSpec);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static string Fingerprint(EngineControlPlaneSpec spec)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            spec,
            RuntimeSpecJsonContext.Default.EngineControlPlaneSpec);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static string Fingerprint(AuthorityBindingSpec spec)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            spec,
            RuntimeSpecJsonContext.Default.AuthorityBindingSpec);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static string Fingerprint(ExecutionUnitSpec spec)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            spec,
            RuntimeSpecJsonContext.Default.ExecutionUnitSpec);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static ExecutionUnitIdentity? UnitIdentity(ExecutionUnitSpec spec)
    {
        if (spec.ReconciliationKey is not { } key || string.IsNullOrWhiteSpace(key.Value))
        {
            return null;
        }
        ResourceRef<RuntimeHost> host = spec.PreferredHost ??
            throw OwnershipFailure(
                "hpd.environment.execution-unit.host-required",
                "A reconciled execution unit requires an owned runtime host.");
        return new ExecutionUnitIdentity(
            host.Scope.Value,
            host.Id.Value,
            host.Generation?.Value,
            key.Value);
    }

    private static ResourceRef<TResource> Ref<TResource>(ResourceMetadata<TResource> metadata)
        where TResource : IExecutionResourceMarker =>
        new(metadata.Id, metadata.Scope, metadata.Generation);

    private static UntypedResourceRef Untyped<TResource>(ResourceRef<TResource> reference)
        where TResource : IExecutionResourceMarker =>
        new(new ResourceKind(typeof(TResource).Name), reference.Id.Value, reference.Scope, reference.Generation);

    private static void ValidateRef<TResource>(
        ResourceRef<TResource> reference,
        ResourceMetadata<TResource> metadata,
        string family)
        where TResource : IExecutionResourceMarker
    {
        if (!reference.Id.Equals(metadata.Id) ||
            !reference.Scope.Equals(metadata.Scope) ||
            (reference.Generation is { } generation && !generation.Equals(metadata.Generation)))
        {
            throw OwnershipFailure(
                "hpd.environment.resource.stale-or-mismatched",
                $"The {family} reference is stale or does not match runtime ownership.");
        }
    }

    private static TProvider SelectProvider<TProvider>(
        IReadOnlyList<TProvider> providers,
        ProviderId? preferred,
        string family)
        where TProvider : class
    {
        if (preferred is { } providerId)
        {
            return ProviderById(providers, providerId, family);
        }
        if (providers.Count == 1)
        {
            return providers[0];
        }
        throw OwnershipFailure(
            "hpd.environment.provider-selection-required",
            providers.Count == 0
                ? $"No {family} provider is registered."
                : $"Multiple {family} providers are registered; an explicit provider selection is required.");
    }

    private static TProvider ProviderForRoute<TProvider>(
        IReadOnlyList<TProvider> providers,
        TargetRoute route,
        string family)
        where TProvider : class
    {
        if (route.ProviderId is not { } providerId)
        {
            throw OwnershipFailure(
                "hpd.environment.target.provider-missing",
                $"The target route does not identify its owning {family} provider.");
        }
        return ProviderById(providers, providerId, family);
    }

    private static TProvider ProviderById<TProvider>(
        IReadOnlyList<TProvider> providers,
        ProviderId providerId,
        string family)
        where TProvider : class
    {
        TProvider? provider = providers.FirstOrDefault(candidate =>
            candidate is IRuntimeHostProvider host && host.ProviderId.Equals(providerId) ||
            candidate is IExecutionUnitProvider unit && unit.ProviderId.Equals(providerId) ||
            candidate is IProcessProvider process && process.ProviderId.Equals(providerId) ||
            candidate is IFunctionSandboxProvider sandbox && sandbox.ProviderId.Equals(providerId) ||
            candidate is IAuthorityBindingProvider authority && authority.ProviderId.Equals(providerId) ||
            candidate is IEngineControlPlaneProvider engine && engine.ProviderId.Equals(providerId));
        return provider ?? throw OwnershipFailure(
            "hpd.environment.provider-owner-unavailable",
            $"The provider '{providerId.Value}' that owns the {family} resource is not registered.");
    }

    private static RuntimeResourceOwnershipException OwnershipFailure(string code, string message) =>
        new(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(code),
            Message = message,
        });

    private static Diagnostic RuntimeDiagnostic(string code, string message) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(code),
            Message = message,
        };

    private sealed record OwnedHost(
        ProviderId ProviderId,
        string SpecFingerprint,
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> Snapshot);
    private sealed record OwnedExecutionUnit(
        ProviderId ProviderId,
        ExecutionUnitIdentity? Identity,
        string SpecFingerprint,
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> Snapshot);
    private sealed record OwnedEngine(
        ProviderId ProviderId,
        string SpecFingerprint,
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> Snapshot);
    private sealed record OwnedAuthorityBinding(
        ProviderId ProviderId,
        ResourceRef<RuntimeHost>? Host,
        ResourceRef<EngineControlPlane>? SourceEngine,
        ResourceSnapshot<AuthorityBinding, AuthorityBindingSpec, AuthorityBindingStatus> Snapshot);
    private sealed record OwnedProcess(
        ProviderId ProviderId,
        ResourceRef<ExecutionUnit> Unit,
        ResourceRef<ProcessInvocation> ProviderResource,
        IProcessInvocationHandle Handle,
        long ActiveHandleId,
        ProcessOutputRetention Output,
        CancellationTokenSource OutputCancellation,
        Task OutputPump,
        ResourceSnapshot<ProcessInvocation, ProcessInvocationSpec, ProcessInvocationStatus> Snapshot);
    private sealed class ProcessOutputRetention(int maxBytesPerStream)
    {
        private const int MaxChunks = 1024;
        private readonly object _gate = new();
        private readonly List<ProcessOutputChunk> _chunks = [];
        private readonly int _maxBytesPerStream = Math.Max(0, maxBytesPerStream);
        private long _stdoutBytes;
        private long _stderrBytes;
        private bool _complete;
        private Diagnostic? _failure;
        private TaskCompletionSource _changed = NewSignal();

        public long HighestSequence
        {
            get
            {
                lock (_gate)
                {
                    return _chunks.Count == 0 ? 0 : _chunks[^1].Sequence;
                }
            }
        }

        public bool IsComplete
        {
            get
            {
                lock (_gate)
                {
                    return _complete;
                }
            }
        }

        public Diagnostic? Failure
        {
            get
            {
                lock (_gate)
                {
                    return _failure;
                }
            }
        }

        public ProcessOutputChunk Add(ProcessOutputChunk chunk)
        {
            lock (_gate)
            {
                ProcessOutputChunk retained = BoundChunk(chunk);
                _chunks.Add(retained);
                AddBytes(retained.Stream, retained.Bytes.Length);
                while (_chunks.Count > MaxChunks ||
                    Bytes(retained.Stream) > _maxBytesPerStream)
                {
                    int index = _chunks.FindIndex(existing => existing.Stream == retained.Stream);
                    if (index < 0)
                    {
                        break;
                    }
                    ProcessOutputChunk removed = _chunks[index];
                    _chunks.RemoveAt(index);
                    AddBytes(removed.Stream, -removed.Bytes.Length);
                }
                Pulse();
                return retained;
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                _complete = true;
                Pulse();
            }
        }

        public void Fail(Diagnostic diagnostic)
        {
            lock (_gate)
            {
                _failure = diagnostic;
                _complete = true;
                Pulse();
            }
        }

        public Task WaitForChangeAsync(long afterSequence, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_complete || (_chunks.Count > 0 && _chunks[^1].Sequence > afterSequence))
                {
                    return Task.CompletedTask;
                }
                return _changed.Task.WaitAsync(cancellationToken);
            }
        }

        public ProcessOutputChunk[] Snapshot(long? afterSequence, int limit)
        {
            lock (_gate)
            {
                return _chunks
                    .Where(chunk => chunk.Sequence > (afterSequence ?? 0))
                    .Take(Math.Max(0, limit))
                    .ToArray();
            }
        }

        private ProcessOutputChunk BoundChunk(ProcessOutputChunk chunk)
        {
            if (chunk.Bytes.Length <= _maxBytesPerStream)
            {
                return chunk;
            }
            ReadOnlyMemory<byte> tail = _maxBytesPerStream == 0
                ? ReadOnlyMemory<byte>.Empty
                : chunk.Bytes[^_maxBytesPerStream..].ToArray();
            return chunk with
            {
                Bytes = tail,
                Flags = chunk.Flags | ProcessOutputChunkFlags.Truncated,
            };
        }

        private long Bytes(ProcessOutputStream stream) =>
            stream == ProcessOutputStream.Stdout ? _stdoutBytes : _stderrBytes;

        private void AddBytes(ProcessOutputStream stream, int value)
        {
            if (stream == ProcessOutputStream.Stdout)
            {
                _stdoutBytes += value;
            }
            else
            {
                _stderrBytes += value;
            }
        }

        private void Pulse()
        {
            TaskCompletionSource changed = _changed;
            _changed = NewSignal();
            changed.TrySetResult();
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly record struct EngineIdentity(
        string Scope,
        string HostId,
        EngineControlPlaneKind Kind,
        EngineApiKind Api);
    private readonly record struct ExecutionUnitIdentity(
        string Scope,
        string HostId,
        long? HostGeneration,
        string Key);
    private sealed record ActiveRun(
        ProcessInvocationSpec Spec,
        CancellationTokenSource Cancellation,
        Task Completion);
    private sealed record PendingEngineAuthorityPlan(
        ResourceRef<EngineControlPlane> SourceEngine,
        string SpecFingerprint,
        DateTimeOffset ExpiresAt);
    private sealed record CleanupContext(
        CleanupPolicy Policy,
        CancellationToken CallerToken,
        CancellationToken OverallToken)
    {
        public List<Diagnostic> Diagnostics { get; } = [];
    }
    private sealed class CleanupRetainedException : Exception
    {
    }

    private sealed class RuntimeOwnedProcessHandle(
        IProcessInvocationHandle inner,
        Action release) : IProcessInvocationHandle
    {
        private int _released;
        public TargetHandle<ProcessInvocation> Handle => inner.Handle;
        public ResourceRef<ProcessInvocation>? Resource => inner.Resource;
        public ProcessInvocationSpec Spec => inner.Spec;
        public ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) =>
            inner.WriteStdinAsync(bytes, cancellationToken);
        public ValueTask CloseStdinAsync(CancellationToken cancellationToken = default) =>
            inner.CloseStdinAsync(cancellationToken);
        public ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default) =>
            inner.SignalAsync(signal, cancellationToken);
        public ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default) =>
            inner.StopAsync(request, cancellationToken);
        public ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default) =>
            inner.ResizeTerminalAsync(size, cancellationToken);
        public async ValueTask<ProcessInvocationResult> WaitAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Release();
            }
        }
        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(CancellationToken cancellationToken = default) =>
            inner.ReadOutputAsync(cancellationToken);
        public async ValueTask DisposeAsync()
        {
            try
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Release();
            }
        }
        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                release();
            }
        }
    }
}

public sealed class RuntimeResourceOwnershipException(Diagnostic diagnostic)
    : InvalidOperationException(diagnostic.Message)
{
    public Diagnostic Diagnostic { get; } = diagnostic;
}

public sealed class RuntimeCleanupException(string step, Exception innerException)
    : InvalidOperationException($"Runtime cleanup failed during {step}; runtime ownership was retained.", innerException)
{
    public string Step { get; } = step;
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(RuntimeHostSpec))]
[JsonSerializable(typeof(EngineControlPlaneSpec))]
[JsonSerializable(typeof(AuthorityBindingSpec))]
[JsonSerializable(typeof(ExecutionUnitSpec))]
internal sealed partial class RuntimeSpecJsonContext : JsonSerializerContext;

public interface IRuntimeFinalizationParticipant
{
    ValueTask<RuntimeFinalizationResult> FinalizeRuntimeAsync(
        RuntimeFinalizationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryEnvironmentProviderModule : IProviderModule
{
    private readonly InMemoryEnvironmentProvider _provider;

    public InMemoryEnvironmentProviderModule()
        : this(new InMemoryEnvironmentProvider())
    {
    }

    public InMemoryEnvironmentProviderModule(InMemoryEnvironmentProvider provider)
    {
        _provider = provider;
    }

    public ProviderDescriptor Descriptor { get; } = new()
    {
        Id = InMemoryEnvironmentProvider.InMemoryProviderId,
        DisplayName = "HPD In-Memory Environment Provider",
        ContractVersion = new SemanticVersion(1, 0, 0),
        ProviderVersion = new SemanticVersion(1, 0, 0),
        ContractKinds =
            ProviderContractKind.RuntimeHost |
            ProviderContractKind.ExecutionUnit |
            ProviderContractKind.ProcessInvocation |
            ProviderContractKind.Artifact |
            ProviderContractKind.RootFilesystemView |
            ProviderContractKind.ContentProjection |
            ProviderContractKind.Network |
            ProviderContractKind.NetworkMembership |
            ProviderContractKind.EndpointPublication |
            ProviderContractKind.AuthorityBinding |
            ProviderContractKind.FunctionSandbox |
            ProviderContractKind.FunctionInvocation |
            ProviderContractKind.HostFunctionBinding |
            ProviderContractKind.FunctionSnapshot |
            ProviderContractKind.EngineControlPlane,
        TrustLevel = ProviderTrustLevel.BuiltIn,
        DefaultActivationScope = ProviderActivationScope.Runtime,
        ActivationModels =
        [
            new ProviderActivationModel(ProviderActivationKind.InProcess, ProviderActivationScope.Runtime, ProviderTransportKind.None),
        ],
        HostPlatforms = [EnvironmentProviderRegistry.DefaultPlatform()],
    };

    public void Register(IProviderRegistrationBuilder builder)
    {
        builder.AddRuntimeHostProvider(_provider);
        builder.AddRuntimeHostResetProvider(_provider);
        builder.AddExecutionUnitProvider(_provider);
        builder.AddProcessProvider(_provider);
        builder.AddFunctionSandboxProvider(_provider);
        builder.AddFunctionSnapshotProvider(_provider);
        builder.AddArtifactProvider(_provider);
        builder.AddRootFilesystemProvider(_provider);
        builder.AddContentProjectionProvider(_provider);
        builder.AddNetworkProvider(_provider);
        builder.AddNetworkMembershipProvider(_provider);
        builder.AddEndpointPublicationProvider(_provider);
        builder.AddAuthorityBindingProvider(_provider);
        builder.AddEngineControlPlaneProvider(_provider);
    }

    public void RegisterJsonTypes(IProviderJsonTypeRegistry registry)
    {
    }
}

public sealed class InMemoryEnvironmentProvider :
    IRuntimeHostProvider,
    IRuntimeHostResetProvider,
    IExecutionUnitProvider,
    IProcessProvider,
    IRetainedProcessProvider,
    IFunctionSandboxProvider,
    IFunctionSnapshotProvider,
    IArtifactProvider,
    IRootFilesystemProvider,
    IContentProjectionProvider,
    INetworkProvider,
    INetworkMembershipProvider,
    IEndpointPublicationProvider,
    IAuthorityBindingProvider,
    IEngineControlPlaneProvider
{
    public static ProviderId InMemoryProviderId { get; } = new("hpd.execution.in-memory");

    private readonly ConcurrentDictionary<string, object> _resources = new(StringComparer.Ordinal);
    private long _sequence;

    public ProviderId ProviderId => InMemoryProviderId;

    public ValueTask<RuntimeHostStatus> EnsureAsync(ResourceMetadata<RuntimeHost> metadata, RuntimeHostSpec spec, RuntimeHostStatus? observed, CancellationToken cancellationToken = default)
    {
        var status = new RuntimeHostStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            HostPhase = RuntimeHostPhase.Ready,
            Handle = Handle<RuntimeHost>(TargetRouteSegmentKind.RuntimeHost, metadata.Id.Value),
            ObservedCapacity = new CapacityObservation(spec.Capacity.CpuCores ?? 1, spec.Capacity.MemoryBytes ?? 512 * 1024 * 1024, spec.Capacity.StorageBytes ?? 1024 * 1024 * 1024),
            Readiness = new RuntimeHostReadinessStatus(Ready: true, Gates: spec.Bootstrap?.ReadinessGates.Select(gate => new ReadinessGateStatus(gate.Name, gate.Kind, ConditionStatus.True, DateTimeOffset.UtcNow, "Satisfied by in-memory provider.")).ToArray()),
            GuestControl = new GuestControlStatus(Expected: false, Installed: false, Reachable: false),
            ControlPlane = new RuntimeHostControlPlaneStatus(Components:
            [
                new ProviderComponentStatus(ProviderComponentKind.ProviderDefined, "in-memory-runtime-host", ProviderComponentPhase.Ready),
            ]),
        };
        _resources[metadata.Id.Value] = status;
        return ValueTask.FromResult(status);
    }

    public ValueTask<RuntimeHostStatus> StopAsync(TargetHandle<RuntimeHost> host, StopPolicy policy, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RuntimeHostStatus { Phase = ResourcePhase.Ready, HostPhase = RuntimeHostPhase.Stopped, Handle = host });

    public ValueTask DeleteAsync(ResourceRef<RuntimeHost> host, CancellationToken cancellationToken = default)
    {
        _resources.TryRemove(host.Id.Value, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<RuntimeHostStatus> GetStatusAsync(TargetHandle<RuntimeHost> host, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RuntimeHostStatus { Phase = ResourcePhase.Ready, HostPhase = RuntimeHostPhase.Ready, Handle = host });

    public ValueTask<RuntimeHostResetResult> ResetAsync(TargetHandle<RuntimeHost> host, RuntimeHostResetRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RuntimeHostResetResult(
            request.Scope,
            new ResourceRef<RuntimeHost>(new ResourceId<RuntimeHost>($"runtime-host-reset-{NextSequence()}"), new ResourceScope("in-memory-runtime")),
            DateTimeOffset.UtcNow));

    public ValueTask<ExecutionUnitStatus> EnsureAsync(ResourceMetadata<ExecutionUnit> metadata, ExecutionUnitSpec spec, ExecutionUnitStatus? observed, CancellationToken cancellationToken = default)
    {
        var status = new ExecutionUnitStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            UnitPhase = ExecutionUnitPhase.Ready,
            AssignedHost = spec.PreferredHost,
            Handle = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, metadata.Id.Value),
            RealizedRootfs = spec.Rootfs,
            RealizedContentProjections = spec.ContentProjections,
        };
        _resources[metadata.Id.Value] = status;
        return ValueTask.FromResult(status);
    }

    public ValueTask<ExecutionUnitStatus> StopAsync(TargetHandle<ExecutionUnit> unit, StopPolicy policy, CancellationToken cancellationToken = default)
    {
        string id = unit.Route.Segments.Last().Value;
        ExecutionUnitStatus current = _resources.TryGetValue(id, out object? value) &&
            value is ExecutionUnitStatus status
                ? status
                : new ExecutionUnitStatus { Handle = unit, UnitPhase = ExecutionUnitPhase.Unknown };
        ExecutionUnitStatus stopped = current with
        {
            Phase = ResourcePhase.Ready,
            UnitPhase = ExecutionUnitPhase.Stopped,
            Handle = unit,
        };
        _resources[id] = stopped;
        return ValueTask.FromResult(stopped);
    }

    public ValueTask DeleteAsync(ResourceRef<ExecutionUnit> unit, CancellationToken cancellationToken = default)
    {
        _resources.TryRemove(unit.Id.Value, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ExecutionUnitStatus> GetStatusAsync(TargetHandle<ExecutionUnit> unit, CancellationToken cancellationToken = default)
    {
        string id = unit.Route.Segments.Last().Value;
        return _resources.TryGetValue(id, out object? value) && value is ExecutionUnitStatus status
            ? ValueTask.FromResult(status)
            : ValueTask.FromResult(new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Failed,
                UnitPhase = ExecutionUnitPhase.Failed,
                Handle = unit,
            });
    }

    public ValueTask<IProcessInvocationHandle> StartAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default)
    {
        var handle = new InMemoryProcessInvocationHandle(spec, CreateProcessResult(spec, ProcessCompletionKind.Completed), NextSequence);
        ResourceRef<ProcessInvocation> resource = handle.Resource!.Value;
        _resources[resource.Id.Value] = new ProcessInvocationStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = resource.Generation ?? new ResourceGeneration(1),
            ProcessPhase = ProcessInvocationPhase.Running,
            Handle = handle.Handle,
            ProviderProcessId = resource.Id.Value,
            IoState = ProcessIoState.Open,
            StartedAt = DateTimeOffset.UtcNow,
        };
        return ValueTask.FromResult<IProcessInvocationHandle>(handle);
    }

    public async ValueTask<ProcessInvocationResult> RunAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (output is not null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(string.Join(" ", spec.Command.Arguments.Prepend(spec.Command.FileName)));
            await output.OnOutputAsync(new ProcessOutputChunk(
                Handle<ProcessInvocation>(TargetRouteSegmentKind.ProcessInvocation, $"process-{NextSequence()}"),
                ProcessOutputStream.Stdout,
                NextSequence(),
                DateTimeOffset.UtcNow,
                bytes,
                ProcessOutputChunkFlags.Final), cancellationToken).ConfigureAwait(false);
        }

        if (spec.Policy.Timeout == TimeSpan.Zero)
        {
            return CreateProcessResult(spec, ProcessCompletionKind.TimedOut, outputDrainTimedOut: true);
        }

        return CreateProcessResult(spec, ProcessCompletionKind.Completed);
    }

    public ValueTask SignalAsync(TargetHandle<ProcessInvocation> process, ProcessSignal signal, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask ResizeTerminalAsync(TargetHandle<ProcessInvocation> process, TerminalSpec size, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<ProcessInvocationResult> WaitAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default)
    {
        ProcessInvocationResult result = CreateProcessResult(null, ProcessCompletionKind.Completed);
        string id = process.Route.Segments.Last().Value;
        if (_resources.TryGetValue(id, out object? value) && value is ProcessInvocationStatus status)
        {
            _resources[id] = status with
            {
                ProcessPhase = ProcessInvocationPhase.Exited,
                IoState = ProcessIoState.Closed,
                Result = result,
                ExitedAt = result.ExitedAt,
            };
        }
        return ValueTask.FromResult(result);
    }

    public ValueTask<ProcessInvocationStatus> GetStatusAsync(
        TargetHandle<ProcessInvocation> process,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string id = process.Route.Segments.Last().Value;
        return _resources.TryGetValue(id, out object? value) && value is ProcessInvocationStatus status
            ? ValueTask.FromResult(status)
            : throw new InvalidOperationException(
                $"Process '{id}' is not owned by the in-memory provider.");
    }

    public ValueTask StopAsync(
        TargetHandle<ProcessInvocation> process,
        ProcessStopRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string id = process.Route.Segments.Last().Value;
        if (!_resources.TryGetValue(id, out object? value) || value is not ProcessInvocationStatus status)
        {
            throw new InvalidOperationException(
                $"Process '{id}' is not owned by the in-memory provider.");
        }
        _resources[id] = status with
        {
            ProcessPhase = ProcessInvocationPhase.Stopped,
            IoState = ProcessIoState.Closed,
            Result = CreateProcessResult(null, ProcessCompletionKind.Stopped),
            ExitedAt = DateTimeOffset.UtcNow,
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAsync(
        ResourceRef<ProcessInvocation> process,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _resources.TryRemove(process.Id.Value, out _);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(TargetHandle<ProcessInvocation> process, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new ProcessOutputChunk(process, ProcessOutputStream.Stdout, NextSequence(), DateTimeOffset.UtcNow, "in-memory"u8.ToArray(), ProcessOutputChunkFlags.Final);
    }

    public ValueTask<FunctionSandboxStatus> EnsureAsync(ResourceMetadata<FunctionSandbox> metadata, FunctionSandboxSpec spec, FunctionSandboxStatus? observed, CancellationToken cancellationToken = default)
    {
        var status = new FunctionSandboxStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            SandboxPhase = FunctionSandboxPhase.Ready,
            Handle = Handle<FunctionSandbox>(TargetRouteSegmentKind.FunctionSandbox, metadata.Id.Value),
            ResolvedGuestBinary = spec.GuestBinary,
            GuestAbi = spec.RequiredGuestAbi,
            Generation = new FunctionSandboxGeneration((ulong)NextSequence()),
            HostFunctions = spec.HostFunctionBindings.Select(binding => new FunctionBindingStatus(
                new HostFunctionName(binding.Id.Value),
                new FunctionSignature { Name = new FunctionName(binding.Id.Value) },
                Registered: true)).ToArray(),
        };
        _resources[metadata.Id.Value] = status;
        return ValueTask.FromResult(status);
    }

    public async ValueTask<FunctionInvocationResult> InvokeAsync(FunctionInvocationSpec spec, IFunctionObservationSink? observations = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (spec.Policy.Timeout == TimeSpan.Zero)
        {
            return new FunctionInvocationResult
            {
                InvocationId = new ResourceId<FunctionInvocation>($"function-{NextSequence()}"),
                CompletionKind = FunctionInvocationCompletionKind.TimedOut,
                Poison = new FunctionSandboxPoisonStatus(false, FunctionPoisonReason.Unknown, Restorable: true, "Invocation timed out before guest dispatch."),
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            };
        }

        if (observations is not null)
        {
            await observations.OnFunctionEventAsync(new ExecutionEventChunk(
                new EventId($"function-stream-{NextSequence()}"),
                NextSequence(),
                DateTimeOffset.UtcNow,
                ObservationKind.Event,
                Encoding.UTF8.GetBytes(spec.Function.Value),
                new SchemaId("hpd.execution.function.event"),
                new ContentType("text/plain")), cancellationToken).ConfigureAwait(false);
        }

        return new FunctionInvocationResult
        {
            InvocationId = new ResourceId<FunctionInvocation>($"function-{NextSequence()}"),
            CompletionKind = FunctionInvocationCompletionKind.Returned,
            ReturnValue = spec.ExpectedReturn is { Kind: FunctionValueKind.Int32 } ? new FunctionValue(FunctionValueKind.Int32, Int32: 0) : FunctionValue.Void,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }

    public ValueTask<FunctionSandboxStatus> GetStatusAsync(TargetHandle<FunctionSandbox> sandbox, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new FunctionSandboxStatus { Phase = ResourcePhase.Ready, SandboxPhase = FunctionSandboxPhase.Ready, Handle = sandbox });

    public ValueTask ReleaseAsync(TargetHandle<FunctionSandbox> sandbox, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<FunctionSandboxSnapshotStatus> CaptureAsync(TargetHandle<FunctionSandbox> sandbox, FunctionSnapshotRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new FunctionSandboxSnapshotStatus
        {
            Phase = ResourcePhase.Ready,
            SnapshotPhase = FunctionSandboxSnapshotPhase.Ready,
            SandboxGeneration = new FunctionSandboxGeneration((ulong)NextSequence()),
            Digest = new Digest("sha256", "inmemory"),
            Size = new ByteSize(0),
        });

    public ValueTask<FunctionSandboxStatus> RestoreAsync(TargetHandle<FunctionSandbox> sandbox, FunctionRestoreRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new FunctionSandboxStatus { Phase = ResourcePhase.Ready, SandboxPhase = FunctionSandboxPhase.Ready, Handle = sandbox });

    public ValueTask ReleaseSnapshotAsync(ResourceRef<FunctionSandboxSnapshot> snapshot, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<ContentArtifactStatus> ResolveAsync(ResourceMetadata<ContentArtifact> metadata, ContentArtifactSpec spec, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ContentArtifactStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ArtifactPhase = ContentArtifactPhase.Available,
            Kind = spec.Kind,
            ResolvedDescriptor = new ArtifactDescriptor(
                spec.Reference.Digest ?? new Digest("sha256", "inmemory"),
                new MediaType(spec.FunctionGuest is not null ? "application/octet-stream" : "application/vnd.oci.image.manifest.v1+json"),
                new ByteSize(0),
                spec.FunctionGuest?.Format ?? ArtifactFormat.ProviderDefined),
            FunctionGuest = spec.Kind == ContentArtifactKind.FunctionGuestBinary
                ? new FunctionGuestBinaryStatus(spec.FunctionGuest?.GuestAbi ?? spec.RequestedGuestAbi, Compatible: true)
                : null,
        });

    public ValueTask<ContentArtifactStatus> EnsureAvailableAsync(ResourceRef<ContentArtifact> artifact, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ContentArtifactStatus { Phase = ResourcePhase.Ready, ArtifactPhase = ContentArtifactPhase.Available, Kind = ContentArtifactKind.ProviderFileArtifact });

    public ValueTask<RootFilesystemViewStatus> MaterializeAsync(ResourceMetadata<RootFilesystemView> metadata, RootFilesystemViewSpec spec, TargetHandle<RuntimeHost>? host, TargetHandle<ExecutionUnit>? unit, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RootFilesystemViewStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            RootfsPhase = RootFilesystemViewPhase.Materialized,
            ResolvedArtifact = spec.Image,
            View = new RealizedRootfsView(RootfsViewKind.ProviderNamespaceHandle, ProviderHandle: new ProviderOpaqueHandle(ProviderId, metadata.Id.Value)),
        });

    public ValueTask<FinalizationResult> FinalizeAsync(TargetHandle<RootFilesystemView> rootfs, FinalizationRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new FinalizationResult { CompletedAt = DateTimeOffset.UtcNow, ManifestDigest = new Digest("sha256", "rootfs-finalized") });

    public ValueTask ReleaseAsync(TargetHandle<RootFilesystemView> rootfs, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<ContentProjectionStatus> ProjectAsync(ResourceMetadata<ContentProjection> metadata, ContentProjectionSpec spec, TargetHandle<RuntimeHost>? host, TargetHandle<ExecutionUnit>? unit, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ContentProjectionStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ProjectionPhase = ContentProjectionPhase.Projected,
            Views =
            [
                new RealizedProjectionView
                {
                    Kind = spec.View.Kind,
                    GuestPath = spec.View.GuestPath,
                    EffectiveAccess = spec.AccessMode,
                    EffectiveRealization = ProjectionRealizationKind.CopyIn,
                    EffectiveWriteEffect = ProjectionWriteEffect.StagedTargetWrite,
                    EffectiveCoherence = CoherenceClass.ManualRefresh,
                    EffectiveCache = CacheBehavior.None,
                },
            ],
        });

    public async ValueTask EnumerateEntriesAsync(ResourceRef<ContentProjection> projection, IContentProjectionEntrySink sink, CancellationToken cancellationToken = default)
    {
        await sink.OnEntryAsync(new ContentProjectionEntry(ContentProjectionEntryKind.Directory, new GuestPath("/"), default, null, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<SyncResult> SyncAsync(TargetHandle<ContentProjection> projection, SyncRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new SyncResult(new SyncCheckpoint(NextSequence(), DateTimeOffset.UtcNow)));

    public ValueTask<FinalizationResult> FinalizeAsync(TargetHandle<ContentProjection> projection, FinalizationRequest request, IExecutionEventSink? events = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new FinalizationResult { CompletedAt = DateTimeOffset.UtcNow, ManifestDigest = new Digest("sha256", "projection-finalized") });

    public ValueTask ReleaseAsync(TargetHandle<ContentProjection> projection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<NetworkStatus> EnsureNetworkAsync(ResourceMetadata<Network> metadata, NetworkSpec spec, NetworkStatus? observed, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new NetworkStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            NetworkPhase = NetworkPhase.Ready,
            RealizedCapabilities = NetworkCapabilitySet.IPv4 | NetworkCapabilitySet.InternalDns | NetworkCapabilitySet.TcpPublish,
            Handle = Handle<Network>(TargetRouteSegmentKind.Network, metadata.Id.Value),
        });

    public ValueTask<NetworkStatus> GetStatusAsync(ResourceRef<Network> network, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new NetworkStatus { Phase = ResourcePhase.Ready, NetworkPhase = NetworkPhase.Ready, RealizedCapabilities = NetworkCapabilitySet.IPv4 });

    public ValueTask DeleteNetworkAsync(ResourceRef<Network> network, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<NetworkMembershipStatus> EnsureMembershipAsync(ResourceMetadata<NetworkMembership> metadata, NetworkMembershipSpec spec, NetworkMembershipStatus? observed, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new NetworkMembershipStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            MembershipPhase = NetworkMembershipPhase.Ready,
            EndpointHandle = new NetworkEndpointHandle(metadata.Id.Value),
            Addresses = [new NetworkAddressAssignment(new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x7f000001), 32, AddressAssignmentKind.ProviderAssigned, IsPrimary: true)],
            RegisteredRecords = spec.ServiceNames.Select(service => new DiscoveryRecord(new DnsName($"{service.Value}.runtime"), DiscoveryRecordKind.Service, new DiscoveryRecordTarget(null, service, new ResourceRef<NetworkMembership>(metadata.Id, metadata.Scope, metadata.Generation), null, NetworkTransport.Tcp, null), TimeSpan.FromSeconds(30), IsDerivedFromMembership: true)).ToArray(),
            Handle = Handle<NetworkMembership>(TargetRouteSegmentKind.Network, metadata.Id.Value),
        });

    public ValueTask<NetworkMembershipStatus> GetMembershipStatusAsync(ResourceRef<NetworkMembership> membership, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new NetworkMembershipStatus { Phase = ResourcePhase.Ready, MembershipPhase = NetworkMembershipPhase.Ready });

    public ValueTask ReleaseMembershipAsync(ResourceRef<NetworkMembership> membership, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<PublishedEndpointStatus> EnsurePublishedEndpointAsync(ResourceMetadata<PublishedEndpoint> metadata, PublishedEndpointSpec spec, PublishedEndpointStatus? observed, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PublishedEndpointStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            EndpointPhase = PublishedEndpointPhase.Bound,
            BoundListener = new BoundEndpoint(spec.Listener.Kind, spec.Listener.Transport, spec.Listener.Address, spec.Listener.Ports, spec.Listener.Socket),
            Route = new EndpointRouteStatus(spec.Target, new NetworkEndpointHandle(metadata.Id.Value), null, spec.Target.Port, spec.Target.SocketPath),
            RouterHandle = Handle<PublishedEndpoint>(TargetRouteSegmentKind.Endpoint, metadata.Id.Value),
        });

    public ValueTask<PublishedEndpointStatus> GetStatusAsync(ResourceRef<PublishedEndpoint> endpoint, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PublishedEndpointStatus { Phase = ResourcePhase.Ready, EndpointPhase = PublishedEndpointPhase.Bound });

    public ValueTask ReleasePublishedEndpointAsync(ResourceRef<PublishedEndpoint> endpoint, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<AuthorityBindingStatus> EnsureAuthorityBindingAsync(ResourceMetadata<AuthorityBinding> metadata, AuthorityBindingSpec spec, AuthorityBindingStatus? observed, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AuthorityBindingStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            BindingPhase = AuthorityBindingPhase.Projected,
            BoundAuthority = new BoundAuthority
            {
                SourceKind = spec.Source.Kind,
                ProjectionKind = spec.Projection.Kind,
                Direction = spec.Policy.Direction,
                EffectiveAuthorityClass = spec.Policy.EffectiveAuthorityClass,
                TargetSocketPath = spec.Projection.TargetSocketPath,
                EnvironmentVariableName = spec.Projection.EnvironmentVariableName,
                HostFunctionName = spec.Source.HostFunction?.Name,
                BoundAt = DateTimeOffset.UtcNow,
                RevocationStatus = RevocationVerificationStatus.Verified,
                AuditCorrelationId = spec.Policy.RequireAudit ? $"audit-{NextSequence()}" : null,
            },
            ProviderHandle = Handle<AuthorityBinding>(TargetRouteSegmentKind.Endpoint, metadata.Id.Value),
        });

    public ValueTask<AuthorityBindingStatus> GetStatusAsync(ResourceRef<AuthorityBinding> binding, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AuthorityBindingStatus { Phase = ResourcePhase.Ready, BindingPhase = AuthorityBindingPhase.Projected });

    public ValueTask RevokeAuthorityBindingAsync(ResourceRef<AuthorityBinding> binding, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<EngineControlPlaneStatus> EnsureEngineControlPlaneAsync(ResourceMetadata<EngineControlPlane> metadata, EngineControlPlaneSpec spec, EngineControlPlaneStatus? observed, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EngineControlPlaneStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            EnginePhase = EngineControlPlanePhase.Ready,
            Endpoints =
            [
                new EngineApiEndpointStatus(
                    spec.Api,
                    new ProviderNamedEndpoint(
                        "engine-api",
                        ProviderEndpointPurpose.EngineApi,
                        new ProviderEndpoint("unix", "guest", Path: "/run/in-memory/engine.sock"),
                        ProviderTransportKind.UnixSocket,
                        EndpointSensitivity.Sensitive),
                    spec.EndpointPolicy ?? new SensitiveEndpointPolicy
                    {
                        Kind = SensitiveEndpointKind.EngineSocket,
                        AuthorityClass = spec.AuthorityMode == EngineAuthorityMode.Rootless
                            ? SensitiveAuthorityClass.RootlessEngineControl
                            : SensitiveAuthorityClass.RootfulEngineControl,
                    }),
            ],
            ProviderHandle = new ProviderOpaqueHandle(ProviderId, metadata.Id.Value),
        });

    public ValueTask<EngineAuthorityBindingPlan> PlanAuthorityBindingAsync(
        EngineControlPlaneStatus engine,
        EngineAuthorityBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EngineApiEndpointStatus? endpoint = engine.Endpoints.FirstOrDefault(candidate => candidate.Api == request.Api);
        if (engine.Phase != ResourcePhase.Ready ||
            engine.EnginePhase != EngineControlPlanePhase.Ready ||
            endpoint?.SensitivePolicy is not { Kind: SensitiveEndpointKind.EngineSocket } policy ||
            endpoint.Endpoint.Sensitivity != EndpointSensitivity.Sensitive ||
            !string.Equals(endpoint.Endpoint.Endpoint.Address, "guest", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(endpoint.Endpoint.Endpoint.Scheme, "unix", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(endpoint.Endpoint.Endpoint.Path))
        {
            return ValueTask.FromResult(new EngineAuthorityBindingPlan
            {
                Accepted = false,
                SourceEngine = request.Engine,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = new DiagnosticCode("hpd.environment.engine-authority.invalid-endpoint"),
                        Message = "Engine authority requires a ready, sensitive, guest-locus Unix socket endpoint.",
                        ProviderId = ProviderId,
                    },
                ],
            });
        }

        return ValueTask.FromResult(new EngineAuthorityBindingPlan
        {
            Accepted = true,
            SourceEngine = request.Engine,
            Spec = new AuthorityBindingSpec
            {
                Kind = AuthorityBindingKind.HostService,
                Source = new AuthorityBindingSource
                {
                    Kind = AuthoritySourceKind.UnixSocket,
                    Locus = BoundaryLocus.RuntimeHost,
                    SocketPath = new UnixSocketPath(endpoint.Endpoint.Endpoint.Path),
                },
                Target = new AuthorityBindingTarget(
                    AuthorityTargetKind.ExecutionUnit,
                    Unit: request.TargetUnit,
                    Locus: BoundaryLocus.ExecutionUnit),
                Projection = new AuthorityBindingProjection
                {
                    Kind = AuthorityProjectionKind.SocketPath,
                    TargetSocketPath = request.TargetSocketPath,
                    ReadOnly = false,
                },
                Policy = new AuthorityBindingPolicy
                {
                    Direction = AuthorityBindingDirection.ProviderToGuest,
                    AuthorityClass = policy.AuthorityClass,
                    EffectiveAuthorityClass = policy.AuthorityClass,
                    Lease = policy.Lease,
                    Redaction = policy.Redaction,
                    RequireAudit = policy.RequireAudit,
                    RequireExplicitUserApproval = policy.RequireExplicitUserApproval,
                    Provenance = request.Provenance,
                },
                AuditLabel = "engine-api:" + request.Api,
            },
        });
    }

    public ValueTask<EngineControlPlaneStatus> GetStatusAsync(ResourceRef<EngineControlPlane> engine, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EngineControlPlaneStatus { Phase = ResourcePhase.Ready, EnginePhase = EngineControlPlanePhase.Ready });

    public ValueTask<EngineControlPlaneStatus> StopAsync(TargetHandle<EngineControlPlane> engine, StopPolicy policy, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EngineControlPlaneStatus { Phase = ResourcePhase.Ready, EnginePhase = EngineControlPlanePhase.Stopped, ProviderHandle = engine.Route.ProviderHandle });

    public ValueTask DeleteAsync(ResourceRef<EngineControlPlane> engine, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    private long NextSequence() => Interlocked.Increment(ref _sequence);

    private ProcessInvocationResult CreateProcessResult(ProcessInvocationSpec? spec, ProcessCompletionKind completionKind, bool outputDrainTimedOut = false)
    {
        byte[] stdout = Encoding.UTF8.GetBytes(spec?.Command.FileName ?? "in-memory");
        return new ProcessInvocationResult
        {
            ProcessId = new ResourceId<ProcessInvocation>($"process-{NextSequence()}"),
            CompletionKind = completionKind,
            ExitCode = completionKind == ProcessCompletionKind.Completed ? 0 : null,
            StartedAt = DateTimeOffset.UtcNow,
            ExitedAt = DateTimeOffset.UtcNow,
            Duration = TimeSpan.Zero,
            Output = new ProcessCapturedOutput
            {
                Stdout = new ProcessStreamOutput
                {
                    CapturedBytes = stdout,
                    BytesObserved = stdout.Length,
                    BytesCaptured = stdout.Length,
                },
                Stderr = new ProcessStreamOutput(),
                OutputDrainTimedOut = outputDrainTimedOut,
                OutputDrainTimeout = spec?.Policy.OutputDrainTimeout ?? ProcessInvocationPolicy.Default.OutputDrainTimeout,
            },
        };
    }

    private TargetHandle<TTarget> Handle<TTarget>(TargetRouteSegmentKind segmentKind, string id)
        where TTarget : IOperationTargetMarker =>
        new(
            new TargetRoute
            {
                Kind = new TargetKind(typeof(TTarget).Name),
                Scope = new ResourceScope("in-memory-runtime"),
                Segments = [new TargetRouteSegment(segmentKind, id)],
                ProviderId = ProviderId,
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Invoke,
            ProviderGeneration: (ulong)Math.Max(0, _sequence));
}

internal sealed class InMemoryProcessInvocationHandle(
    ProcessInvocationSpec spec,
    ProcessInvocationResult result,
    Func<long> nextSequence) : IProcessInvocationHandle
{
    private readonly List<ProcessOutputChunk> _output =
    [
        new ProcessOutputChunk(
            new TargetHandle<ProcessInvocation>(
                new TargetRoute
                {
                    Kind = new TargetKind(nameof(ProcessInvocation)),
                    Scope = new ResourceScope("in-memory-runtime"),
                    Segments = [new TargetRouteSegment(TargetRouteSegmentKind.ProcessInvocation, result.ProcessId?.Value ?? "process")],
                },
                TargetHandleLifetime.LiveCapability,
                TargetHandleAuthority.Observe | TargetHandleAuthority.Control),
            ProcessOutputStream.Stdout,
            nextSequence(),
            DateTimeOffset.UtcNow,
            result.Output.Stdout.CapturedBytes,
            ProcessOutputChunkFlags.Final),
    ];

    public TargetHandle<ProcessInvocation> Handle => _output[0].Process;
    public ResourceRef<ProcessInvocation>? Resource => result.ProcessId is { } id ? new ResourceRef<ProcessInvocation>(id, new ResourceScope("in-memory-runtime")) : null;
    public ProcessInvocationSpec Spec => spec;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask CloseStdinAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask<ProcessInvocationResult> WaitAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(result);

    public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (ProcessOutputChunk chunk in _output)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
        }
    }
}
