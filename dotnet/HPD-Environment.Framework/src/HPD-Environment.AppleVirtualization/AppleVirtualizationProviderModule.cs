namespace HPD.Environment.AppleVirtualization;

using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Environment.AppleVirtualization.Activation;
using HPD.Environment.AppleVirtualization.Authority;
using HPD.Environment.AppleVirtualization.Engines;
using HPD.Environment.AppleVirtualization.ExecutionUnits;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Hosts;
using HPD.Environment.AppleVirtualization.Networks;
using HPD.Environment.AppleVirtualization.Processes;
using HPD.Environment.AppleVirtualization.Projections;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

public sealed class AppleVirtualizationProviderModule : IProviderModule
{
    private readonly AppleVirtualizationProviderOptions _options;
    private readonly IAppleVirtualizationHelperClient _helperClient;
    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly IProviderCapabilityReporter _capabilityReporter;
    private readonly PlatformSpec? _hostPlatformOverride;
    private readonly IProviderActivator? _activator;

    public AppleVirtualizationProviderModule()
        : this(new AppleVirtualizationProviderOptions())
    {
    }

    public AppleVirtualizationProviderModule(AppleVirtualizationProviderOptions options)
        : this(options, helperClient: null, ledger: null, capabilityReporter: null, hostPlatformOverride: null)
    {
    }

    internal AppleVirtualizationProviderModule(IProviderCapabilityReporter capabilityReporter)
        : this(new AppleVirtualizationProviderOptions(), helperClient: null, ledger: null, capabilityReporter, hostPlatformOverride: null)
    {
    }

    internal AppleVirtualizationProviderModule(
        AppleVirtualizationProviderOptions options,
        IAppleVirtualizationHelperClient? helperClient,
        AppleVirtualizationProviderStateLedger? ledger,
        IProviderCapabilityReporter? capabilityReporter = null,
        PlatformSpec? hostPlatformOverride = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ledger = ledger ?? new AppleVirtualizationProviderStateLedger();
        if (helperClient is null &&
            _options.FeatureGates.EnableRealHelperActivation &&
            _options.HelperTransportMode == AppleVirtualizationHelperTransportMode.StdIo)
        {
            var activator = new AppleVirtualizationHelperActivator(_options, _ledger);
            _helperClient = activator;
            _activator = activator;
        }
        else
        {
            _helperClient = helperClient ?? CreateDefaultHelperClient(_options);
        }

        _capabilityReporter = capabilityReporter ?? new AppleVirtualizationCapabilityReporter(_options);
        _hostPlatformOverride = hostPlatformOverride;
    }

    public ProviderDescriptor Descriptor => AppleVirtualizationProviderDescriptor.Create(_options);

    public void Register(IProviderRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProviderCapabilityReporter(_capabilityReporter);
        if (_activator is not null)
        {
            builder.AddProviderActivator(_activator);
        }

        var projectionProvider = new AppleVirtualizationContentProjectionProvider(_helperClient, _ledger);
        var networkProvider = new AppleVirtualizationNetworkProvider(_ledger, _helperClient);
        var serviceDiscoveryProvider = new AppleVirtualizationServiceDiscoveryProvider(_ledger);
        var endpointProvider = new AppleVirtualizationEndpointPublicationProvider(_ledger, _helperClient);
        var authorityProvider = new AppleVirtualizationAuthorityBindingProvider(_ledger, _helperClient);
        builder.AddRuntimeHostProvider(new AppleVirtualizationRuntimeHostProvider(
            _helperClient,
            _ledger,
            _hostPlatformOverride ?? AppleVirtualizationProviderDescriptor.CurrentPlatform(),
            _options));
        builder.AddExecutionUnitProvider(new AppleVirtualizationExecutionUnitProvider(_ledger, _helperClient, projectionProvider, authorityProvider));
        builder.AddContentProjectionProvider(projectionProvider);
        builder.AddProcessProvider(new AppleVirtualizationProcessProvider(_ledger, _helperClient));
        builder.AddNetworkProvider(networkProvider);
        builder.AddNetworkMembershipProvider(networkProvider);
        builder.AddServiceDiscoveryProvider(serviceDiscoveryProvider);
        builder.AddEndpointPublicationProvider(endpointProvider);
        builder.AddAuthorityBindingProvider(authorityProvider);
        if (_options.FeatureGates.EnableEngineControlPlane)
        {
            builder.AddEngineControlPlaneProvider(new AppleVirtualizationEngineControlPlaneProvider(_ledger, _helperClient, _options));
        }
    }

