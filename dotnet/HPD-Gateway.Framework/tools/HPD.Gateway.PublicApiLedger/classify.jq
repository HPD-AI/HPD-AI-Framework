def simple: (.currentType | split(".") | last | split("+") | last | sub("`[0-9]+$"; ""));

def retained:
  if .currentAssembly == "HPD.Gateway" then true
  elif .currentAssembly == "HPD.Gateway.Abstractions" then
    (simple | IN(
      "CandidateId", "ContentHash", "CorsPolicyBinding", "CredentialDispositionBinding",
      "CredentialDispositionKind", "DeclarationDefinition", "DeclarationFamilyId",
      "DeclarationReference", "DefinitionId", "DestinationDeclaration", "DestinationId",
      "DiscoveryProfileId", "DiscoveryStaleBehavior", "GatewayCanonicalDocument", "GatewayConfiguration",
      "GatewayDefinitions", "GatewayIdentifier", "GatewayRootDeclarations",
      "GatewaySchemaVersion", "GatewayValidationError", "GatewayValidationErrorCode",
      "GatewayValidationResult", "HeaderTransformKind", "HealthCheckDeclaration",
      "ActiveHealthCheckDeclaration", "PassiveHealthCheckDeclaration", "HttpHeaderMatch",
      "HttpQueryMatch", "HttpRouteMatch", "HttpVersionSelection", "ListenerId",
      "LoadBalancingDeclaration", "LoadBalancingKind", "MetadataEntry",
      "NamedAuthorizationPolicy", "OrderedRequestTransforms", "OrderedResponseTransforms",
      "OutputCacheBinding", "ProviderId", "ProviderObjectId", "RequestHeaderTransform",
      "RequestInspectionBinding", "RequestInspectionMode", "RequestInspectionSpillPolicy",
      "RequestTimeoutBinding", "ResourceMetadata", "ResponseHeaderTransform",
      "RouteDeclaration", "RouteDeclarations", "RouteId", "SecretReference",
      "ServiceDiscoveryEndpointName", "ServiceDiscoveryEndpointSource",
      "ServiceDiscoveryName", "ServiceDiscoveryScheme", "SessionAffinityDeclaration",
      "StaticEndpointSource", "TelemetryEnrichment", "TextMatchKind",
      "TrafficAdmissionBinding", "UpstreamDeclaration", "UpstreamEndpointSource",
      "UpstreamHttpVersion", "UpstreamId", "UpstreamRequestDeclaration",
      "UpstreamResilienceBinding", "UpstreamTlsDeclaration", "UpstreamTransportDeclaration"
    ))
  elif .currentAssembly == "HPD.Gateway.Core" then
    (simple | IN(
      "DiscoveryProfileCapability", "DiscoveryProviderKind", "DiscoveryRuntimeKind",
      "GatewayCandidateReadResult", "GatewayCandidateReader", "GatewayDeclarationFamilies",
      "HostCapabilityRegistration", "HostCapabilitySnapshot", "ListenerCapability",
      "ListenerProtocols", "ListenerRole", "OutputCacheCapability", "OutputCacheStoreScope",
      "UpstreamResilienceCapability", "UpstreamResilienceStrategies"
    ))
  elif .currentAssembly == "HPD.Gateway.Effective" then
    (simple | IN(
      "GatewayAppliedMembershipDisposition", "GatewayAppliedRoute",
      "GatewayAppliedRuntimeObservation", "GatewayAppliedRuntimeSnapshot",
      "GatewayAppliedUpstream", "GatewayAppliedUpstreamKind", "GatewayContributionDisposition",
      "GatewayContributionScope", "GatewayContributionSourceKind", "GatewayEffectiveBounds",
      "GatewayEffectiveComposition", "GatewayEffectiveContribution", "GatewayEffectiveDiagnostic",
      "GatewayEffectiveFamilies", "GatewayEffectiveRecord", "GatewayEffectiveTargetKind",
      "GatewayMaterializationDisposition", "GatewayNativeProjection", "IGatewayNodeAppliedRuntimeReader"
    ))
  elif .currentAssembly == "HPD.Gateway.Hosting" then
    (simple | IN(
      "GatewayCertificateSourceRegistryBuilder", "GatewayEndpointRoleMetadata",
      "GatewayHostCandidate", "GatewayHostConfiguration", "GatewayHostId", "GatewayHostLifecycleExtensions",
      "GatewayHostSchemaVersion", "GatewayHttpsListenerDeclaration",
      "GatewayInboundTlsDeclaration", "GatewayKestrelHostingExtensions",
      "GatewayListenerBindingKind", "GatewayListenerProtocols", "GatewayListenerRole",
      "GatewayListenerRoleExtensions", "GatewayManagementExposure",
      "GatewayManagementListenerDeclaration", "GatewayPfxCertificateSource",
      "GatewaySniTlsDeclaration", "IHpdGatewayListenerFeature", "InboundTlsFallback"
    ))
  elif .currentAssembly == "HPD.Gateway.Inspection" then true
  elif .currentAssembly == "HPD.Gateway.OutputCaching" then
    (simple | IN("GatewayOutputCacheBuilderExtensions", "GatewayOutputCacheProfile", "GatewayOutputCacheRegistryBuilder"))
  elif .currentAssembly == "HPD.Gateway.Resilience" then
    (simple | IN(
      "GatewayAttemptTimeoutProfile", "GatewayCircuitBreakerProfile",
      "GatewayOutboundConcurrencyProfile", "GatewayResilienceBuilderExtensions",
      "GatewayResilienceProfile", "GatewayResilienceRegistryBuilder", "GatewayResponseRetryProfile"
    ))
  elif .currentAssembly == "HPD.Gateway.Status" then simple != "GatewayStatusJsonContext"
  elif .currentAssembly == "HPD.Gateway.Yarp" then
    (simple | IN(
      "ActivePublicationIdentity", "GatewayPublicationDiagnostic", "GatewayPublicationOutcome",
      "GatewayPublicationState", "PublicationCandidateIdentity"
    ))
  elif .currentAssembly == "HPD.Gateway.Admin" then
    (simple | IN(
      "GatewayAdminCapabilities", "GatewayAdminEndpointOptions",
      "GatewayAdminEndpointRouteBuilderExtensions", "GatewayAdminRequestAttribution",
      "GatewayAdminResource", "GatewayAdminResourceKind", "GatewayAdminResourcePolicies",
      "GatewayAdminServiceCollectionExtensions", "IGatewayAdminActorProjector",
      "IGatewayAdminSecurityMetadataProvider"
    ))
  elif .currentAssembly == "HPD.Gateway.Management" then
    (simple | IN(
      "GatewayActivateRevisionCommand", "GatewayAdministrativeCompletionState",
      "GatewayAdministrativeObservationKind", "GatewayAdministrativeOperationKind",
      "GatewayAdministrativeOperationReadProjection", "GatewayAdministrativeOperationReadState",
      "GatewayAdministrativeResult", "GatewayApplicationReadResult", "GatewayApplicationReadState",
      "GatewayAuthorityCapabilitySnapshot", "GatewayAuthorityDurability", "GatewayBackupArtifact",
      "GatewayBackupSinkRegistry", "GatewayDesiredProjection", "GatewayLocalProvisionTargetCommand",
      "GatewayManagedPage", "GatewayManagedRecord", "GatewayManagementActor",
      "GatewayManagementBuilder", "GatewayManagementCommandResult", "GatewayManagementCommandState",
      "GatewayManagementOptions", "GatewayManagementPurgeCategory", "GatewayManagementServiceCollectionExtensions",
      "GatewayManagementStatusSnapshot", "GatewayNodeOutcomeKind", "GatewayProvisionTargetCommand",
      "GatewayRevisionActivationKind", "GatewayRevisionComparison", "GatewayRevisionDifference",
      "GatewayRevisionExport", "GatewayRevisionMutation", "GatewayRollbackMutation",
      "GatewaySubmitCommand", "GatewayValidationOutcome", "IGatewayBackupSink",
      "IGatewayManagementAdministration", "IGatewayManagementApplication",
      "IGatewayManagementCommandCoordinator", "IGatewayManagementStatusReader"
    ))
  elif .currentAssembly == "HPD.Gateway.Studio" then true
  elif .currentAssembly == "HPD.Gateway.HPDAuth" then true
  elif .currentAssembly == "HPD.Gateway.Discovery.Microsoft" then true
  else false end;

