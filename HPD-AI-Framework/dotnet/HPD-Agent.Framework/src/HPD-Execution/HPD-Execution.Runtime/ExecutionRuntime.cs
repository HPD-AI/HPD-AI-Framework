#nullable enable

namespace HPD.Execution.Runtime;

using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using HPD.Execution.Contracts;

public sealed class ExecutionProviderRegistry :
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
    public IReadOnlyList<IProcessIsolationProvider> ProcessIsolationProviders => _processIsolationProviders;
    public IReadOnlyList<IFunctionSandboxProvider> FunctionSandboxProviders => _functionSandboxProviders;
    public IReadOnlyList<IFunctionSnapshotProvider> FunctionSnapshotProviders => _functionSnapshotProviders;
    public IReadOnlyList<IArtifactProvider> ArtifactProviders => _artifactProviders;
    public IReadOnlyList<IRootFilesystemProvider> RootFilesystemProviders => _rootFilesystemProviders;
    public IReadOnlyList<IExecutionWorkspaceStore> ExecutionWorkspaceStores => _executionWorkspaceStores;
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
    private readonly List<IProcessIsolationProvider> _processIsolationProviders = [];
    private readonly List<IFunctionSandboxProvider> _functionSandboxProviders = [];
    private readonly List<IFunctionSnapshotProvider> _functionSnapshotProviders = [];
    private readonly List<IArtifactProvider> _artifactProviders = [];
    private readonly List<IRootFilesystemProvider> _rootFilesystemProviders = [];
    private readonly List<IExecutionWorkspaceStore> _executionWorkspaceStores = [];
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
    public void AddProcessIsolationProvider(IProcessIsolationProvider provider) => _processIsolationProviders.Add(provider);
    public void AddFunctionSandboxProvider(IFunctionSandboxProvider provider) => _functionSandboxProviders.Add(provider);
    public void AddFunctionSnapshotProvider(IFunctionSnapshotProvider provider) => _functionSnapshotProviders.Add(provider);
    public void AddArtifactProvider(IArtifactProvider provider) => _artifactProviders.Add(provider);
    public void AddRootFilesystemProvider(IRootFilesystemProvider provider) => _rootFilesystemProviders.Add(provider);
    public void AddExecutionWorkspaceStore(IExecutionWorkspaceStore provider) => _executionWorkspaceStores.Add(provider);
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
                Id = new CapabilityId($"hpd.execution.contract.{ToCapabilityName(kind)}"),
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
                    HostPlatform: ExecutionProviderRegistry.DefaultPlatform(),
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

        PlatformSpec platform = request.RequestedPlatform ?? ExecutionProviderRegistry.DefaultPlatform();
        return new RuntimePlan
        {
            Id = new RuntimePlanId($"plan-{Guid.NewGuid():N}"),
            TopologyPolicy = request.TopologyPolicy,
            Compatibility = new PlatformCompatibilityPlan
            {
                RequestedPlatform = platform,
                HostPlatform = capabilityReport?.HostPlatform ?? ExecutionProviderRegistry.DefaultPlatform(),
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

public sealed class InMemoryExecutionRuntime(ExecutionProviderRegistry registry, IRuntimePlanner? planner = null) : IExecutionRuntime
{
    private readonly IRuntimePlanner _planner = planner ?? new DefaultRuntimePlanner(registry, registry);
    private long _generation;

    public ValueTask<RuntimePlan> PlanAsync(RuntimePlanRequest request, CancellationToken cancellationToken = default) =>
        _planner.PlanAsync(request, cancellationToken);

    public ValueTask<RuntimePlanValidationResult> ValidateAsync(RuntimePlan plan, CancellationToken cancellationToken = default) =>
        _planner.ValidateAsync(plan, cancellationToken);

    public async ValueTask<ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>> EnsureHostAsync(RuntimeHostSpec spec, CancellationToken cancellationToken = default)
    {
        ResourceMetadata<RuntimeHost> metadata = Metadata<RuntimeHost>("runtime-host");
        RuntimeHostStatus status = await Require(registry.RuntimeHostProviders, "runtime host").EnsureAsync(metadata, spec, observed: null, cancellationToken).ConfigureAwait(false);
        return new ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>(metadata, spec, status);
    }

    public async ValueTask<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>> EnsureExecutionUnitAsync(ExecutionUnitSpec spec, CancellationToken cancellationToken = default)
    {
        ResourceMetadata<ExecutionUnit> metadata = Metadata<ExecutionUnit>("execution-unit") with { Lifetime = ResourceLifetime.ExecutionUnit };
        ExecutionUnitStatus status = await Require(registry.ExecutionUnitProviders, "execution unit").EnsureAsync(metadata, spec, observed: null, cancellationToken).ConfigureAwait(false);
        return new ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>(metadata, spec, status);
    }

    public ValueTask<IProcessInvocationHandle> StartProcessAsync(ProcessInvocationSpec spec, CancellationToken cancellationToken = default) =>
        StartPreparedProcessAsync(spec, cancellationToken);

    public ValueTask<ProcessInvocationResult> RunProcessAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default) =>
        RunPreparedProcessAsync(spec, output, cancellationToken);

    private async ValueTask<IProcessInvocationHandle> StartPreparedProcessAsync(ProcessInvocationSpec spec, CancellationToken cancellationToken)
    {
        ProcessInvocationSpec prepared = await PrepareProcessIsolationAsync(spec, cancellationToken).ConfigureAwait(false);
        return await Require(registry.ProcessProviders, "process").StartAsync(prepared, output: null, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ProcessInvocationResult> RunPreparedProcessAsync(ProcessInvocationSpec spec, IProcessOutputSink? output, CancellationToken cancellationToken)
    {
        ProcessInvocationSpec prepared = await PrepareProcessIsolationAsync(spec, cancellationToken).ConfigureAwait(false);
        return await Require(registry.ProcessProviders, "process").RunAsync(prepared, output, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ProcessInvocationSpec> PrepareProcessIsolationAsync(ProcessInvocationSpec spec, CancellationToken cancellationToken)
    {
        if (spec.Isolation.Mode == ProcessIsolationMode.Disabled)
        {
            return spec;
        }

        IProcessIsolationProvider? provider = registry.ProcessIsolationProviders.FirstOrDefault();
        if (provider is null)
        {
            if (spec.Isolation.Mode == ProcessIsolationMode.Isolated)
            {
                throw new InvalidOperationException("Process isolation was required, but no process isolation provider is registered.");
            }

            return spec;
        }

        ProcessIsolationPlan plan = await provider.PlanIsolationAsync(spec, spec.Isolation, cancellationToken).ConfigureAwait(false);
        IsolatedProcessCommand prepared = await provider.PrepareAsync(spec, spec.Isolation, plan, cancellationToken).ConfigureAwait(false);
        return prepared.Invocation;
    }

    public async ValueTask<ResourceSnapshot<FunctionSandbox, FunctionSandboxSpec, FunctionSandboxStatus>> EnsureFunctionSandboxAsync(FunctionSandboxSpec spec, CancellationToken cancellationToken = default)
    {
        ResourceMetadata<FunctionSandbox> metadata = Metadata<FunctionSandbox>("function-sandbox");
        FunctionSandboxStatus status = await Require(registry.FunctionSandboxProviders, "function sandbox").EnsureAsync(metadata, spec, observed: null, cancellationToken).ConfigureAwait(false);
        return new ResourceSnapshot<FunctionSandbox, FunctionSandboxSpec, FunctionSandboxStatus>(metadata, spec, status);
    }

    public ValueTask<FunctionInvocationResult> InvokeFunctionAsync(FunctionInvocationSpec spec, IFunctionObservationSink? observations = null, CancellationToken cancellationToken = default) =>
        Require(registry.FunctionSandboxProviders, "function sandbox").InvokeAsync(spec, observations, cancellationToken);

    public async ValueTask<RuntimeFinalizationResult> FinalizeRuntimeAsync(RuntimeFinalizationRequest request, CancellationToken cancellationToken = default)
    {
        var contentProjections = new List<FinalizationResult>();
        var retainedResources = new List<UntypedResourceRef>();
        var conflicts = new List<WorkspaceConflict>();
        var diagnostics = new List<Diagnostic>();

        foreach (IContentProjectionProvider provider in registry.ContentProjectionProviders)
        {
            if (provider is not IRuntimeFinalizationParticipant participant)
            {
                continue;
            }

            RuntimeFinalizationResult participantResult =
                await participant.FinalizeRuntimeAsync(request, cancellationToken).ConfigureAwait(false);
            contentProjections.AddRange(participantResult.ContentProjections);
            retainedResources.AddRange(participantResult.RetainedResources);
            conflicts.AddRange(participantResult.Conflicts);
            diagnostics.AddRange(participantResult.Diagnostics);
        }

        foreach (IAuthorityBindingProvider provider in registry.AuthorityBindingProviders)
        {
            if (provider is not IRuntimeFinalizationParticipant participant)
            {
                continue;
            }

            RuntimeFinalizationResult participantResult =
                await participant.FinalizeRuntimeAsync(request, cancellationToken).ConfigureAwait(false);
            retainedResources.AddRange(participantResult.RetainedResources);
            diagnostics.AddRange(participantResult.Diagnostics);
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

    private static TProvider Require<TProvider>(IReadOnlyList<TProvider> providers, string family)
        where TProvider : class =>
        providers.FirstOrDefault() ?? throw new InvalidOperationException($"No {family} provider is registered.");
}

public interface IRuntimeFinalizationParticipant
{
    ValueTask<RuntimeFinalizationResult> FinalizeRuntimeAsync(
        RuntimeFinalizationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryExecutionProviderModule : IProviderModule
{
    private readonly InMemoryExecutionProvider _provider;

    public InMemoryExecutionProviderModule()
        : this(new InMemoryExecutionProvider())
    {
    }

    public InMemoryExecutionProviderModule(InMemoryExecutionProvider provider)
    {
        _provider = provider;
    }

    public ProviderDescriptor Descriptor { get; } = new()
    {
        Id = InMemoryExecutionProvider.InMemoryProviderId,
        DisplayName = "HPD In-Memory Execution Provider",
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
            ProviderContractKind.ProcessIsolation |
            ProviderContractKind.EngineControlPlane,
        TrustLevel = ProviderTrustLevel.BuiltIn,
        DefaultActivationScope = ProviderActivationScope.Runtime,
        ActivationModels =
        [
            new ProviderActivationModel(ProviderActivationKind.InProcess, ProviderActivationScope.Runtime, ProviderTransportKind.None),
        ],
        HostPlatforms = [ExecutionProviderRegistry.DefaultPlatform()],
    };

    public void Register(IProviderRegistrationBuilder builder)
    {
        builder.AddRuntimeHostProvider(_provider);
        builder.AddRuntimeHostResetProvider(_provider);
        builder.AddExecutionUnitProvider(_provider);
        builder.AddProcessProvider(_provider);
        builder.AddProcessIsolationProvider(_provider);
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

public sealed class InMemoryExecutionProvider :
    IRuntimeHostProvider,
    IRuntimeHostResetProvider,
    IExecutionUnitProvider,
    IProcessProvider,
    IProcessIsolationProvider,
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

    public ValueTask<ExecutionUnitStatus> StopAsync(TargetHandle<ExecutionUnit> unit, StopPolicy policy, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ExecutionUnitStatus { Phase = ResourcePhase.Ready, UnitPhase = ExecutionUnitPhase.Stopped, Handle = unit });

    public ValueTask DeleteAsync(ResourceRef<ExecutionUnit> unit, CancellationToken cancellationToken = default)
    {
        _resources.TryRemove(unit.Id.Value, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ExecutionUnitStatus> GetStatusAsync(TargetHandle<ExecutionUnit> unit, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ExecutionUnitStatus { Phase = ResourcePhase.Ready, UnitPhase = ExecutionUnitPhase.Ready, Handle = unit });

    public ValueTask<IProcessInvocationHandle> StartAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default)
    {
        var handle = new InMemoryProcessInvocationHandle(spec, CreateProcessResult(spec, ProcessCompletionKind.Completed), NextSequence);
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

    public ValueTask<ProcessIsolationPlan> PlanIsolationAsync(ProcessInvocationSpec invocation, ProcessIsolationPolicy policy, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new ProcessIsolationPlan
        {
            Diagnostics = ["in-memory process isolation is a no-op preparation step"],
        });
    }

    public ValueTask<IsolatedProcessCommand> PrepareAsync(ProcessInvocationSpec invocation, ProcessIsolationPolicy policy, ProcessIsolationPlan? plan = null, CancellationToken cancellationToken = default)
    {
        ProviderExtensionData marker = new(
            ProviderId,
            new SchemaId("hpd.execution.process-isolation.in-memory"),
            new ContentType("text/plain"),
            Encoding.UTF8.GetBytes("prepared"));

        ProcessInvocationSpec prepared = invocation with
        {
            ProviderExtensions = invocation.ProviderExtensions.Concat([marker]).ToArray(),
        };

        return ValueTask.FromResult(new IsolatedProcessCommand
        {
            Invocation = prepared,
            Plan = plan ?? ProcessIsolationPlan.Empty,
            ProviderExtensions = [marker],
        });
    }

    public ValueTask SignalAsync(TargetHandle<ProcessInvocation> process, ProcessSignal signal, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask ResizeTerminalAsync(TargetHandle<ProcessInvocation> process, TerminalSpec size, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<ProcessInvocationResult> WaitAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(CreateProcessResult(null, ProcessCompletionKind.Completed));

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
            ProviderHandle = new ProviderOpaqueHandle(ProviderId, metadata.Id.Value),
        });

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