    public void RegisterJsonTypes(IProviderJsonTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Add(AppleVirtualizationJsonContext.Default.ProviderDescriptor, "hpd.execution.apple-virtualization.provider-descriptor");
        registry.Add(AppleVirtualizationJsonContext.Default.ProviderCapabilityReport, "hpd.execution.apple-virtualization.capability-report");
        registry.Add(AppleVirtualizationJsonContext.Default.ProviderActivationSpec, "hpd.execution.apple-virtualization.activation-spec");
        registry.Add(AppleVirtualizationJsonContext.Default.ProviderActivationStatus, "hpd.execution.apple-virtualization.activation-status");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProviderOptions, "hpd.execution.apple-virtualization.options.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestImageOptions, "hpd.execution.apple-virtualization.guest-image.options.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestSharedDirectoryOptions, "hpd.execution.apple-virtualization.guest-image.shared-directory.options.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineBootstrapOptions, "hpd.execution.apple-virtualization.engine-bootstrap.options.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineProvisioningOptions, "hpd.execution.apple-virtualization.engine-provisioning.options.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProviderFeatureGates, "hpd.execution.apple-virtualization.feature-gates.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope, "hpd.execution.apple-virtualization.helper.envelope.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperError, "hpd.execution.apple-virtualization.helper.error.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperHelloRequest, "hpd.execution.apple-virtualization.helper.hello.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperHelloResponse, "hpd.execution.apple-virtualization.helper.hello.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationPreflightFact, "hpd.execution.apple-virtualization.helper.preflight.fact.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationCapabilitiesGetRequest, "hpd.execution.apple-virtualization.helper.capabilities.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationCapabilitiesGetResponse, "hpd.execution.apple-virtualization.helper.capabilities.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationPreflightRunRequest, "hpd.execution.apple-virtualization.helper.preflight.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationPreflightRunResponse, "hpd.execution.apple-virtualization.helper.preflight.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationActivationStatusResponse, "hpd.execution.apple-virtualization.helper.activation-status.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHealthProbeRequest, "hpd.execution.apple-virtualization.helper.health.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHealthProbeResponse, "hpd.execution.apple-virtualization.helper.health.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationShutdownRequest, "hpd.execution.apple-virtualization.helper.shutdown.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationShutdownResponse, "hpd.execution.apple-virtualization.helper.shutdown.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHostEnsureRequest, "hpd.execution.apple-virtualization.helper.host.ensure.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHostLifecycleRequest, "hpd.execution.apple-virtualization.helper.host.lifecycle.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHostStatusResponse, "hpd.execution.apple-virtualization.helper.host.status.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestControlWaitReadyRequest, "hpd.execution.apple-virtualization.helper.guest-control.wait-ready.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestControlStatusResponse, "hpd.execution.apple-virtualization.helper.guest-control.status.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentTransportProbeRequest, "hpd.execution.apple-virtualization.helper.guest-agent.transport.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentTransportProbeResponse, "hpd.execution.apple-virtualization.helper.guest-agent.transport.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentTransportEndpoint, "hpd.execution.apple-virtualization.helper.guest-agent.transport.endpoint.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentReadinessProbeRequest, "hpd.execution.apple-virtualization.helper.guest-agent.readiness.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentReadinessProbeResponse, "hpd.execution.apple-virtualization.helper.guest-agent.readiness.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionConfigureRequest, "hpd.execution.apple-virtualization.helper.projection.configure.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionMountRequest, "hpd.execution.apple-virtualization.helper.projection.mount.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionStatusRequest, "hpd.execution.apple-virtualization.helper.projection.status.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionUnmountRequest, "hpd.execution.apple-virtualization.helper.projection.unmount.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionObserveRequest, "hpd.execution.apple-virtualization.helper.projection.observe.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionSyncRequest, "hpd.execution.apple-virtualization.helper.projection.sync.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionFinalizationRequest, "hpd.execution.apple-virtualization.helper.projection.finalization.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionChangeEnumerationRequest, "hpd.execution.apple-virtualization.helper.projection.change-enumeration.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionPromotionRequest, "hpd.execution.apple-virtualization.helper.projection.promotion.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperProjectionScriptedGuestState, "hpd.execution.apple-virtualization.helper.projection.scripted-guest-state.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionLifecycleRequest, "hpd.execution.apple-virtualization.helper.projection.lifecycle.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProjectionStatusResponse, "hpd.execution.apple-virtualization.helper.projection.status.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationNetworkStatusRequest, "hpd.execution.apple-virtualization.helper.network.status.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationNetworkStatusResponse, "hpd.execution.apple-virtualization.helper.network.status.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationNetworkAttachmentCapabilityFact, "hpd.execution.apple-virtualization.helper.network.attachment-capability.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEndpointPublicationRequest, "hpd.execution.apple-virtualization.helper.endpoint.publication.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEndpointPublicationResponse, "hpd.execution.apple-virtualization.helper.endpoint.publication.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationAuthoritySourceDescriptor, "hpd.execution.apple-virtualization.helper.authority.source.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationAuthorityTargetDescriptor, "hpd.execution.apple-virtualization.helper.authority.target.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationAuthorityProjectionDescriptor, "hpd.execution.apple-virtualization.helper.authority.projection.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationAuthorityLeaseDescriptor, "hpd.execution.apple-virtualization.helper.authority.lease.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationAuthorityBindingRequest, "hpd.execution.apple-virtualization.helper.authority.binding.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationAuthorityBindingResponse, "hpd.execution.apple-virtualization.helper.authority.binding.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineStatusRequest, "hpd.execution.apple-virtualization.helper.engine.status.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineStatusResponse, "hpd.execution.apple-virtualization.helper.engine.status.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineProvisioningRequest, "hpd.execution.apple-virtualization.helper.engine.provisioning.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineProvisioningResponse, "hpd.execution.apple-virtualization.helper.engine.provisioning.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineProvisioningPlanStep, "hpd.execution.apple-virtualization.helper.engine.provisioning.plan-step.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineProvisioningPrerequisiteStatus, "hpd.execution.apple-virtualization.helper.engine.provisioning.prerequisites.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineProvisioningOutputCapture, "hpd.execution.apple-virtualization.helper.engine.provisioning.output.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationEngineProvisioningEvidence, "hpd.execution.apple-virtualization.helper.engine.provisioning.evidence.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationUnitEnsureRequest, "hpd.execution.apple-virtualization.helper.unit.ensure.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationUnitLifecycleRequest, "hpd.execution.apple-virtualization.helper.unit.lifecycle.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationUnitStatusResponse, "hpd.execution.apple-virtualization.helper.unit.status.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProcessStartRequest, "hpd.execution.apple-virtualization.helper.process.start.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProcessStdinRequest, "hpd.execution.apple-virtualization.helper.process.stdin.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProcessSignalRequest, "hpd.execution.apple-virtualization.helper.process.signal.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProcessStopRequest, "hpd.execution.apple-virtualization.helper.process.stop.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProcessResizeRequest, "hpd.execution.apple-virtualization.helper.process.resize.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProcessLifecycleRequest, "hpd.execution.apple-virtualization.helper.process.lifecycle.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProcessStatusResponse, "hpd.execution.apple-virtualization.helper.process.status.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationProcessOutputEvent, "hpd.execution.apple-virtualization.helper.process.output-event.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationVmConfigurationValidationRequest, "hpd.execution.apple-virtualization.helper.vm-configuration.validation.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationVmConfigurationValidationResponse, "hpd.execution.apple-virtualization.helper.vm-configuration.validation.response.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationVmConfigurationSharedDirectory, "hpd.execution.apple-virtualization.helper.vm-configuration.shared-directory.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEnvelope, "hpd.execution.apple-virtualization.guest-agent.envelope.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentError, "hpd.execution.apple-virtualization.guest-agent.error.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentHello, "hpd.execution.apple-virtualization.guest-agent.hello.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentReady, "hpd.execution.apple-virtualization.guest-agent.ready.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionIdentity, "hpd.execution.apple-virtualization.guest-agent.projection.identity.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionHostSourceIdentity, "hpd.execution.apple-virtualization.guest-agent.projection.host-source.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionGuestPathExpectation, "hpd.execution.apple-virtualization.guest-agent.projection.guest-path-expectation.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionMountRequest, "hpd.execution.apple-virtualization.guest-agent.projection.mount.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionMountResult, "hpd.execution.apple-virtualization.guest-agent.projection.mount.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionStatusRequest, "hpd.execution.apple-virtualization.guest-agent.projection.status.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionStatus, "hpd.execution.apple-virtualization.guest-agent.projection.status.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionUnmountRequest, "hpd.execution.apple-virtualization.guest-agent.projection.unmount.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionUnmountResult, "hpd.execution.apple-virtualization.guest-agent.projection.unmount.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionObserveRequest, "hpd.execution.apple-virtualization.guest-agent.projection.observe.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionObserveResult, "hpd.execution.apple-virtualization.guest-agent.projection.observe.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionSyncRequest, "hpd.execution.apple-virtualization.guest-agent.projection.sync.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionSyncResult, "hpd.execution.apple-virtualization.guest-agent.projection.sync.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionFinalizationRequest, "hpd.execution.apple-virtualization.guest-agent.projection.finalization.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionFinalizationResult, "hpd.execution.apple-virtualization.guest-agent.projection.finalization.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionChangeEnumerationRequest, "hpd.execution.apple-virtualization.guest-agent.projection.change-enumeration.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionChangeEnumerationResult, "hpd.execution.apple-virtualization.guest-agent.projection.change-enumeration.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionPromotionRequest, "hpd.execution.apple-virtualization.guest-agent.projection.promotion.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionPromotionResult, "hpd.execution.apple-virtualization.guest-agent.projection.promotion.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProjectionChange, "hpd.execution.apple-virtualization.guest-agent.projection.change.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentNetworkStatusRequest, "hpd.execution.apple-virtualization.guest-agent.network.status.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentNetworkStatus, "hpd.execution.apple-virtualization.guest-agent.network.status.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentNetworkInterfaceStatus, "hpd.execution.apple-virtualization.guest-agent.network.interface-status.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentNetworkRouteObservation, "hpd.execution.apple-virtualization.guest-agent.network.route-observation.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentNetworkListenerObservation, "hpd.execution.apple-virtualization.guest-agent.network.listener-observation.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentAuthoritySource, "hpd.execution.apple-virtualization.guest-agent.authority.source.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentAuthorityTarget, "hpd.execution.apple-virtualization.guest-agent.authority.target.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentAuthorityProjection, "hpd.execution.apple-virtualization.guest-agent.authority.projection.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentAuthorityLease, "hpd.execution.apple-virtualization.guest-agent.authority.lease.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentAuthorityProjectionRequest, "hpd.execution.apple-virtualization.guest-agent.authority.projection.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentAuthorityStatusRequest, "hpd.execution.apple-virtualization.guest-agent.authority.status.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentAuthorityRevocationRequest, "hpd.execution.apple-virtualization.guest-agent.authority.revocation.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentBoundAuthority, "hpd.execution.apple-virtualization.guest-agent.authority.bound.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentAuthorityStatus, "hpd.execution.apple-virtualization.guest-agent.authority.status.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentAuthorityRevocationResult, "hpd.execution.apple-virtualization.guest-agent.authority.revocation.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEngineStatusRequest, "hpd.execution.apple-virtualization.guest-agent.engine.status.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEngineStatus, "hpd.execution.apple-virtualization.guest-agent.engine.status.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEngineApiEndpoint, "hpd.execution.apple-virtualization.guest-agent.engine.endpoint.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEngineProvisioningRequest, "hpd.execution.apple-virtualization.guest-agent.engine.provisioning.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEngineProvisioningResult, "hpd.execution.apple-virtualization.guest-agent.engine.provisioning.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentContainerObservation, "hpd.execution.apple-virtualization.guest-agent.engine.container-observation.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProcessStartRequest, "hpd.execution.apple-virtualization.guest-agent.process.start.request.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProcessOutputChunk, "hpd.execution.apple-virtualization.guest-agent.process.output.chunk.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProcessResult, "hpd.execution.apple-virtualization.guest-agent.process.result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProcessStatus, "hpd.execution.apple-virtualization.guest-agent.process.status.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProcessOutputReadResult, "hpd.execution.apple-virtualization.guest-agent.process.output.read-result.v1");
        registry.Add(AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentProcessControlResult, "hpd.execution.apple-virtualization.guest-agent.process.control.result.v1");
        registry.Add(AppleVirtualizationExecutionUnitJsonContext.Default.AppleVirtualizationExecutionUnitContextExtension, "hpd.execution.apple-virtualization.execution-unit.context.v1");
    }

    private static IAppleVirtualizationHelperClient CreateDefaultHelperClient(AppleVirtualizationProviderOptions options) =>
        options.FeatureGates.EnableInMemoryFakeHelper || options.HelperTransportMode == AppleVirtualizationHelperTransportMode.InMemoryFake
            ? new FakeAppleVirtualizationHelperClient()
            : new AppleVirtualizationUnavailableHelperClient();
}

