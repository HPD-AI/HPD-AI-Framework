namespace HPD.Environment.AppleVirtualization.Tests;

using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using HPD.Environment.AppleVirtualization.ExecutionUnits;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Projections;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;

public sealed class AppleVirtualizationProviderModuleTests
{
    [Fact]
    public async Task Module_registers_descriptor_and_implemented_provider_families()
    {
        var registry = new EnvironmentProviderRegistry();

        registry.RegisterAppleVirtualizationProvider();

        IReadOnlyList<ProviderDescriptor> providers = await registry.ListAsync();
        ProviderDescriptor descriptor = providers.Single();

        descriptor.Id.Should().Be(AppleVirtualizationProviderDescriptor.ProviderId);
        descriptor.DisplayName.Should().Be("HPD Apple Virtualization Provider");
        descriptor.ContractKinds.Should().Be(AppleVirtualizationProviderDescriptor.FirstSliceContracts);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.RuntimeHost);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.ExecutionUnit);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.ProcessInvocation);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.ContentProjection);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.Network);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.NetworkMembership);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.ServiceDiscovery);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.EndpointPublication);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.AuthorityBinding);
        descriptor.ContractKinds.Should().NotHaveFlag(ProviderContractKind.EngineControlPlane);

        ProviderActivationModel activation = descriptor.ActivationModels.Single();
        activation.Kind.Should().Be(ProviderActivationKind.SupervisedExecutable);
        activation.Scope.Should().Be(ProviderActivationScope.Runtime);
        activation.Transport.Should().Be(ProviderTransportKind.StdIo);
        activation.RequiresSupervision.Should().BeTrue();

        registry.ProviderCapabilityReporters.Should().ContainSingle();
        registry.RuntimeHostProviders.Should().ContainSingle();
        registry.ExecutionUnitProviders.Should().ContainSingle();
        registry.ProcessProviders.Should().ContainSingle();
        registry.ContentProjectionProviders.Should().ContainSingle();
        registry.NetworkProviders.Should().ContainSingle();
        registry.NetworkMembershipProviders.Should().ContainSingle();
        registry.ServiceDiscoveryProviders.Should().ContainSingle();
        registry.EndpointPublicationProviders.Should().ContainSingle();
        registry.AuthorityBindingProviders.Should().ContainSingle();
        registry.ArtifactProviders.Should().BeEmpty();
        registry.RootFilesystemProviders.Should().BeEmpty();
        registry.EngineControlPlaneProviders.Should().BeEmpty();
        registry.FunctionSandboxProviders.Should().BeEmpty();
        registry.FunctionSnapshotProviders.Should().BeEmpty();
    }

    [Fact]
    public async Task Capability_report_is_honest_about_scaffold_readiness_on_current_host()
    {
        var reporter = new AppleVirtualizationCapabilityReporter();

        ProviderCapabilityReport report = await reporter.GetCapabilitiesAsync(
            AppleVirtualizationProviderDescriptor.ProviderId,
            new ProviderCapabilityQuery(HostPlatform: CurrentPlatform()));

        bool hostIsMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        report.ProviderId.Should().Be(AppleVirtualizationProviderDescriptor.ProviderId);
        report.HostPlatform.Should().Be(CurrentPlatform());
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.HelperPreflightCapability &&
            fact.AppliesTo == ProviderContractKind.RuntimeHost &&
            fact.State == (hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.RuntimeHostBootCapability &&
            fact.AppliesTo == ProviderContractKind.RuntimeHost &&
            fact.State == (hostIsMac ? CapabilityState.RequiresPermission : CapabilityState.Unsupported));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.ExecutionUnitCapability &&
            fact.State == (hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.ProcessInvocationCapability &&
            fact.State == (hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.ContentProjectionCapability &&
            fact.State == (hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.NetworkCapability &&
            fact.State == (hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.ServiceDiscoveryCapability &&
            fact.AppliesTo == ProviderContractKind.ServiceDiscovery &&
            fact.State == (hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.EndpointPublicationCapability &&
            fact.State == (hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.AuthorityBindingCapability &&
            fact.State == (hostIsMac ? CapabilityState.RequiresConfiguration : CapabilityState.Unsupported));
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.EngineControlPlaneCapability &&
            fact.State == CapabilityState.Deferred);
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.ArtifactCapability &&
            fact.State == CapabilityState.Deferred);
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.RootFilesystemCapability &&
            fact.State == CapabilityState.Deferred);
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.BlockVolumeCapability &&
            fact.State == CapabilityState.Deferred);
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.FunctionLaneCapability &&
            fact.State == CapabilityState.Deferred);
        report.PreflightChecks.Should().Contain(check => check.Name == "hpd-vz-helper");
        report.PreflightChecks.Should().Contain(check => check.Name == "guest-agent");
        report.PreflightChecks.Should().Contain(check =>
            check.Name == "helper-protocol-compatibility" &&
            check.State == (hostIsMac ? PreflightCheckState.Unknown : PreflightCheckState.Skipped));
        report.PreflightChecks.Should().Contain(check =>
            check.Name == "virtualization-framework" &&
            check.State == (hostIsMac ? PreflightCheckState.Unknown : PreflightCheckState.Skipped));
        report.PreflightChecks.Should().Contain(check =>
            check.Name == "vm-boot-inputs" &&
            check.State == (hostIsMac ? PreflightCheckState.Warning : PreflightCheckState.Skipped) &&
            check.Detail!.Contains("RequiresConfiguration", StringComparison.Ordinal));
        report.PreflightChecks.Should().Contain(check =>
            check.Name == "helper-health-not-guest-readiness" &&
            check.State == PreflightCheckState.Passed &&
            check.Detail!.Contains("HPD Ready", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Capability_report_models_non_macos_hosts_as_unsupported()
    {
        var reporter = new AppleVirtualizationCapabilityReporter();

        ProviderCapabilityReport report = await reporter.GetCapabilitiesAsync(
            AppleVirtualizationProviderDescriptor.ProviderId,
            new ProviderCapabilityQuery(HostPlatform: new PlatformSpec("linux", "x64")));

        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.RuntimeHostBootCapability &&
            fact.State == CapabilityState.Unsupported);
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.ExecutionUnitCapability &&
            fact.State == CapabilityState.Unsupported);
        report.RequiredPermissions.Should().BeEmpty();
        report.PreflightChecks.Should().Contain(check =>
            check.Name == "host-platform" &&
            check.State == PreflightCheckState.Failed);
        report.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualizationHostSupported" &&
            condition.Status == ConditionStatus.False);
    }

    [Fact]
    public async Task Planner_uses_apple_activation_model_and_rejects_not_ready_capabilities()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterAppleVirtualizationProvider();
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
                        Id = AppleVirtualizationProviderDescriptor.RuntimeHostBootCapability,
                        AppliesTo = ProviderContractKind.RuntimeHost,
                        Strength = CapabilityRequirementStrength.Required,
                    },
                ],
            },
        });
        RuntimePlanValidationResult validation = await planner.ValidateAsync(plan);

        validation.IsSupported.Should().BeFalse();
        ProviderActivationSpec activation = plan.Activations.Single();
        activation.ProviderId.Should().Be(AppleVirtualizationProviderDescriptor.ProviderId);
        activation.ActivationKind.Should().Be(ProviderActivationKind.SupervisedExecutable);
        activation.Transport.TransportKind.Should().Be(ProviderTransportKind.StdIo);
        activation.Supervisor.RequiresSupervision.Should().BeTrue();
        plan.UnsupportedReasons.Should().Contain(reason =>
            reason.Code.Value == "hpd.execution.capability.requires-permission" ||
            reason.Code.Value == "hpd.execution.capability.unsupported");
    }

    [Fact]
    public void Source_generated_json_metadata_covers_provider_dtos()
    {
        var descriptor = AppleVirtualizationProviderDescriptor.Create();
        string json = JsonSerializer.Serialize(descriptor, AppleVirtualizationJsonContext.Default.ProviderDescriptor);
        ProviderDescriptor? roundTrip = JsonSerializer.Deserialize(json, AppleVirtualizationJsonContext.Default.ProviderDescriptor);

        roundTrip.Should().NotBeNull();
        roundTrip!.Id.Should().Be(AppleVirtualizationProviderDescriptor.ProviderId);
        AppleVirtualizationJsonContext.Default.ProviderCapabilityReport.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.ProviderActivationSpec.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.ProviderActivationStatus.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.AppleVirtualizationProviderOptions.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestImageOptions.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestSharedDirectoryOptions.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.AppleVirtualizationProviderFeatureGates.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.AppleVirtualizationNetworkStatusRequest.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.AppleVirtualizationNetworkStatusResponse.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentNetworkStatusRequest.Should().NotBeNull();
        AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentNetworkStatus.Should().NotBeNull();
        AppleVirtualizationExecutionUnitJsonContext.Default.AppleVirtualizationExecutionUnitContextExtension.Should().NotBeNull();
    }

    [Fact]
    public void Module_registers_explicit_json_type_metadata_for_protocol_options_and_extensions()
    {
        var registry = new EnvironmentProviderRegistry();

        registry.RegisterAppleVirtualizationProvider();

        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(ProviderDescriptor));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationProviderOptions));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestImageOptions));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestSharedDirectoryOptions));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationProviderFeatureGates));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationHelperEnvelope));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationNetworkStatusRequest));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationNetworkStatusResponse));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentNetworkStatusRequest));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationGuestAgentNetworkStatus));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationPreflightFact));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationProcessOutputEvent));
        registry.JsonTypes.Should().Contain(registration => registration.Type == typeof(AppleVirtualizationExecutionUnitContextExtension));
        registry.JsonTypes.Select(registration => registration.TypeDiscriminator).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Module_default_unavailable_helper_returns_structured_errors_without_fake_success()
    {
        string hostPath = Path.Combine(Path.GetTempPath(), "hpd-applevz-unavailable-helper", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(hostPath);
        try
        {
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(new AppleVirtualizationProviderModule(
                new AppleVirtualizationProviderOptions
                {
                    StateRoot = Path.Combine(hostPath, ".state"),
                },
                helperClient: null,
                ledger: new AppleVirtualizationProviderStateLedger(),
                capabilityReporter: null,
                hostPlatformOverride: new PlatformSpec("macos", "arm64")));

            ResourceMetadata<RuntimeHost> hostMetadata =
                AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-unavailable", "runtime-host");
            RuntimeHostStatus host = await registry.RuntimeHostProviders.Single().EnsureAsync(
                hostMetadata,
                AppleVirtualizationContractFixtures.RuntimeHostSpec(),
                observed: null);

            host.Phase.Should().Be(ResourcePhase.Failed);
            host.HostPhase.Should().Be(RuntimeHostPhase.Failed);
            host.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code.Value == "AppleVirtualization.HelperUnavailable" &&
                diagnostic.TargetPath == "host.ensure");

            ExecutionUnitStatus unit = await registry.ExecutionUnitProviders.Single().EnsureAsync(
                AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>("unit-unavailable", "execution-unit"),
                AppleVirtualizationContractFixtures.ExecutionUnitSpec(new ResourceRef<RuntimeHost>(
                    hostMetadata.Id,
                    hostMetadata.Scope,
                    hostMetadata.Generation)),
                observed: null);

            unit.Phase.Should().Be(ResourcePhase.Failed);
            unit.UnitPhase.Should().Be(ExecutionUnitPhase.Failed);
            unit.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code.Value == "AppleVirtualization.HelperUnavailable" &&
                diagnostic.TargetPath == "unit.ensure");

            ContentProjectionStatus projection = await registry.ContentProjectionProviders.Single().ProjectAsync(
                AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-unavailable", "content-projection"),
                ProjectionSpec(hostPath),
                host.Handle,
                unit: null);

            projection.Phase.Should().Be(ResourcePhase.Failed);
            projection.ProjectionPhase.Should().Be(ContentProjectionPhase.Failed);
            projection.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code.Value == "AppleVirtualization.HelperUnavailable" &&
                diagnostic.TargetPath == "projection.configure");
        }
        finally
        {
            if (Directory.Exists(hostPath))
            {
                Directory.Delete(hostPath, recursive: true);
            }
        }
    }

    [Fact]
    public void Provider_options_round_trip_through_source_generated_json()
    {
        var options = new AppleVirtualizationProviderOptions
        {
            HelperPath = "/opt/hpd/bin/hpd-vz",
            HelperArguments = ["--fake"],
            HelperTransportMode = AppleVirtualizationHelperTransportMode.UnixSocket,
            StateRoot = "/var/lib/hpd/apple-vz",
            HelperStartupTimeout = TimeSpan.FromSeconds(3),
            HelperStopTimeout = TimeSpan.FromSeconds(1),
            StartupStderrCaptureBytes = 2048,
            DefaultCpuCores = 8,
            DefaultMemoryBytes = 16L * 1024 * 1024 * 1024,
            DefaultDiskBytes = 128L * 1024 * 1024 * 1024,
            GuestImage = new AppleVirtualizationGuestImageOptions
            {
                BundleRoot = "/opt/hpd/guests/applevz-linux-arm64",
                BootLoader = AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader,
                KernelPath = "/opt/hpd/guests/applevz-linux-arm64/vmlinuz",
                InitrdPath = "/opt/hpd/guests/applevz-linux-arm64/initrd.img",
                KernelCommandLine = "console=hvc0 root=/dev/vda1 rw",
                DiskImagePath = "/opt/hpd/guests/applevz-linux-arm64/root.raw",
                SerialLogPath = "/var/log/hpd/apple-vz/runtime-host.serial.log",
                Architecture = AppleVirtualizationGuestArchitectureExpectation.Arm64,
                ExpectVirtiofsSupport = true,
                ExpectedGuestAgentVersion = "0.1.0",
                GuestAgentConfigPath = "/etc/hpd/guest-agent/config.json",
                GuestAgentBootstrapPath = "/opt/hpd/guest-agent/bootstrap.json",
                GuestAgentBootstrapInlinePayloadRef = "hpd-provider-payload://guest-agent/bootstrap",
                SharedDirectories =
                [
                    new AppleVirtualizationGuestSharedDirectoryOptions
                    {
                        Tag = "workspace",
                        HostPath = "/Users/example/workspace",
                        ReadOnly = false,
                    },
                ],
            },
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableRealHelperActivation = true,
                EnableRealVmBoot = true,
                EnableVmConfigurationValidation = true,
                EnableNetworkResources = true,
                EnableEndpointPublication = true,
                EnableAuthorityBinding = true,
                EnableEngineControlPlane = true,
                EnableArtifactAndRootfsProviders = true,
                EnableFunctionLanes = true,
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            options,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationProviderOptions);
        AppleVirtualizationProviderOptions? roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationProviderOptions);

        roundTrip.Should().NotBeNull();
        roundTrip!.HelperPath.Should().Be(options.HelperPath);
        roundTrip.HelperArguments.Should().Equal("--fake");
        roundTrip.HelperTransportMode.Should().Be(AppleVirtualizationHelperTransportMode.UnixSocket);
        roundTrip.StateRoot.Should().Be(options.StateRoot);
        roundTrip.HelperStartupTimeout.Should().Be(TimeSpan.FromSeconds(3));
        roundTrip.HelperStopTimeout.Should().Be(TimeSpan.FromSeconds(1));
        roundTrip.StartupStderrCaptureBytes.Should().Be(2048);
        roundTrip.DefaultCpuCores.Should().Be(8);
        roundTrip.DefaultMemoryBytes.Should().Be(16L * 1024 * 1024 * 1024);
        roundTrip.DefaultDiskBytes.Should().Be(128L * 1024 * 1024 * 1024);
        roundTrip.GuestImage.BundleRoot.Should().Be(options.GuestImage.BundleRoot);
        roundTrip.GuestImage.BootLoader.Should().Be(AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader);
        roundTrip.GuestImage.KernelPath.Should().Be(options.GuestImage.KernelPath);
        roundTrip.GuestImage.InitrdPath.Should().Be(options.GuestImage.InitrdPath);
        roundTrip.GuestImage.KernelCommandLine.Should().Be(options.GuestImage.KernelCommandLine);
        roundTrip.GuestImage.DiskImagePath.Should().Be(options.GuestImage.DiskImagePath);
        roundTrip.GuestImage.SerialLogPath.Should().Be(options.GuestImage.SerialLogPath);
        roundTrip.GuestImage.Architecture.Should().Be(AppleVirtualizationGuestArchitectureExpectation.Arm64);
        roundTrip.GuestImage.ExpectVirtiofsSupport.Should().BeTrue();
        roundTrip.GuestImage.ExpectedGuestAgentVersion.Should().Be("0.1.0");
        roundTrip.GuestImage.GuestAgentConfigPath.Should().Be(options.GuestImage.GuestAgentConfigPath);
        roundTrip.GuestImage.GuestAgentBootstrapPath.Should().Be(options.GuestImage.GuestAgentBootstrapPath);
        roundTrip.GuestImage.GuestAgentBootstrapInlinePayloadRef.Should().Be(options.GuestImage.GuestAgentBootstrapInlinePayloadRef);
        roundTrip.GuestImage.SharedDirectories.Should().ContainSingle(share =>
            share.Tag == "workspace" &&
            share.HostPath == "/Users/example/workspace" &&
            !share.ReadOnly);
        roundTrip.GuestImage.GetConfigurationState().Should().Be(AppleVirtualizationGuestImageConfigurationState.Complete);
        roundTrip.FeatureGates.EnableInMemoryFakeHelper.Should().BeFalse();
        roundTrip.FeatureGates.EnableRealHelperActivation.Should().BeTrue();
        roundTrip.FeatureGates.EnableRealVmBoot.Should().BeTrue();
        roundTrip.FeatureGates.EnableVmConfigurationValidation.Should().BeTrue();
        roundTrip.FeatureGates.EnableNetworkResources.Should().BeTrue();
        roundTrip.FeatureGates.EnableEndpointPublication.Should().BeTrue();
        roundTrip.FeatureGates.EnableAuthorityBinding.Should().BeTrue();
        roundTrip.FeatureGates.EnableEngineControlPlane.Should().BeTrue();
        roundTrip.FeatureGates.EnableArtifactAndRootfsProviders.Should().BeTrue();
        roundTrip.FeatureGates.EnableFunctionLanes.Should().BeTrue();
    }

    [Fact]
    public void Missing_required_guest_image_boot_inputs_are_representable_as_configuration_missing()
    {
        new AppleVirtualizationGuestImageOptions()
            .GetConfigurationState()
            .Should()
            .Be(AppleVirtualizationGuestImageConfigurationState.MissingRequiredBootInputs);

        new AppleVirtualizationGuestImageOptions
        {
            BootLoader = AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader,
            DiskImagePath = "/opt/hpd/guests/applevz-linux-arm64/root.raw",
        }.GetConfigurationState().Should().Be(AppleVirtualizationGuestImageConfigurationState.MissingRequiredBootInputs);

        new AppleVirtualizationGuestImageOptions
        {
            BootLoader = AppleVirtualizationGuestBootLoaderKind.Efi,
            DiskImagePath = "/opt/hpd/guests/applevz-linux-arm64/root.raw",
        }.GetConfigurationState().Should().Be(AppleVirtualizationGuestImageConfigurationState.MissingRequiredBootInputs);
    }

    [Fact]
    public async Task Deferred_feature_gates_create_capability_facts_without_registering_deferred_lanes()
    {
        var options = new AppleVirtualizationProviderOptions
        {
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableNetworkResources = true,
                EnableEndpointPublication = true,
                EnableAuthorityBinding = true,
                EnableEngineControlPlane = true,
                EnableArtifactAndRootfsProviders = true,
                EnableFunctionLanes = true,
            },
        };
        var registry = new EnvironmentProviderRegistry();

        registry.RegisterModule(new AppleVirtualizationProviderModule(options));

        ProviderDescriptor descriptor = (await registry.ListAsync()).Single();
        descriptor.ContractKinds.Should().Be(AppleVirtualizationProviderDescriptor.FirstSliceContracts | ProviderContractKind.EngineControlPlane);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.Network);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.NetworkMembership);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.ServiceDiscovery);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.EndpointPublication);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.AuthorityBinding);
        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.EngineControlPlane);
        descriptor.ContractKinds.Should().NotHaveFlag(ProviderContractKind.Artifact);
        descriptor.ContractKinds.Should().NotHaveFlag(ProviderContractKind.RootFilesystemView);
        descriptor.ContractKinds.Should().NotHaveFlag(ProviderContractKind.BlockVolume);
        descriptor.ContractKinds.Should().NotHaveFlag(ProviderContractKind.FunctionSandbox);
        descriptor.ContractKinds.Should().NotHaveFlag(ProviderContractKind.FunctionInvocation);
        descriptor.ContractKinds.Should().NotHaveFlag(ProviderContractKind.FunctionSnapshot);

        registry.NetworkProviders.Should().ContainSingle();
        registry.NetworkMembershipProviders.Should().ContainSingle();
        registry.ServiceDiscoveryProviders.Should().ContainSingle();
        registry.EndpointPublicationProviders.Should().ContainSingle();
        registry.AuthorityBindingProviders.Should().ContainSingle();
        registry.EngineControlPlaneProviders.Should().ContainSingle();
        registry.ArtifactProviders.Should().BeEmpty();
        registry.RootFilesystemProviders.Should().BeEmpty();
        registry.FunctionSandboxProviders.Should().BeEmpty();
        registry.FunctionSnapshotProviders.Should().BeEmpty();

        ProviderCapabilityReport report = await registry.ProviderCapabilityReporters.Single().GetCapabilitiesAsync(
            AppleVirtualizationProviderDescriptor.ProviderId,
            new ProviderCapabilityQuery(HostPlatform: new PlatformSpec("macos", "arm64")));

        AssertCapabilityFact(report, AppleVirtualizationProviderDescriptor.HelperPreflightCapability, ProviderContractKind.RuntimeHost, CapabilityState.RequiresConfiguration);
        AssertCapabilityFact(report, AppleVirtualizationProviderDescriptor.NetworkCapability, ProviderContractKind.Network, CapabilityState.RequiresConfiguration);
        AssertCapabilityFact(report, AppleVirtualizationProviderDescriptor.ServiceDiscoveryCapability, ProviderContractKind.ServiceDiscovery, CapabilityState.RequiresConfiguration);
        AssertCapabilityFact(report, AppleVirtualizationProviderDescriptor.EndpointPublicationCapability, ProviderContractKind.EndpointPublication, CapabilityState.RequiresConfiguration);
        AssertCapabilityFact(report, AppleVirtualizationProviderDescriptor.AuthorityBindingCapability, ProviderContractKind.AuthorityBinding, CapabilityState.RequiresConfiguration);
        AssertCapabilityFact(report, AppleVirtualizationProviderDescriptor.EngineControlPlaneCapability, ProviderContractKind.EngineControlPlane, CapabilityState.RequiresConfiguration);
        AssertCapabilityFact(report, AppleVirtualizationProviderDescriptor.ArtifactCapability, ProviderContractKind.Artifact, CapabilityState.RequiresConfiguration);
        AssertCapabilityFact(report, AppleVirtualizationProviderDescriptor.RootFilesystemCapability, ProviderContractKind.RootFilesystemView, CapabilityState.RequiresConfiguration);
        AssertCapabilityFact(report, AppleVirtualizationProviderDescriptor.BlockVolumeCapability, ProviderContractKind.BlockVolume, CapabilityState.Deferred);
        report.Capabilities.Should().Contain(fact =>
            fact.Id == AppleVirtualizationProviderDescriptor.FunctionLaneCapability &&
            fact.AppliesTo.HasFlag(ProviderContractKind.FunctionSandbox) &&
            fact.AppliesTo.HasFlag(ProviderContractKind.FunctionInvocation) &&
            fact.AppliesTo.HasFlag(ProviderContractKind.FunctionSnapshot) &&
            fact.State == CapabilityState.RequiresConfiguration);
    }

    [Fact]
    public async Task Module_registered_providers_complete_fake_helper_vertical_slice()
    {
        string hostPath = Path.Combine(Path.GetTempPath(), "hpd-applevz-module-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(hostPath);
        try
        {
            var helper = new FakeAppleVirtualizationHelperClient();
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(new AppleVirtualizationProviderModule(
                new AppleVirtualizationProviderOptions
                {
                    HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake,
                    StateRoot = Path.Combine(hostPath, ".state"),
                },
                helper,
                new AppleVirtualizationProviderStateLedger(),
                capabilityReporter: null,
                hostPlatformOverride: new PlatformSpec("macos", "arm64")));

            helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
            helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
            helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
            helper.EnqueueResponse(GuestAgentReadinessResponse());

            ResourceMetadata<RuntimeHost> hostMetadata = AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host");
            RuntimeHostStatus host = await registry.RuntimeHostProviders.Single().EnsureAsync(
                hostMetadata,
                AppleVirtualizationContractFixtures.RuntimeHostSpec(),
                observed: null);

            helper.EnqueueResponse(ProjectionResponse(AppleVirtualizationHelperOperation.ProjectionConfigure, "projection-1", ContentProjectionPhase.Projecting));
            helper.EnqueueResponse(ProjectionResponse(
                AppleVirtualizationHelperOperation.ProjectionMount,
                "projection-1",
                ContentProjectionPhase.Projected,
                GuestMountVerified(new ResourceGeneration(1))));

            ResourceMetadata<ContentProjection> projectionMetadata = AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-1", "content-projection");
            ContentProjectionStatus projection = await registry.ContentProjectionProviders.Single().ProjectAsync(
                projectionMetadata,
                ProjectionSpec(hostPath),
                host.Handle!.Value,
                unit: null);

            helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready, "/hpd/units/unit-1"));

            ExecutionUnitStatus unit = await registry.ExecutionUnitProviders.Single().EnsureAsync(
                AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>("unit-1", "execution-unit"),
                AppleVirtualizationContractFixtures.ExecutionUnitSpec(new ResourceRef<RuntimeHost>(
                    hostMetadata.Id,
                    hostMetadata.Scope,
                    hostMetadata.Generation)),
                observed: null);

            helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
            helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, "process-1", ProcessInvocationPhase.Running));
            helper.EnqueueEvent(ProcessOutput("process-1", ProcessOutputStream.Stdout, new byte[] { 0x6f, 0x6b }, final: true));
            helper.EnqueueResponse(ProcessExited("process-1", exitCode: 0));
            var sink = new RecordingProcessOutputSink();

            ProcessInvocationResult result = await registry.ProcessProviders.Single().RunAsync(
                AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Handle),
                sink);

            host.HostPhase.Should().Be(RuntimeHostPhase.Ready);
            projection.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);
            unit.UnitPhase.Should().Be(ExecutionUnitPhase.Ready);
            result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
            result.Output.Stdout.CapturedBytes.ToArray().Should().Equal(0x6f, 0x6b);
            sink.Chunks.Should().ContainSingle();
            helper.Requests.Select(request => request.Operation).Should().ContainInOrder(
                AppleVirtualizationHelperOperation.HostEnsure,
                AppleVirtualizationHelperOperation.HostStart,
                AppleVirtualizationHelperOperation.HostStatus,
                AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
                AppleVirtualizationHelperOperation.ProjectionConfigure,
                AppleVirtualizationHelperOperation.ProjectionMount,
                AppleVirtualizationHelperOperation.UnitEnsure,
                AppleVirtualizationHelperOperation.ProcessStart,
                AppleVirtualizationHelperOperation.ProcessReadOutput,
                AppleVirtualizationHelperOperation.ProcessWait);
        }
        finally
        {
            if (Directory.Exists(hostPath))
            {
                Directory.Delete(hostPath, recursive: true);
            }
        }
    }

    private static PlatformSpec CurrentPlatform() =>
        new(
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "unknown",
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

    private static ContentProjectionSpec ProjectionSpec(string hostPath) =>
        AppleVirtualizationContractFixtures.ReadOnlyWorkspaceProjection() with
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.HostPath,
                HostPath = new HostPathSelection(new HostPath(hostPath), HostPathKind.Directory),
            },
            AccessMode = AccessMode.ReadOnly,
            Realization = new ProjectionRealizationSpec
            {
                Kind = ProjectionRealizationKind.LiveProjection,
                WriteEffect = ProjectionWriteEffect.NoWrites,
                RequestedCoherence = CoherenceClass.CloseToOpen,
                Cache = CacheBehavior.ReadCache,
            },
        };

    private static void AssertCapabilityFact(
        ProviderCapabilityReport report,
        CapabilityId id,
        ProviderContractKind appliesTo,
        CapabilityState state) =>
        report.Capabilities.Should().Contain(fact =>
            fact.Id == id &&
            fact.AppliesTo == appliesTo &&
            fact.State == state);

    private static AppleVirtualizationHelperEnvelope HostResponse(
        AppleVirtualizationHelperOperation operation,
        RuntimeHostPhase phase,
        ResourcePhase? resourcePhase = null) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.HostResponseSchema,
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = "runtime-host-1",
                HostPhase = phase,
                Phase = resourcePhase ?? PhaseFor(phase),
                GuestControlReachable = false,
            },
        };

    private static AppleVirtualizationHelperEnvelope GuestAgentReadinessResponse() =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.GuestAgentReadinessResponseSchema,
            GuestAgentReadinessProbeResponse = new AppleVirtualizationGuestAgentReadinessProbeResponse
            {
                HostId = "runtime-host-1",
                State = AppleVirtualizationGuestAgentReadinessState.Ready,
                TransportState = AppleVirtualizationGuestAgentTransportState.Connected,
                VmRunning = true,
                TransportConnected = true,
                VerifiedReady = true,
                ProtocolVersion = "1.0",
                AgentVersion = "0.1.0",
                GuestBootId = "boot-1",
                GuestBootGeneration = 1,
                GuestAgentGeneration = 1,
            },
        };

    private static ResourcePhase PhaseFor(RuntimeHostPhase phase) =>
        phase switch
        {
            RuntimeHostPhase.Ready or RuntimeHostPhase.Stopped => ResourcePhase.Ready,
            RuntimeHostPhase.Degraded => ResourcePhase.Degraded,
            RuntimeHostPhase.Deleted => ResourcePhase.Deleted,
            RuntimeHostPhase.Failed => ResourcePhase.Failed,
            _ => ResourcePhase.Reconciling,
        };

    private static AppleVirtualizationHelperEnvelope ProjectionResponse(
        AppleVirtualizationHelperOperation operation,
        string projectionId,
        ContentProjectionPhase phase,
        Condition? condition = null)
    {
        bool verified = condition is
        {
            Type: AppleVirtualizationContentProjectionProvider.GuestMountVerifiedCondition,
            Status: ConditionStatus.True,
        };

        return new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionResponseSchema,
            ProjectionStatusResponse = new AppleVirtualizationProjectionStatusResponse
            {
                ProjectionId = projectionId,
                ProjectionPhase = phase,
                EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                EffectiveCoherence = CoherenceClass.CloseToOpen,
                GuestAgentReady = verified,
                HostShareConfigured = true,
                FrameworkShareAccepted = true,
                VerifiedByGuestAgent = verified,
                GuestProjectionStatus = verified
                    ? new AppleVirtualizationGuestAgentProjectionStatus
                    {
                        ProjectionId = projectionId,
                        GuestPath = "/workspace",
                        Tag = "hpdprojection",
                        Mounted = true,
                        GuestMountVerified = true,
                        HostShareState = AppleVirtualizationGuestAgentProjectionHostShareState.HostShareConfigured,
                        FrameworkShareState = AppleVirtualizationGuestAgentProjectionFrameworkShareState.Accepted,
                        VerificationState = AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse,
                        ExpectedGuestPath = "/workspace",
                        ActualGuestPath = "/workspace",
                        RequestedAccessMode = AccessMode.ReadOnly,
                        EffectiveAccessMode = AccessMode.ReadOnly,
                        ProjectionPhase = phase,
                        EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                        EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                        EffectiveCoherence = CoherenceClass.CloseToOpen,
                        EffectiveCache = CacheBehavior.ReadCache,
                        Conditions = condition is null ? Array.Empty<Condition>() : [condition.Value],
                    }
                    : null,
                Conditions = condition is null ? Array.Empty<Condition>() : [condition.Value],
            },
        };
    }

    private static Condition GuestMountVerified(ResourceGeneration generation) =>
        new(
            AppleVirtualizationContentProjectionProvider.GuestMountVerifiedCondition,
            ConditionStatus.True,
            "Mounted",
            "Guest mount was verified by fake helper.",
            DateTimeOffset.UtcNow,
            generation);

    private static AppleVirtualizationHelperEnvelope UnitResponse(
        AppleVirtualizationHelperOperation operation,
        ExecutionUnitPhase phase,
        string? workingDirectory = null) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.UnitResponseSchema,
            UnitStatusResponse = new AppleVirtualizationUnitStatusResponse
            {
                UnitId = "unit-1",
                UnitPhase = phase,
                WorkingDirectory = workingDirectory,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessStatus(
        AppleVirtualizationHelperOperation operation,
        string processId,
        ProcessInvocationPhase phase) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = processId,
                ProcessPhase = phase,
                IoState = ProcessIoState.Open,
                ProviderProcessId = "guest-" + processId,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessExited(string processId, int exitCode) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.ProcessWait,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = processId,
                ProcessPhase = ProcessInvocationPhase.Exited,
                IoState = ProcessIoState.Closed,
                ProviderProcessId = "guest-" + processId,
                Result = new ProcessInvocationResult
                {
                    ProcessId = new ResourceId<ProcessInvocation>(processId),
                    ProviderProcessId = "guest-" + processId,
                    ExitCode = exitCode,
                    CompletionKind = ProcessCompletionKind.Exited,
                    StartedAt = DateTimeOffset.UtcNow,
                    ExitedAt = DateTimeOffset.UtcNow,
                    Output = new ProcessCapturedOutput
                    {
                        Stdout = new ProcessStreamOutput(),
                        Stderr = new ProcessStreamOutput(),
                        OutputDrainTimeout = ProcessInvocationPolicy.Default.OutputDrainTimeout,
                    },
                },
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
            EventKind = AppleVirtualizationHelperEventKind.ProcessOutput,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            ProcessOutputEvent = new AppleVirtualizationProcessOutputEvent
            {
                ProcessId = processId,
                Stream = stream,
                Sequence = 1,
                ObservedAt = DateTimeOffset.UtcNow,
                Bytes = bytes,
                Flags = final ? ProcessOutputChunkFlags.Final : ProcessOutputChunkFlags.None,
            },
        };

    private sealed class RecordingProcessOutputSink : IProcessOutputSink
    {
        public List<ProcessOutputChunk> Chunks { get; } = [];

        public ValueTask OnOutputAsync(ProcessOutputChunk chunk, CancellationToken cancellationToken = default)
        {
            Chunks.Add(chunk);
            return ValueTask.CompletedTask;
        }
    }
}