def owner:
  if .currentAssembly == "HPD.Gateway.Abstractions" then "Decision0001DeclarationContract"
  elif .currentAssembly == "HPD.Gateway.Core" then "Decision0001CandidateAdmissionAndDecision0011Composition"
  elif .currentAssembly == "HPD.Gateway.Hosting" then "Decision0008HostingContract"
  elif .currentAssembly == "HPD.Gateway.Inspection" then "Decision0004InspectionExtensionContract"
  elif .currentAssembly == "HPD.Gateway.OutputCaching" then "Decision0007OutputCacheProfiles"
  elif .currentAssembly == "HPD.Gateway.Resilience" then "Decision0005ResilienceProfiles"
  elif .currentAssembly == "HPD.Gateway.Status" then "Decision0009StatusContract"
  elif .currentAssembly == "HPD.Gateway.Effective" then "Decision0010AppliedAndEffectiveTruth"
  elif .currentAssembly == "HPD.Gateway" then "Decision0011CompositionAndActivation"
  elif .currentAssembly == "HPD.Gateway.Yarp" then "Decision0011PublicationOutcomeContract"
  elif .currentAssembly == "HPD.Gateway.Management" then "Decision0012ProgrammaticControlPlane"
  elif .currentAssembly == "HPD.Gateway.Admin" then "Decision0013AdminHostExtensionContract"
  elif .currentAssembly == "HPD.Gateway.Studio" then "Decision0014StudioHostExtensionContract"
  elif .currentAssembly == "HPD.Gateway.HPDAuth" then "Decision0013OptionalHpdAuthAdapter"
  elif .currentAssembly == "HPD.Gateway.Discovery.Microsoft" then "Decision0015MicrosoftDiscoveryProfile"
  else "ImplementationOnly" end;

def aot:
  if retained and (.currentAssembly == "HPD.Gateway.Abstractions") then "GatewayJsonSerializerContext"
  elif retained and (.currentAssembly == "HPD.Gateway.Effective") then "GatewayEffectiveJsonSerializerContext"
  elif retained and (.currentAssembly == "HPD.Gateway.Status") then "GatewayStatusJsonContext"
  elif retained and (.currentAssembly == "HPD.Gateway.Management") then "GatewayManagementJsonContextWhenSerialized"
  else "NoGeneratedSerializationRoot" end;

{
  classificationVersion: "hpd-gateway-public-type-classification/v1",
  recordCount: .recordCount,
  records: [.records[] | . + (
    if retained then {
      finalAccessibility: "Public",
      consumerOrContract: owner,
      nativeAotConsequence: aot
    } else {
      disposition: "ImplementationInternal",
      finalAccessibility: "Internal",
      consumerOrContract: "NoExternalConsumerImplementationOnly",
      nativeAotConsequence: (if (.currentType | contains("JsonContext")) then "RetainInternalGeneratedContext" else "NoPublicRoot" end)
    } end
  )]
}