public static class AppleVirtualizationRegistrationExtensions
{
    public static EnvironmentProviderRegistry RegisterAppleVirtualizationProvider(this EnvironmentProviderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.RegisterModule(new AppleVirtualizationProviderModule());
        return registry;
    }
}

public static class AppleVirtualizationProviderDescriptor
{
    public static readonly ProviderId ProviderId = new("hpd.execution.apple-virtualization");
    public static readonly string HelperExecutableName = "hpd-vz";

    public const string CapabilityPrefix = "hpd.execution.apple-virtualization";

    public static readonly CapabilityId RuntimeHostBootCapability = new($"{CapabilityPrefix}.runtime-host.boot");
    public static readonly CapabilityId HelperPreflightCapability = new($"{CapabilityPrefix}.helper.preflight");
    public static readonly CapabilityId ExecutionUnitCapability = new($"{CapabilityPrefix}.execution-unit");
    public static readonly CapabilityId ProcessInvocationCapability = new($"{CapabilityPrefix}.process-invocation");
    public static readonly CapabilityId ContentProjectionCapability = new($"{CapabilityPrefix}.content-projection");
    public static readonly CapabilityId NetworkCapability = new($"{CapabilityPrefix}.network");
    public static readonly CapabilityId ServiceDiscoveryCapability = new($"{CapabilityPrefix}.service-discovery");
    public static readonly CapabilityId EndpointPublicationCapability = new($"{CapabilityPrefix}.endpoint-publication");
    public static readonly CapabilityId AuthorityBindingCapability = new($"{CapabilityPrefix}.authority-binding");
    public static readonly CapabilityId EngineControlPlaneCapability = new($"{CapabilityPrefix}.engine-control-plane");
    public static readonly CapabilityId ArtifactCapability = new($"{CapabilityPrefix}.artifact");
    public static readonly CapabilityId RootFilesystemCapability = new($"{CapabilityPrefix}.root-filesystem-view");
    public static readonly CapabilityId BlockVolumeCapability = new($"{CapabilityPrefix}.block-volume");
    public static readonly CapabilityId FunctionLaneCapability = new($"{CapabilityPrefix}.function-lanes");

    public static ProviderContractKind FirstSliceContracts =>
        ProviderContractKind.RuntimeHost |
        ProviderContractKind.ExecutionUnit |
        ProviderContractKind.ProcessInvocation |
        ProviderContractKind.ContentProjection |
        ProviderContractKind.Network |
        ProviderContractKind.NetworkMembership |
        ProviderContractKind.ServiceDiscovery |
        ProviderContractKind.EndpointPublication |
        ProviderContractKind.AuthorityBinding;

    public static ProviderDescriptor Create() =>
        Create(new AppleVirtualizationProviderOptions());

    public static ProviderDescriptor Create(AppleVirtualizationProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ProviderContractKind contractKinds = options.FeatureGates.EnableEngineControlPlane
            ? FirstSliceContracts | ProviderContractKind.EngineControlPlane
            : FirstSliceContracts;

        return
        new()
        {
            Id = ProviderId,
            DisplayName = "HPD Apple Virtualization Provider",
            ContractVersion = new SemanticVersion(1, 0, 0),
            ProviderVersion = new SemanticVersion(0, 1, 0, "preview"),
            ContractKinds = contractKinds,
            TrustLevel = ProviderTrustLevel.BuiltIn,
            DefaultActivationScope = ProviderActivationScope.Runtime,
            SupportedActivationScopes = [ProviderActivationScope.Runtime],
            ActivationModels =
            [
                new ProviderActivationModel(
                    ProviderActivationKind.SupervisedExecutable,
                    ProviderActivationScope.Runtime,
                    ProviderTransportKind.StdIo,
                    RequiresSupervision: true),
            ],
            HostPlatforms =
            [
                new PlatformSpec("macos", "arm64"),
                new PlatformSpec("macos", "x64"),
            ],
            GuestPlatforms =
            [
                new PlatformSpec("linux", "arm64"),
                new PlatformSpec("linux", "x64"),
            ],
            Discovery =
            [
                new ProviderDiscoveryDescriptor(ProviderDiscoveryKind.ExecutablePath, HelperExecutableName),
                new ProviderDiscoveryDescriptor(ProviderDiscoveryKind.WellKnownPath, "/usr/local/bin/hpd-vz"),
            ],
            HostDependencies =
            [
                new HostDependencyRequirement(new HostDependencyRef(HostDependencyKind.Executable, HelperExecutableName), Required: true, Detail: "Swift helper that owns Virtualization.framework objects."),
                new HostDependencyRequirement(new HostDependencyRef(HostDependencyKind.Permission, "com.apple.security.virtualization"), Required: true, Detail: "Entitlement required by Apple's Virtualization.framework."),
                new HostDependencyRequirement(new HostDependencyRef(HostDependencyKind.ProviderDefined, "linux-guest-image"), Required: true, Detail: "Known Linux boot image for the first provider slice."),
                new HostDependencyRequirement(new HostDependencyRef(HostDependencyKind.ProviderDefined, "hpd-guest-agent"), Required: true, Detail: "In-guest agent required for readiness, projections, and process execution."),
            ],
        };
    }

    internal static PlatformSpec CurrentPlatform() =>
        new(
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "unknown",
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
}

public sealed class AppleVirtualizationCapabilityReporter : IProviderCapabilityReporter
{
    private readonly AppleVirtualizationProviderOptions _options;

    public AppleVirtualizationCapabilityReporter()
        : this(new AppleVirtualizationProviderOptions())
    {
    }

    public AppleVirtualizationCapabilityReporter(AppleVirtualizationProviderOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, CancellationToken cancellationToken = default) =>
        GetCapabilitiesAsync(providerId, new ProviderCapabilityQuery(CapabilityRequirementSet.Empty), cancellationToken);

    public ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(ProviderId providerId, ProviderCapabilityQuery query, CancellationToken cancellationToken = default)
    {
        PlatformSpec host = query.HostPlatform ?? AppleVirtualizationProviderDescriptor.CurrentPlatform();
        bool hostIsMac = string.Equals(host.OperatingSystem, "macos", StringComparison.OrdinalIgnoreCase);

        ProviderCapabilityReport report = new()
        {
            ProviderId = providerId,
            ObservedAt = DateTimeOffset.UtcNow,
            HostPlatform = host,
            Capabilities = CreateCapabilities(hostIsMac, _options),
            HostDependencies = CreateHostDependencies(hostIsMac),
            RequiredPermissions = CreatePermissions(hostIsMac),
            PreflightChecks = CreatePreflightChecks(hostIsMac),
            Conditions = CreateConditions(hostIsMac),
        };

        return ValueTask.FromResult(report);
    }

    private static IReadOnlyList<CapabilityFact> CreateCapabilities(bool hostIsMac, AppleVirtualizationProviderOptions options) =>
    [
        new CapabilityFact
        {
            Id = StandardEnvironmentCapabilities.ProcessIsolation,
            Category = new CapabilityCategory("isolation"),
            AppliesTo = ProviderContractKind.ExecutionUnit,
            State = hostIsMac ? CapabilityState.Supported : CapabilityState.Unsupported,
            Detail = "Workloads execute inside a managed hardware-virtualized guest.",
        },
        new CapabilityFact
        {
            Id = StandardEnvironmentCapabilities.ContainerIsolation,
            Category = new CapabilityCategory("isolation"),
            AppliesTo = ProviderContractKind.ExecutionUnit,
            State = hostIsMac ? CapabilityState.Supported : CapabilityState.Unsupported,
            Detail = "Container workloads execute inside the managed guest engine.",
        },
        new CapabilityFact
        {
            Id = StandardEnvironmentCapabilities.SharedHostKernel,
            Category = new CapabilityCategory("isolation"),
            AppliesTo = ProviderContractKind.ExecutionUnit,
            State = CapabilityState.Unsupported,
            Detail = "The managed guest does not share the macOS host kernel.",
        },
        new CapabilityFact
        {
            Id = StandardEnvironmentCapabilities.HardwareVirtualization,
            Category = new CapabilityCategory("isolation"),
            AppliesTo = ProviderContractKind.RuntimeHost,
            State = hostIsMac ? CapabilityState.Supported : CapabilityState.Unsupported,
            Detail = "Virtualization.framework provides the hardware-virtualized boundary.",
        },
        new CapabilityFact
        {
            Id = StandardEnvironmentCapabilities.GuestAgentBoundary,
            Category = new CapabilityCategory("isolation"),
            AppliesTo = ProviderContractKind.RuntimeHost,
            State = hostIsMac ? CapabilityState.Supported : CapabilityState.Unsupported,
            Detail = "The guest-agent transport is the bounded guest control boundary.",
        },
        new CapabilityFact
        {
            Id = StandardEnvironmentCapabilities.MediatedEngineAuthority,
            Category = new CapabilityCategory("authority"),
            AppliesTo = ProviderContractKind.AuthorityBinding |
                ProviderContractKind.EngineControlPlane,
            State = hostIsMac ? CapabilityState.Supported : CapabilityState.Unsupported,
            Detail = "Engine authority is mediated through HPD Environment and the guest agent.",
        },
        new CapabilityFact
        {
            Id = StandardEnvironmentCapabilities.HostLocalEndpointPublication,
            Category = new CapabilityCategory("endpoint"),
            AppliesTo = ProviderContractKind.EndpointPublication,
            State = hostIsMac ? CapabilityState.Supported : CapabilityState.Unsupported,
            Detail = "App endpoints are published through bounded host-local routes.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.HelperPreflightCapability,
            Category = new CapabilityCategory("preflight"),
            AppliesTo = ProviderContractKind.RuntimeHost,
            State = hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported,
            Detail = hostIsMac
                ? "Native Swift helper preflight can report host facts, framework support, entitlement/signing inspectability, and missing boot inputs without starting a VM."
                : "Native Apple Virtualization helper preflight is unavailable off macOS.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.RuntimeHostBootCapability,
            Category = new CapabilityCategory("runtime-host"),
            AppliesTo = ProviderContractKind.RuntimeHost,
            State = hostIsMac ? CapabilityState.RequiresPermission : CapabilityState.Unsupported,
            Detail = hostIsMac
                ? "Virtualization.framework host boot requires the hpd-vz helper and virtualization entitlement."
                : "Apple Virtualization.framework is available only on macOS hosts.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.ExecutionUnitCapability,
            Category = new CapabilityCategory("execution-unit"),
            AppliesTo = ProviderContractKind.ExecutionUnit,
            State = hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported,
            Detail = hostIsMac
                ? "Execution units require guest-agent readiness and a configured working directory policy."
                : "Apple Virtualization execution units are unavailable off macOS.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.ProcessInvocationCapability,
            Category = new CapabilityCategory("process"),
            AppliesTo = ProviderContractKind.ProcessInvocation,
            State = hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported,
            Detail = hostIsMac
                ? "Process invocation requires the Linux guest agent over the helper transport."
                : "Apple Virtualization process invocation is unavailable off macOS.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.ContentProjectionCapability,
            Category = new CapabilityCategory("content-projection"),
            AppliesTo = ProviderContractKind.ContentProjection,
            State = hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported,
            Detail = hostIsMac
                ? "Content projection requires helper-managed file sharing and guest mount verification."
                : "Apple Virtualization content projection is unavailable off macOS.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.NetworkCapability,
            Category = new CapabilityCategory("network"),
            AppliesTo = ProviderContractKind.Network,
            State = hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported,
            Detail = "Network resources support a conservative default NAT/IPv4 egress shape; endpoint publication and advanced attachment modes remain separate.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.ServiceDiscoveryCapability,
            Category = new CapabilityCategory("discovery"),
            AppliesTo = ProviderContractKind.ServiceDiscovery,
            State = hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported,
            Detail = "Service discovery derives bounded local HPD records from network membership observations; host DNS export/import remain unsupported.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.EndpointPublicationCapability,
            Category = new CapabilityCategory("endpoint"),
            AppliesTo = ProviderContractKind.EndpointPublication,
            State = hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported,
            Detail = "Endpoint publication supports conservative host-local TCP route records; broad exposure and sensitive endpoints remain deferred to policy and authority binding.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.AuthorityBindingCapability,
            Category = new CapabilityCategory("authority"),
            AppliesTo = ProviderContractKind.AuthorityBinding,
            State = hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported,
            Detail = "Authority binding supports contract-shaped lease, audit, redaction, classification, and helper protocol state. Real sensitive authority acceptance still requires explicit policy and remains conservative by default.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.EngineControlPlaneCapability,
            Category = new CapabilityCategory("engine"),
            AppliesTo = ProviderContractKind.EngineControlPlane,
            State = options.FeatureGates.EnableEngineControlPlane ? CapabilityState.RequiresConfiguration : CapabilityState.Deferred,
            Detail = "Docker/containerd is planned as an in-VM EngineControlPlane after boot, projection, and process execution are reliable.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.ArtifactCapability,
            Category = new CapabilityCategory("artifact"),
            AppliesTo = ProviderContractKind.Artifact,
            State = options.FeatureGates.EnableArtifactAndRootfsProviders ? CapabilityState.RequiresConfiguration : CapabilityState.Deferred,
            Detail = "Artifact import and image resolution are deferred; the first slice assumes known local boot inputs.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.RootFilesystemCapability,
            Category = new CapabilityCategory("rootfs"),
            AppliesTo = ProviderContractKind.RootFilesystemView,
            State = options.FeatureGates.EnableArtifactAndRootfsProviders ? CapabilityState.RequiresConfiguration : CapabilityState.Deferred,
            Detail = "Root filesystem materialization is deferred until artifact and image conversion policy are implemented.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.BlockVolumeCapability,
            Category = new CapabilityCategory("storage"),
            AppliesTo = ProviderContractKind.BlockVolume,
            State = CapabilityState.Deferred,
            Detail = "Block volumes are deferred; storage support is currently limited to known runtime host boot inputs.",
        },
        new CapabilityFact
        {
            Id = AppleVirtualizationProviderDescriptor.FunctionLaneCapability,
            Category = new CapabilityCategory("function"),
            AppliesTo = ProviderContractKind.FunctionSandbox | ProviderContractKind.FunctionInvocation | ProviderContractKind.FunctionSnapshot,
            State = options.FeatureGates.EnableFunctionLanes ? CapabilityState.RequiresConfiguration : CapabilityState.Deferred,
            Detail = "Function sandbox, invocation, and snapshot lanes are deferred; a Linux VM process agent does not satisfy function contracts.",
        },
    ];

    private static IReadOnlyList<HostDependencyFact> CreateHostDependencies(bool hostIsMac) =>
    [
        new HostDependencyFact(
            new HostDependencyRef(HostDependencyKind.Executable, AppleVirtualizationProviderDescriptor.HelperExecutableName),
            hostIsMac ? DependencyState.Missing : DependencyState.Misconfigured,
            Detail: hostIsMac
                ? "hpd-vz has not been discovered by this scaffolded provider."
                : "hpd-vz is only meaningful on macOS hosts."),
        new HostDependencyFact(
            new HostDependencyRef(HostDependencyKind.Permission, "com.apple.security.virtualization"),
            hostIsMac ? DependencyState.Unknown : DependencyState.Misconfigured,
            Detail: "The entitlement is verified by the signed Swift helper, not HPD core."),
        new HostDependencyFact(
            new HostDependencyRef(HostDependencyKind.ProviderDefined, "linux-guest-image"),
            hostIsMac ? DependencyState.Missing : DependencyState.Misconfigured,
            Detail: "A known Linux guest image is required before the first boot slice can become ready."),
        new HostDependencyFact(
            new HostDependencyRef(HostDependencyKind.ProviderDefined, "hpd-guest-agent"),
            hostIsMac ? DependencyState.Missing : DependencyState.Misconfigured,
            Detail: "The guest agent is required before process execution can be reported as ready."),
    ];

    private static IReadOnlyList<ProviderPermissionRequirement> CreatePermissions(bool hostIsMac) =>
        hostIsMac
            ?
            [
                new ProviderPermissionRequirement
                {
                    Id = new PermissionId("com.apple.security.virtualization"),
                    Capability = AppleVirtualizationProviderDescriptor.RuntimeHostBootCapability,
                    Scope = PermissionScope.Provider,
                    Required = true,
                    CanPrompt = false,
                    State = PermissionGrantState.VerificationFailed,
                    Severity = PermissionSeverity.Fatal,
                    DisplayMessage = "The signed hpd-vz helper must carry the Apple virtualization entitlement.",
                    Checks =
                    [
                        new PermissionCheck("helper-entitlement", PreflightCheckState.RequiresRemediation, "Entitlement verification is not implemented in the scaffold."),
                    ],
                },
            ]
            : Array.Empty<ProviderPermissionRequirement>();

    private static IReadOnlyList<ProviderPreflightCheck> CreatePreflightChecks(bool hostIsMac) =>
    [
        new ProviderPreflightCheck(
            "host-platform",
            hostIsMac ? PreflightCheckState.Passed : PreflightCheckState.Failed,
            hostIsMac ? DiagnosticSeverity.Info : DiagnosticSeverity.Fatal,
            hostIsMac ? "macOS host detected." : "Apple Virtualization.framework requires macOS."),
        new ProviderPreflightCheck(
            "helper-protocol-compatibility",
            hostIsMac ? PreflightCheckState.Unknown : PreflightCheckState.Skipped,
            DiagnosticSeverity.Info,
            "Protocol compatibility is confirmed by hpd-vz hello/preflight before native VM work proceeds."),
        new ProviderPreflightCheck(
            "virtualization-framework",
            hostIsMac ? PreflightCheckState.Unknown : PreflightCheckState.Skipped,
            DiagnosticSeverity.Info,
            "The Swift helper reports Virtualization.framework availability and VZVirtualMachine.isSupported when it is launched."),
        new ProviderPreflightCheck(
            "vm-boot-inputs",
            hostIsMac ? PreflightCheckState.Warning : PreflightCheckState.Skipped,
            hostIsMac ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
            "Known Linux boot/provisioning inputs are not configured in this L3 slice; helper facts report RequiresConfiguration."),
        new ProviderPreflightCheck(
            "helper-health-not-guest-readiness",
            PreflightCheckState.Passed,
            DiagnosticSeverity.Info,
            "Helper health only proves the hpd-vz protocol loop; HPD Ready requires guest-agent readiness."),
        new ProviderPreflightCheck(
            "hpd-vz-helper",
            hostIsMac ? PreflightCheckState.RequiresRemediation : PreflightCheckState.Skipped,
            hostIsMac ? DiagnosticSeverity.Error : DiagnosticSeverity.Info,
            "The hpd-vz helper is not implemented by this scaffold."),
        new ProviderPreflightCheck(
            "guest-agent",
            hostIsMac ? PreflightCheckState.RequiresRemediation : PreflightCheckState.Skipped,
            hostIsMac ? DiagnosticSeverity.Error : DiagnosticSeverity.Info,
            "The Linux guest agent is required for process execution and projection verification."),
    ];

    private static IReadOnlyList<Condition> CreateConditions(bool hostIsMac) =>
    [
        new Condition(
            "AppleVirtualizationHostSupported",
            hostIsMac ? ConditionStatus.True : ConditionStatus.False,
            hostIsMac ? "HostIsMacOS" : "HostNotMacOS",
            hostIsMac ? "Apple Virtualization host platform is available." : "Apple Virtualization requires a macOS host.",
            DateTimeOffset.UtcNow,
            default,
            hostIsMac ? DiagnosticSeverity.Info : DiagnosticSeverity.Fatal),
        new Condition(
            "AppleVirtualizationHelperReady",
            ConditionStatus.False,
            "HelperNotImplemented",
            "The hpd-vz helper has not been implemented or discovered.",
            DateTimeOffset.UtcNow,
            default,
            DiagnosticSeverity.Error),
    ];
}

[JsonSerializable(typeof(ProviderDescriptor))]
[JsonSerializable(typeof(ProviderCapabilityReport))]
[JsonSerializable(typeof(ProviderActivationSpec))]
[JsonSerializable(typeof(ProviderActivationStatus))]
[JsonSerializable(typeof(AppleVirtualizationProviderOptions))]
[JsonSerializable(typeof(AppleVirtualizationGuestImageOptions))]
[JsonSerializable(typeof(AppleVirtualizationGuestSharedDirectoryOptions))]
[JsonSerializable(typeof(AppleVirtualizationEngineBootstrapOptions))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningOptions))]
[JsonSerializable(typeof(AppleVirtualizationGuestBootLoaderKind))]
[JsonSerializable(typeof(AppleVirtualizationGuestArchitectureExpectation))]
[JsonSerializable(typeof(AppleVirtualizationGuestImageConfigurationState))]
[JsonSerializable(typeof(AppleVirtualizationProviderFeatureGates))]
[JsonSerializable(typeof(AppleVirtualizationHelperTransportMode))]
[JsonSerializable(typeof(AppleVirtualizationHelperEnvelope))]
[JsonSerializable(typeof(AppleVirtualizationHelperError))]
[JsonSerializable(typeof(AppleVirtualizationHelperHelloRequest))]
[JsonSerializable(typeof(AppleVirtualizationHelperHelloResponse))]
[JsonSerializable(typeof(AppleVirtualizationPreflightFact))]
[JsonSerializable(typeof(AppleVirtualizationPreflightFactState))]
[JsonSerializable(typeof(AppleVirtualizationCapabilitiesGetRequest))]
[JsonSerializable(typeof(AppleVirtualizationCapabilitiesGetResponse))]
[JsonSerializable(typeof(AppleVirtualizationPreflightRunRequest))]
[JsonSerializable(typeof(AppleVirtualizationPreflightRunResponse))]
[JsonSerializable(typeof(AppleVirtualizationActivationStatusResponse))]
[JsonSerializable(typeof(AppleVirtualizationHealthProbeRequest))]
[JsonSerializable(typeof(AppleVirtualizationHealthProbeResponse))]
[JsonSerializable(typeof(AppleVirtualizationShutdownRequest))]
[JsonSerializable(typeof(AppleVirtualizationShutdownResponse))]
[JsonSerializable(typeof(AppleVirtualizationHostEnsureRequest))]
[JsonSerializable(typeof(AppleVirtualizationHostLifecycleRequest))]
[JsonSerializable(typeof(AppleVirtualizationHostStatusResponse))]
[JsonSerializable(typeof(AppleVirtualizationGuestControlWaitReadyRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestControlStatusResponse))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentTransportProbeRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentTransportProbeResponse))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentTransportEndpoint))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentTransportState))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentTransportKind))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentReadinessProbeRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentReadinessProbeResponse))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentReadinessState))]
[JsonSerializable(typeof(AppleVirtualizationProjectionConfigureRequest))]
[JsonSerializable(typeof(AppleVirtualizationProjectionMountRequest))]
[JsonSerializable(typeof(AppleVirtualizationProjectionStatusRequest))]
[JsonSerializable(typeof(AppleVirtualizationProjectionUnmountRequest))]
[JsonSerializable(typeof(AppleVirtualizationProjectionObserveRequest))]
[JsonSerializable(typeof(AppleVirtualizationProjectionSyncRequest))]
[JsonSerializable(typeof(AppleVirtualizationProjectionFinalizationRequest))]
[JsonSerializable(typeof(AppleVirtualizationProjectionChangeEnumerationRequest))]
[JsonSerializable(typeof(AppleVirtualizationProjectionPromotionRequest))]
[JsonSerializable(typeof(AppleVirtualizationHelperProjectionScriptedGuestState))]
[JsonSerializable(typeof(AppleVirtualizationProjectionLifecycleRequest))]
[JsonSerializable(typeof(AppleVirtualizationProjectionStatusResponse))]
[JsonSerializable(typeof(AppleVirtualizationNetworkStatusRequest))]
[JsonSerializable(typeof(AppleVirtualizationNetworkStatusResponse))]
[JsonSerializable(typeof(AppleVirtualizationNetworkAttachmentCapabilityFact))]
[JsonSerializable(typeof(AppleVirtualizationNetworkAttachmentKind))]
[JsonSerializable(typeof(AppleVirtualizationNetworkObservationState))]
[JsonSerializable(typeof(AppleVirtualizationEndpointPublicationRequest))]
[JsonSerializable(typeof(AppleVirtualizationEndpointPublicationResponse))]
[JsonSerializable(typeof(AppleVirtualizationEndpointPublicationAction))]
[JsonSerializable(typeof(AppleVirtualizationAuthorityBindingAction))]
[JsonSerializable(typeof(AppleVirtualizationAuthoritySourceDescriptor))]
[JsonSerializable(typeof(AppleVirtualizationAuthorityTargetDescriptor))]
[JsonSerializable(typeof(AppleVirtualizationAuthorityProjectionDescriptor))]
[JsonSerializable(typeof(AppleVirtualizationAuthorityLeaseDescriptor))]
[JsonSerializable(typeof(AppleVirtualizationAuthorityBindingRequest))]
[JsonSerializable(typeof(AppleVirtualizationAuthorityBindingResponse))]
[JsonSerializable(typeof(AppleVirtualizationAuthorityRevocationEvidence))]
[JsonSerializable(typeof(AppleVirtualizationAuthorityRevocationEvidenceKind))]
[JsonSerializable(typeof(AppleVirtualizationEngineObservationState))]
[JsonSerializable(typeof(AppleVirtualizationEngineStatusRequest))]
[JsonSerializable(typeof(AppleVirtualizationEngineStatusResponse))]
[JsonSerializable(typeof(AppleVirtualizationRuntimeHostFingerprintInput))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningPackageManager))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningAction))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningPhase))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningExecutionState))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningPrerequisiteStatus))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningPlanStep))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningOutputCapture))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningEvidence))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningRequest))]
[JsonSerializable(typeof(AppleVirtualizationEngineProvisioningResponse))]
[JsonSerializable(typeof(AppleVirtualizationUnitEnsureRequest))]
[JsonSerializable(typeof(AppleVirtualizationUnitLifecycleRequest))]
[JsonSerializable(typeof(AppleVirtualizationUnitStatusResponse))]
[JsonSerializable(typeof(AppleVirtualizationProcessStartRequest))]
[JsonSerializable(typeof(SandboxPlanEnvelope))]
[JsonSerializable(typeof(SandboxIsolationPlan))]
[JsonSerializable(typeof(SandboxFilesystemIsolationPlan))]
[JsonSerializable(typeof(SandboxPathAccessRule))]
[JsonSerializable(typeof(SandboxNetworkIsolationPlan))]
[JsonSerializable(typeof(SandboxDomainRule))]
[JsonSerializable(typeof(SandboxUnixSocketIsolationPlan))]
[JsonSerializable(typeof(SandboxEnvironmentIsolationPlan))]
[JsonSerializable(typeof(SandboxTlsIsolationPlan))]
[JsonSerializable(typeof(SandboxInteractiveIsolationPlan))]
[JsonSerializable(typeof(SandboxViolationIsolationPlan))]
[JsonSerializable(typeof(SandboxIsolationDegradationPlan))]
[JsonSerializable(typeof(SandboxEnforcementLocation))]
[JsonSerializable(typeof(AppleVirtualizationProcessStdinRequest))]
[JsonSerializable(typeof(AppleVirtualizationProcessSignalRequest))]
[JsonSerializable(typeof(AppleVirtualizationProcessStopRequest))]
[JsonSerializable(typeof(AppleVirtualizationProcessResizeRequest))]
[JsonSerializable(typeof(AppleVirtualizationProcessLifecycleRequest))]
[JsonSerializable(typeof(AppleVirtualizationProcessStatusResponse))]
[JsonSerializable(typeof(AppleVirtualizationProcessOutputEvent))]
[JsonSerializable(typeof(AppleVirtualizationVmConfigurationValidationRequest))]
[JsonSerializable(typeof(AppleVirtualizationVmConfigurationValidationResponse))]
[JsonSerializable(typeof(AppleVirtualizationVmConfigurationSharedDirectory))]
[JsonSerializable(typeof(AppleVirtualizationVmConfigurationValidationPhase))]
[JsonSerializable(typeof(AppleVirtualizationVmConfigurationValidationState))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEnvelope))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentError))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentHello))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentHealth))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentReady))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentCapabilities))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionHostShareState))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionFrameworkShareState))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionVerificationState))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionIdentity))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionHostSourceIdentity))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionGuestPathExpectation))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionGuestPathObservation))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionGenerationStamp))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionMountRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionMountResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionStatusRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionStatus))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionUnmountRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionUnmountResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionObserveRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionObserveResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionSyncState))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionFinalizationState))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionPromotionState))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionSyncRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionSyncResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionFinalizationRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionFinalizationResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionChangeEnumerationRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionChangeEnumerationResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionPromotionRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionPromotionResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProjectionChange))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentNetworkStatusRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentNetworkStatus))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentNetworkInterfaceStatus))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentNetworkRouteObservation))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentNetworkListenerObservation))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentNetworkGenerationStamp))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthorityGenerationStamp))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthoritySource))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthorityTarget))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthorityProjection))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthorityLease))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthorityProjectionRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthorityStatusRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthorityRevocationRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentBoundAuthority))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthorityStatus))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentAuthorityRevocationResult))]
[JsonSerializable(typeof(AppleVirtualizationAuthorityEvidenceExtension))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineGenerationStamp))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineStatusRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineProbeReadiness))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineProbeIssue))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineProbeCandidate))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineProbeObservation))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineStatus))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineApiEndpoint))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineProvisioningRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentEngineProvisioningResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentContainerObservation))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessStartRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessStarted))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessStatusRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessStatus))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessStdinRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessCloseStdinRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessSignalRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessStopRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessControlResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessResizeRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessWaitRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessReadOutputRequest))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessOutputReadResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessOutputChunk))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessResult))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentCaptureAccounting))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentTerminalState))]
[JsonSerializable(typeof(AppleVirtualizationGuestAgentProcessGenerationStamp))]
internal sealed partial class AppleVirtualizationJsonContext : JsonSerializerContext;

internal sealed class AppleVirtualizationUnavailableHelperClient : IAppleVirtualizationHelperClient
{
    private long _sequence;

    public ValueTask<AppleVirtualizationHelperEnvelope> SendAsync(
        AppleVirtualizationHelperEnvelope request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string operation = AppleVirtualizationHelperOperationNames.ToWireName(request.Operation);
        var error = new AppleVirtualizationHelperError
        {
            Code = "AppleVirtualization.HelperUnavailable",
            Message = "The Apple Virtualization hpd-vz helper is not configured for this provider module instance.",
            Operation = operation,
            Retryable = true,
            FailedPhase = "Activation",
            Severity = DiagnosticSeverity.Error,
        };

        return ValueTask.FromResult(request.ToErrorResponse(Interlocked.Increment(ref _sequence), error));
    }

    public async IAsyncEnumerable<AppleVirtualizationHelperEnvelope> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }
}
