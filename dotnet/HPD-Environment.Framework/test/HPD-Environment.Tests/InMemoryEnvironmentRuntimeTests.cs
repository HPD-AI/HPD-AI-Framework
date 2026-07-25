using System.Buffers;
using System.Diagnostics;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

namespace HPD.Environment.Runtime.Tests;

public sealed class InMemoryEnvironmentRuntimeTests
{
    [Fact]
    public async Task Registry_registers_provider_families_and_reports_capabilities()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();

        IReadOnlyList<ProviderDescriptor> providers = await registry.ListAsync();
        ProviderCapabilityReport report = await registry.GetCapabilitiesAsync(InMemoryEnvironmentProvider.InMemoryProviderId);

        Assert.Single(providers);
        Assert.NotEmpty(registry.RuntimeHostProviders);
        Assert.NotEmpty(registry.ExecutionUnitProviders);
        Assert.NotEmpty(registry.ProcessProviders);
        Assert.NotEmpty(registry.FunctionSandboxProviders);
        Assert.NotEmpty(registry.ArtifactProviders);
        Assert.NotEmpty(registry.NetworkProviders);
        Assert.Contains(report.Capabilities, fact => fact.AppliesTo == ProviderContractKind.RuntimeHost && fact.State == CapabilityState.Supported);
        Assert.Contains(report.PreflightChecks, check => check.State == PreflightCheckState.Passed);
    }

    [Fact]
    public async Task Planner_selects_provider_for_required_runtime_contracts_and_validates_plan()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var planner = new DefaultRuntimePlanner(registry, registry);

        RuntimePlan plan = await planner.PlanAsync(new RuntimePlanRequest
        {
            TopologyPolicy = new RuntimeTopologyPolicy { Mode = RuntimeTopologyMode.OneHostPerRuntime },
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
        });
        RuntimePlanValidationResult validation = await planner.ValidateAsync(plan);

        Assert.True(validation.IsSupported);
        Assert.Empty(plan.UnsupportedReasons);
        Assert.Single(plan.Activations);
        Assert.Contains(plan.Providers, provider => provider.ContractKind == ProviderContractKind.ProcessInvocation);
        Assert.Contains(plan.CapabilityCoverage, coverage => coverage.State == CapabilityState.Supported);
    }

    [Fact]
    public async Task Planner_returns_unsupported_reason_when_required_contracts_are_missing()
    {
        var registry = new EnvironmentProviderRegistry();
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
        var registry = new EnvironmentProviderRegistry();
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
            var registry = new EnvironmentProviderRegistry();
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
            var registry = new EnvironmentProviderRegistry();
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
        var registry = new EnvironmentProviderRegistry();
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
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);

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
            Target = unit.Status.Handle!.Value,
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
    public async Task Runtime_owns_engine_unit_and_authority_lifecycle_operations()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(
            host.Metadata.Id,
            host.Metadata.Scope,
            host.Metadata.Generation);
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                Host = hostRef,
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                PreferredHost = hostRef,
            });
        EngineAuthorityBindingPlan authorityPlan = await runtime.PlanEngineAuthorityBindingAsync(
            new EngineAuthorityBindingRequest
            {
                Engine = new ResourceRef<EngineControlPlane>(
                    engine.Metadata.Id,
                    engine.Metadata.Scope,
                    engine.Metadata.Generation),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = unit.Status.Handle!.Value,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine.sock"),
            });
        Assert.True(authorityPlan.Accepted);
        ResourceSnapshot<AuthorityBinding, AuthorityBindingSpec, AuthorityBindingStatus> authority =
            await runtime.EnsureEngineAuthorityBindingAsync(authorityPlan);

        await runtime.RevokeAuthorityBindingAsync(new ResourceRef<AuthorityBinding>(
            authority.Metadata.Id,
            authority.Metadata.Scope,
            authority.Metadata.Generation));
        await runtime.DeleteExecutionUnitAsync(new ResourceRef<ExecutionUnit>(
            unit.Metadata.Id,
            unit.Metadata.Scope,
            unit.Metadata.Generation));

        Assert.Equal(EngineControlPlanePhase.Ready, engine.Status.EnginePhase);
        Assert.Equal(AuthorityBindingPhase.Projected, authority.Status.BindingPhase);
    }

    [Fact]
    public async Task Concurrent_host_ensure_is_serialized_and_preserves_one_snapshot_identity()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        var spec = new RuntimeHostSpec
        {
            Platform = new PlatformSpec("linux", "x64"),
        };

        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>[] results =
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
                runtime.EnsureHostAsync(spec).AsTask()));

        Assert.Single(results.Select(result => result.Metadata.Id).Distinct());
        Assert.Single(results.Select(result => result.Metadata.Generation).Distinct());
        Assert.All(results, result => Assert.Equal(RuntimeHostPhase.Ready, result.Status.HostPhase));
    }

    [Fact]
    public async Task Reconciled_execution_unit_retains_identity_and_advances_only_for_material_change()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(
            host.Metadata.Id,
            host.Metadata.Scope,
            host.Metadata.Generation);
        var initialSpec = new ExecutionUnitSpec
        {
            ReconciliationKey = new ExecutionUnitIdentityKey("workload-1"),
            PreferredHost = hostRef,
            Network = new ExecutionUnitNetworkSpec { Hostname = "workload-1" },
        };

        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> first =
            await runtime.EnsureExecutionUnitAsync(initialSpec);
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> repeated =
            await runtime.EnsureExecutionUnitAsync(initialSpec);
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> changed =
            await runtime.EnsureExecutionUnitAsync(initialSpec with
            {
                Network = new ExecutionUnitNetworkSpec { Hostname = "workload-1-new" },
            });

        Assert.Equal(first.Metadata.Id, repeated.Metadata.Id);
        Assert.Equal(first.Metadata.Generation, repeated.Metadata.Generation);
        Assert.Equal(first.Metadata.Id, changed.Metadata.Id);
        Assert.True(changed.Metadata.Generation.Value > repeated.Metadata.Generation.Value);
        Assert.Equal("workload-1-new", changed.Spec.Network.Hostname);

        IReadOnlyList<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>> listed =
            await runtime.ListExecutionUnitsAsync();
        Assert.Single(listed);
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> observed =
            await runtime.GetExecutionUnitAsync(new ResourceRef<ExecutionUnit>(
                changed.Metadata.Id,
                changed.Metadata.Scope,
                changed.Metadata.Generation));
        Assert.Equal(changed, observed);
    }

    [Fact]
    public async Task Concurrent_reconciled_execution_unit_ensure_creates_one_logical_resource_and_delete_releases_key()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(
            host.Metadata.Id,
            host.Metadata.Scope,
            host.Metadata.Generation);
        var spec = new ExecutionUnitSpec
        {
            ReconciliationKey = new ExecutionUnitIdentityKey("concurrent-workload"),
            PreferredHost = hostRef,
        };

        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>[] ensured =
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
                runtime.EnsureExecutionUnitAsync(spec).AsTask()));

        Assert.Single(ensured.Select(snapshot => snapshot.Metadata.Id).Distinct());
        Assert.Single(ensured.Select(snapshot => snapshot.Metadata.Generation).Distinct());
        Assert.Single(await runtime.ListExecutionUnitsAsync());

        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> accepted = ensured[0];
        await runtime.DeleteExecutionUnitAsync(new ResourceRef<ExecutionUnit>(
            accepted.Metadata.Id,
            accepted.Metadata.Scope,
            accepted.Metadata.Generation));
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> recreated =
            await runtime.EnsureExecutionUnitAsync(spec);

        Assert.NotEqual(accepted.Metadata.Id, recreated.Metadata.Id);
    }

    [Fact]
    public async Task Execution_unit_get_and_list_refresh_provider_status()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-observation");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        var spec = new ExecutionUnitSpec
        {
            ReconciliationKey = new ExecutionUnitIdentityKey("observed-workload"),
            PreferredHost = new ResourceRef<RuntimeHost>(
                host.Metadata.Id,
                host.Metadata.Scope,
                host.Metadata.Generation),
        };
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(spec);
        var process = new ResourceRef<ProcessInvocation>(
            new ResourceId<ProcessInvocation>("provider-process"),
            unit.Metadata.Scope,
            unit.Metadata.Generation);
        provider.UnitStatusOverride = unit.Status with
        {
            Phase = ResourcePhase.Ready,
            UnitPhase = ExecutionUnitPhase.Running,
            ActiveProcesses = [process],
        };

        ResourceRef<ExecutionUnit> reference = new(
            unit.Metadata.Id,
            unit.Metadata.Scope,
            unit.Metadata.Generation);
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> observed =
            await runtime.GetExecutionUnitAsync(reference);
        IReadOnlyList<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>> listed =
            await runtime.ListExecutionUnitsAsync();

        Assert.Equal(ExecutionUnitPhase.Running, observed.Status.UnitPhase);
        Assert.Equal(process, Assert.Single(observed.Status.ActiveProcesses));
        Assert.Equal(ExecutionUnitPhase.Running, Assert.Single(listed).Status.UnitPhase);
        Assert.True(provider.Calls.Count(call => call == "unit-status") >= 2);
    }

    [Fact]
    public async Task Execution_unit_observation_timeout_retains_degraded_owned_snapshot()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-observation-timeout")
        {
            IgnoreUnitObservationCancellation = true,
        };
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(
            registry,
            executionUnitObservationTimeout: TimeSpan.FromMilliseconds(25));
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                ReconciliationKey = new ExecutionUnitIdentityKey("timeout-workload"),
                PreferredHost = new ResourceRef<RuntimeHost>(
                    host.Metadata.Id,
                    host.Metadata.Scope,
                    host.Metadata.Generation),
            });

        long started = Stopwatch.GetTimestamp();
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> observed =
            await runtime.GetExecutionUnitAsync(new ResourceRef<ExecutionUnit>(
                unit.Metadata.Id,
                unit.Metadata.Scope,
                unit.Metadata.Generation));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Observation took {elapsed}.");
        Assert.Equal(ResourcePhase.Degraded, observed.Status.Phase);
        Assert.Contains(
            observed.Status.Diagnostics,
            diagnostic => diagnostic.Code.Value == "hpd.environment.execution-unit.observe-timeout");
        provider.IgnoreUnitObservationCancellation = false;
        Assert.Single(await runtime.ListExecutionUnitsAsync());
    }

    [Fact]
    public async Task Execution_unit_observation_propagates_caller_cancellation_when_provider_ignores_it()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-observation-cancel")
        {
            IgnoreUnitObservationCancellation = true,
        };
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(
            registry,
            executionUnitObservationTimeout: TimeSpan.FromSeconds(5));
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                ReconciliationKey = new ExecutionUnitIdentityKey("cancel-workload"),
                PreferredHost = new ResourceRef<RuntimeHost>(
                    host.Metadata.Id,
                    host.Metadata.Scope,
                    host.Metadata.Generation),
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.GetExecutionUnitAsync(
                new ResourceRef<ExecutionUnit>(
                    unit.Metadata.Id,
                    unit.Metadata.Scope,
                    unit.Metadata.Generation),
                cancellation.Token).AsTask());

        provider.IgnoreUnitObservationCancellation = false;
        Assert.Single(await runtime.ListExecutionUnitsAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("provider")]
    [InlineData("token")]
    [InlineData("schema")]
    [InlineData("generation")]
    public async Task Execution_unit_observation_rejects_missing_or_mismatched_namespace_handle(
        string mismatch)
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-namespace");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        var acceptedNamespace = new ProviderOpaqueHandle(
            provider.ProviderId,
            "namespace-1",
            new SchemaId("hpd.test.namespace.v1"),
            Generation: 7);
        provider.UnitNamespaceHandleOnEnsure = acceptedNamespace;
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                ReconciliationKey = new ExecutionUnitIdentityKey($"namespace-{mismatch}"),
                PreferredHost = new ResourceRef<RuntimeHost>(
                    host.Metadata.Id,
                    host.Metadata.Scope,
                    host.Metadata.Generation),
            });
        ProviderOpaqueHandle? observedNamespace = mismatch switch
        {
            "missing" => null,
            "provider" => acceptedNamespace with { ProviderId = new ProviderId("other-provider") },
            "token" => acceptedNamespace with { Token = "namespace-2" },
            "schema" => acceptedNamespace with { SchemaId = new SchemaId("hpd.test.namespace.v2") },
            "generation" => acceptedNamespace with { Generation = 8 },
            _ => throw new InvalidOperationException(),
        };
        provider.UnitStatusOverride = unit.Status with { NamespaceHandle = observedNamespace };

        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> observed =
            await runtime.GetExecutionUnitAsync(new ResourceRef<ExecutionUnit>(
                unit.Metadata.Id,
                unit.Metadata.Scope,
                unit.Metadata.Generation));

        Assert.Equal(ResourcePhase.Degraded, observed.Status.Phase);
        Assert.Equal(acceptedNamespace, observed.Status.NamespaceHandle);
        Assert.Contains(
            observed.Status.Diagnostics,
            diagnostic => diagnostic.Code.Value ==
                "hpd.environment.execution-unit.observe-namespace-handle-mismatch");
    }

    [Fact]
    public async Task Material_execution_unit_change_is_rejected_while_authority_is_active()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(
            host.Metadata.Id,
            host.Metadata.Scope,
            host.Metadata.Generation);
        var spec = new ExecutionUnitSpec
        {
            ReconciliationKey = new ExecutionUnitIdentityKey("protected-workload"),
            PreferredHost = hostRef,
        };
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(spec);
        await runtime.EnsureAuthorityBindingAsync(new AuthorityBindingSpec
        {
            Kind = AuthorityBindingKind.GuestCapability,
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.ProviderCapability,
                Locus = BoundaryLocus.Provider,
            },
            Target = new AuthorityBindingTarget(
                AuthorityTargetKind.ExecutionUnit,
                Unit: unit.Status.Handle),
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.EnvironmentReference,
                EnvironmentVariableName = "HPD_AUTHORITY",
            },
            Policy = new AuthorityBindingPolicy
            {
                AuthorityClass = SensitiveAuthorityClass.ProviderDefined,
                EffectiveAuthorityClass = SensitiveAuthorityClass.ProviderDefined,
            },
        });

        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> rejected =
            await runtime.EnsureExecutionUnitAsync(spec with
            {
                SecurityPolicy = new SecurityPolicy
                {
                    AllowAuthorityBindings = true,
                },
            });

        Assert.Equal(ResourceReconciliationOutcome.ImmutableConflict, rejected.Status.ReconciliationOutcome);
        Assert.Equal(unit.Metadata.Generation, rejected.Metadata.Generation);
        Assert.Equal(unit.Spec, rejected.Spec);
        Assert.Contains(
            rejected.Status.Diagnostics,
            diagnostic => diagnostic.Code.Value ==
                "hpd.environment.execution-unit.replacement-dependents-active");
    }

    [Fact]
    public async Task Material_host_change_is_rejected_while_keyed_unit_exists_without_orphaning_it()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        var originalSpec = new RuntimeHostSpec
        {
            Platform = new PlatformSpec("linux", "x64"),
            Capacity = new ResourceQuotaPolicy { CpuCores = 2 },
        };
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(originalSpec);
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                ReconciliationKey = new ExecutionUnitIdentityKey("host-bound-workload"),
                PreferredHost = new ResourceRef<RuntimeHost>(
                    host.Metadata.Id,
                    host.Metadata.Scope,
                    host.Metadata.Generation),
            });

        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> rejected =
            await runtime.EnsureHostAsync(originalSpec with
            {
                Capacity = new ResourceQuotaPolicy { CpuCores = 4 },
            });

        Assert.Equal(ResourceReconciliationOutcome.ImmutableConflict, rejected.Status.ReconciliationOutcome);
        Assert.Equal(host.Metadata.Generation, rejected.Metadata.Generation);
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> retained =
            Assert.Single(await runtime.ListExecutionUnitsAsync());
        Assert.Equal(unit.Metadata.Id, retained.Metadata.Id);
        Assert.Equal(host.Metadata.Generation, retained.Spec.PreferredHost?.Generation);
    }

    [Fact]
    public async Task Delete_host_clears_runtime_owned_snapshot_before_recreation()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        var firstSpec = new RuntimeHostSpec
        {
            Platform = new PlatformSpec("linux", "x64"),
        };

        await runtime.EnsureHostAsync(firstSpec);
        await runtime.DeleteHostAsync();
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> recreated =
            await runtime.EnsureHostAsync(firstSpec with
            {
                Platform = new PlatformSpec("linux", "arm64"),
            });

        Assert.Equal("arm64", recreated.Spec.Platform.Architecture);
        Assert.Equal(RuntimeHostPhase.Ready, recreated.Status.HostPhase);
    }

    [Fact]
    public async Task Engine_reconciliation_has_stable_identity_generation_and_host_ownership()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(
            host.Metadata.Id,
            host.Metadata.Scope,
            host.Metadata.Generation);
        var spec = new EngineControlPlaneSpec
        {
            Kind = EngineControlPlaneKind.DockerCompatible,
            Api = EngineApiKind.DockerCompatible,
            Host = hostRef,
        };

        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> first =
            await runtime.EnsureEngineControlPlaneAsync(spec);
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> second =
            await runtime.EnsureEngineControlPlaneAsync(spec);
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus>[] concurrent =
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
                runtime.EnsureEngineControlPlaneAsync(spec).AsTask()));
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> changed =
            await runtime.EnsureEngineControlPlaneAsync(spec with
            {
                ImageStore = EngineImageStoreMode.Remote,
            });
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> independentApi =
            await runtime.EnsureEngineControlPlaneAsync(spec with
            {
                Kind = EngineControlPlaneKind.Containerd,
                Api = EngineApiKind.ContainerdApi,
            });

        Assert.Equal(first.Metadata.Id, second.Metadata.Id);
        Assert.Equal(first.Metadata.Generation, second.Metadata.Generation);
        Assert.All(concurrent, snapshot => Assert.Equal(first.Metadata.Id, snapshot.Metadata.Id));
        Assert.All(concurrent, snapshot => Assert.Equal(first.Metadata.Generation, snapshot.Metadata.Generation));
        Assert.Equal(first.Metadata.Id, changed.Metadata.Id);
        Assert.True(changed.Metadata.Generation.Value > first.Metadata.Generation.Value);
        Assert.NotEqual(first.Metadata.Id, independentApi.Metadata.Id);

        await runtime.DeleteHostAsync();
        await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
            runtime.PlanEngineAuthorityBindingAsync(new EngineAuthorityBindingRequest
            {
                Engine = new ResourceRef<EngineControlPlane>(
                    first.Metadata.Id,
                    first.Metadata.Scope,
                    first.Metadata.Generation),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "stale"),
                TargetSocketPath = new UnixSocketPath("/run/stale.sock"),
            }).AsTask());

        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> recreatedHost =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> recreatedEngine =
            await runtime.EnsureEngineControlPlaneAsync(spec with
            {
                Host = new ResourceRef<RuntimeHost>(
                    recreatedHost.Metadata.Id,
                    recreatedHost.Metadata.Scope,
                    recreatedHost.Metadata.Generation),
            });
        Assert.NotEqual(first.Metadata.Id, recreatedEngine.Metadata.Id);
    }

    [Fact]
    public async Task Repeated_engine_ensure_passes_the_last_accepted_status_as_observed()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-engine-observed");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        var spec = new EngineControlPlaneSpec
        {
            Kind = EngineControlPlaneKind.DockerCompatible,
            Api = EngineApiKind.DockerCompatible,
            Host = new ResourceRef<RuntimeHost>(
                host.Metadata.Id,
                host.Metadata.Scope,
                host.Metadata.Generation),
        };

        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> first =
            await runtime.EnsureEngineControlPlaneAsync(spec);
        Assert.Null(provider.FirstObservedEngine);
        _ = await runtime.EnsureEngineControlPlaneAsync(spec);

        Assert.NotNull(provider.LastObservedEngine);
        Assert.Equal(first.Metadata.Generation, provider.LastObservedEngine!.ObservedGeneration);
    }

    [Fact]
    public async Task Engine_authority_is_generation_bound_and_blocks_engine_reconfiguration()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-engine-authority-generation");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(host.Metadata.Id, host.Metadata.Scope, host.Metadata.Generation);
        var engineSpec = new EngineControlPlaneSpec
        {
            Kind = EngineControlPlaneKind.DockerCompatible,
            Api = EngineApiKind.DockerCompatible,
            Host = hostRef,
        };
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(engineSpec);
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec { PreferredHost = hostRef });
        EngineAuthorityBindingPlan plan = await runtime.PlanEngineAuthorityBindingAsync(
            new EngineAuthorityBindingRequest
            {
                Engine = new ResourceRef<EngineControlPlane>(
                    engine.Metadata.Id,
                    engine.Metadata.Scope,
                    engine.Metadata.Generation),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = unit.Status.Handle!.Value,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine.sock"),
            });

        Assert.Equal(plan.SourceEngine, new ResourceRef<EngineControlPlane>(
            engine.Metadata.Id,
            engine.Metadata.Scope,
            engine.Metadata.Generation));
        RuntimeResourceOwnershipException bypass =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureAuthorityBindingAsync(plan.Spec!).AsTask());
        Assert.Equal("hpd.environment.engine-authority.plan-required", bypass.Diagnostic.Code.Value);
        ResourceSnapshot<AuthorityBinding, AuthorityBindingSpec, AuthorityBindingStatus> authority =
            await runtime.EnsureEngineAuthorityBindingAsync(plan);
        int engineEnsureCalls = provider.Calls.Count(call => call == "engine-ensure");

        RuntimeResourceOwnershipException conflict =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureEngineControlPlaneAsync(engineSpec with
                {
                    AuthorityMode = EngineAuthorityMode.Rootful,
                }).AsTask());
        Assert.Equal("hpd.environment.engine.reconfiguration-authority-active", conflict.Diagnostic.Code.Value);
        Assert.Equal(engineEnsureCalls, provider.Calls.Count(call => call == "engine-ensure"));

        await runtime.RevokeAuthorityBindingAsync(new ResourceRef<AuthorityBinding>(
            authority.Metadata.Id,
            authority.Metadata.Scope,
            authority.Metadata.Generation));
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> changed =
            await runtime.EnsureEngineControlPlaneAsync(engineSpec with
            {
                AuthorityMode = EngineAuthorityMode.Rootful,
            });
        Assert.True(changed.Metadata.Generation.Value > engine.Metadata.Generation.Value);
    }

    [Fact]
    public async Task Stale_engine_authority_plan_is_rejected_before_projection()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-engine-authority-stale-plan");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(host.Metadata.Id, host.Metadata.Scope, host.Metadata.Generation);
        var engineSpec = new EngineControlPlaneSpec
        {
            Kind = EngineControlPlaneKind.DockerCompatible,
            Api = EngineApiKind.DockerCompatible,
            Host = hostRef,
        };
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(engineSpec);
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec { PreferredHost = hostRef });
        EngineAuthorityBindingPlan stalePlan = await runtime.PlanEngineAuthorityBindingAsync(
            new EngineAuthorityBindingRequest
            {
                Engine = new ResourceRef<EngineControlPlane>(
                    engine.Metadata.Id,
                    engine.Metadata.Scope,
                    engine.Metadata.Generation),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = unit.Status.Handle!.Value,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine.sock"),
            });
        _ = await runtime.EnsureEngineControlPlaneAsync(engineSpec with
        {
            ImageStore = EngineImageStoreMode.Remote,
        });

        RuntimeResourceOwnershipException stale =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureEngineAuthorityBindingAsync(stalePlan).AsTask());
        Assert.Equal("hpd.environment.engine-authority.plan-unknown-or-consumed", stale.Diagnostic.Code.Value);
        Assert.DoesNotContain("authority-ensure", provider.Calls);
    }

    [Fact]
    public async Task Engine_authority_plans_reject_alteration_unknown_ids_and_reuse()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-engine-authority-plan-integrity");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(host.Metadata.Id, host.Metadata.Scope, host.Metadata.Generation);
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> docker =
            await runtime.EnsureEngineControlPlaneAsync(new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                Host = hostRef,
            });
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> containerd =
            await runtime.EnsureEngineControlPlaneAsync(new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.Containerd,
                Api = EngineApiKind.ContainerdApi,
                Host = hostRef,
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec { PreferredHost = hostRef });
        EngineAuthorityBindingPlan approved = await runtime.PlanEngineAuthorityBindingAsync(
            new EngineAuthorityBindingRequest
            {
                Engine = Ref(docker.Metadata),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = unit.Status.Handle!.Value,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine.sock"),
            });

        RuntimeResourceOwnershipException alteredEngine =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureEngineAuthorityBindingAsync(approved with
                {
                    SourceEngine = Ref(containerd.Metadata),
                }).AsTask());
        Assert.Equal("hpd.environment.engine-authority.plan-altered", alteredEngine.Diagnostic.Code.Value);

        RuntimeResourceOwnershipException alteredSpec =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureEngineAuthorityBindingAsync(approved with
                {
                    Spec = approved.Spec! with { AuditLabel = "altered-after-approval" },
                }).AsTask());
        Assert.Equal("hpd.environment.engine-authority.plan-altered", alteredSpec.Diagnostic.Code.Value);

        RuntimeResourceOwnershipException unknown =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureEngineAuthorityBindingAsync(approved with
                {
                    PlanId = new EngineAuthorityBindingPlanId("unknown"),
                }).AsTask());
        Assert.Equal("hpd.environment.engine-authority.plan-unknown-or-consumed", unknown.Diagnostic.Code.Value);

        _ = await runtime.EnsureEngineAuthorityBindingAsync(approved);
        RuntimeResourceOwnershipException reused =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureEngineAuthorityBindingAsync(approved).AsTask());
        Assert.Equal("hpd.environment.engine-authority.plan-unknown-or-consumed", reused.Diagnostic.Code.Value);

        EngineAuthorityBindingPlan invalidatedByDelete = await runtime.PlanEngineAuthorityBindingAsync(
            new EngineAuthorityBindingRequest
            {
                Engine = Ref(docker.Metadata),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = unit.Status.Handle.Value,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine-2.sock"),
            });
        _ = await runtime.DeleteHostAsync();
        RuntimeResourceOwnershipException deletedPlan =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureEngineAuthorityBindingAsync(invalidatedByDelete).AsTask());
        Assert.Equal(
            "hpd.environment.engine-authority.plan-unknown-or-consumed",
            deletedPlan.Diagnostic.Code.Value);
    }

    [Fact]
    public async Task Engine_authority_plan_expiry_is_enforced_by_runtime_time()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-engine-authority-plan-expiry");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-25T12:00:00Z"));
        var runtime = new InMemoryEnvironmentRuntime(
            registry,
            timeProvider: time,
            engineAuthorityPlanLifetime: TimeSpan.FromMinutes(1));
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = Ref(host.Metadata);
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                Host = hostRef,
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec { PreferredHost = hostRef });
        EngineAuthorityBindingPlan plan = await runtime.PlanEngineAuthorityBindingAsync(
            new EngineAuthorityBindingRequest
            {
                Engine = Ref(engine.Metadata),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = unit.Status.Handle!.Value,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine.sock"),
            });
        time.Advance(TimeSpan.FromMinutes(1));

        RuntimeResourceOwnershipException expired =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureEngineAuthorityBindingAsync(plan).AsTask());
        Assert.Equal("hpd.environment.engine-authority.plan-expired", expired.Diagnostic.Code.Value);
        Assert.DoesNotContain("authority-ensure", provider.Calls);
    }

    [Fact]
    public async Task Host_generation_tracks_nested_byte_content_and_identical_specs()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        RuntimeHostSpec firstSpec = HostSpecWithPayload([1, 2, 3, 4]);

        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> first =
            await runtime.EnsureHostAsync(firstSpec);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> identical =
            await runtime.EnsureHostAsync(HostSpecWithPayload([1, 2, 3, 4]));
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> changed =
            await runtime.EnsureHostAsync(HostSpecWithPayload([1, 2, 3, 5]));

        Assert.Equal(first.Metadata.Id, identical.Metadata.Id);
        Assert.Equal(first.Metadata.Generation, identical.Metadata.Generation);
        Assert.Equal(first.Metadata.Id, changed.Metadata.Id);
        Assert.True(changed.Metadata.Generation.Value > first.Metadata.Generation.Value);
        Assert.Equal(changed.Metadata.Generation, changed.Status.ObservedGeneration);
    }

    [Fact]
    public async Task Provider_neutral_immutable_conflict_preserves_last_accepted_snapshot()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-non-apple")
        {
            RejectArm64HostChange = true,
        };
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);

        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> accepted =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> rejected =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "arm64"),
            });
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> retried =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });

        Assert.Equal(ResourceReconciliationOutcome.ImmutableConflict, rejected.Status.ReconciliationOutcome);
        Assert.True(rejected.Metadata.Generation.Value > accepted.Metadata.Generation.Value);
        Assert.Equal(accepted.Metadata.Generation, retried.Metadata.Generation);
        Assert.Equal("x64", retried.Spec.Platform.Architecture);
        Assert.NotNull(provider.LastObservedHost);
    }

    [Fact]
    public async Task Runtime_routes_owned_resources_and_deletes_in_dependency_order()
    {
        var first = new RecordingRuntimeProvider("hpd.execution.test-first");
        var owner = new RecordingRuntimeProvider("hpd.execution.test-owner");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(first));
        registry.RegisterModule(new RecordingRuntimeProviderModule(owner));
        var runtime = new InMemoryEnvironmentRuntime(registry);

        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = owner.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(host.Metadata.Id, host.Metadata.Scope, host.Metadata.Generation);
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                Host = hostRef,
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec { PreferredHost = hostRef });
        EngineAuthorityBindingPlan plan = await runtime.PlanEngineAuthorityBindingAsync(
            new EngineAuthorityBindingRequest
            {
                Engine = new ResourceRef<EngineControlPlane>(
                    engine.Metadata.Id,
                    engine.Metadata.Scope,
                    engine.Metadata.Generation),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = unit.Status.Handle!.Value,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine.sock"),
            });
        Assert.True(plan.Accepted);
        _ = await runtime.EnsureEngineAuthorityBindingAsync(plan);
        owner.BlockProcessUntilCanceled = true;
        Task<ProcessInvocationResult> runningProcess = runtime.RunProcessAsync(
            new ProcessInvocationSpec
            {
                Target = unit.Status.Handle.Value,
                Command = new ProcessCommandSpec { FileName = "/bin/blocked" },
            }).AsTask();
        await owner.ProcessStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await runtime.DeleteHostAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runningProcess);

        Assert.Empty(first.Calls);
        Assert.Equal(
            ["host-ensure", "engine-ensure", "unit-ensure", "engine-authority-plan", "authority-ensure",
             "process-start", "process-stop", "finalize-content", "authority-revoke", "unit-delete",
             "engine-delete", "host-delete"],
            owner.Calls);
    }

    [Fact]
    public async Task Cleanup_failure_and_cancellation_preserve_recoverable_runtime_ownership()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-cleanup");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceRef<RuntimeHost> hostRef = new(host.Metadata.Id, host.Metadata.Scope, host.Metadata.Generation);
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                Host = hostRef,
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec { PreferredHost = hostRef });
        EngineAuthorityBindingPlan plan = await runtime.PlanEngineAuthorityBindingAsync(
            new EngineAuthorityBindingRequest
            {
                Engine = new ResourceRef<EngineControlPlane>(
                    engine.Metadata.Id,
                    engine.Metadata.Scope,
                    engine.Metadata.Generation),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = unit.Status.Handle!.Value,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine.sock"),
            });
        _ = await runtime.EnsureEngineAuthorityBindingAsync(plan);

        provider.FailAuthorityRevocation = true;
        RuntimeHostDeletionResult retained = await runtime.DeleteHostAsync();
        Assert.False(retained.Deleted);
        Assert.Equal(RuntimeHostPhase.Degraded, retained.RetainedHostStatus?.HostPhase);
        Assert.Contains(retained.Diagnostics, diagnostic =>
            diagnostic.Code.Value == "hpd.environment.runtime-cleanup.failed");
        Assert.DoesNotContain("host-delete", provider.Calls);

        provider.FailAuthorityRevocation = false;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.DeleteHostAsync(canceled.Token).AsTask());
        Assert.DoesNotContain("host-delete", provider.Calls);

        await runtime.DeleteHostAsync();
        Assert.Contains("host-delete", provider.Calls);
    }

    [Fact]
    public async Task Cleanup_failure_modes_are_distinct_and_truthful()
    {
        RuntimeHostDeletionResult bestEffort =
            await RunAuthorityCleanupFailureAsync(CleanupFailureMode.BestEffortRelease);
        Assert.True(bestEffort.Deleted);
        Assert.Contains(bestEffort.Diagnostics, diagnostic =>
            diagnostic.Code.Value == "hpd.environment.runtime-cleanup.failed");

        await Assert.ThrowsAsync<RuntimeCleanupException>(() =>
            RunAuthorityCleanupFailureAsync(CleanupFailureMode.FailOperation));
    }

    [Fact]
    public async Task Cleanup_timeout_is_bounded_and_retains_a_degraded_host()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-cleanup-timeout")
        {
            IgnoreProcessCancellation = true,
        };
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
                LifecyclePolicy = LifecyclePolicy.Default with
                {
                    Cleanup = CleanupPolicy.Default with
                    {
                        OverallTimeout = TimeSpan.FromMilliseconds(400),
                        OperationTimeout = TimeSpan.FromMilliseconds(75),
                    },
                },
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                PreferredHost = new ResourceRef<RuntimeHost>(
                    host.Metadata.Id,
                    host.Metadata.Scope,
                    host.Metadata.Generation),
            });
        Task<ProcessInvocationResult> process = runtime.RunProcessAsync(new ProcessInvocationSpec
        {
            Target = unit.Status.Handle!.Value,
            Command = new ProcessCommandSpec { FileName = "/bin/ignore-cancellation" },
        }).AsTask();
        await provider.ProcessStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        RuntimeHostDeletionResult result = await runtime.DeleteHostAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Deleted);
        Assert.Equal(RuntimeHostPhase.Degraded, result.RetainedHostStatus?.HostPhase);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code.Value == "hpd.environment.runtime-cleanup.timeout" &&
            diagnostic.Message.Contains("active process completion", StringComparison.Ordinal));
        Assert.DoesNotContain("host-delete", provider.Calls);

        provider.ReleaseIgnoredProcess.TrySetResult();
        _ = await process.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Provider_migration_and_delete_protection_fail_before_provider_operations()
    {
        var owner = new RecordingRuntimeProvider("hpd.execution.test-provider-owner");
        var other = new RecordingRuntimeProvider("hpd.execution.test-provider-other");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(owner));
        registry.RegisterModule(new RecordingRuntimeProviderModule(other));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        _ = await runtime.EnsureHostAsync(new RuntimeHostSpec
        {
            PreferredProvider = owner.ProviderId,
            Platform = new PlatformSpec("linux", "x64"),
            HostPolicy = RuntimeHostLifecyclePolicy.Default with { ProtectFromDelete = true },
        });
        int ownerCalls = owner.Calls.Count;

        RuntimeResourceOwnershipException migration =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.EnsureHostAsync(new RuntimeHostSpec
                {
                    PreferredProvider = other.ProviderId,
                    Platform = new PlatformSpec("linux", "x64"),
                }).AsTask());
        Assert.Equal(
            "hpd.environment.runtime-host.provider-migration-requires-replacement",
            migration.Diagnostic.Code.Value);
        Assert.Equal(ownerCalls, owner.Calls.Count);
        Assert.Empty(other.Calls);

        RuntimeResourceOwnershipException protectedDelete =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.DeleteHostAsync().AsTask());
        Assert.Equal("hpd.environment.runtime-host.delete-protected", protectedDelete.Diagnostic.Code.Value);
        Assert.Equal(ownerCalls, owner.Calls.Count);
    }

    [Fact]
    public async Task Unknown_resource_is_not_dispatched_to_an_arbitrary_provider()
    {
        var provider = new RecordingRuntimeProvider("hpd.execution.test-owner");
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);

        RuntimeResourceOwnershipException exception =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.DeleteExecutionUnitAsync(new ResourceRef<ExecutionUnit>(
                    new ResourceId<ExecutionUnit>("unknown"),
                    new ResourceScope("in-memory-runtime"),
                    new ResourceGeneration(99))).AsTask());

        Assert.Equal("hpd.environment.execution-unit.unknown", exception.Diagnostic.Code.Value);
        Assert.DoesNotContain("unit-delete", provider.Calls);

        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                PreferredHost = new ResourceRef<RuntimeHost>(
                    host.Metadata.Id,
                    host.Metadata.Scope,
                    host.Metadata.Generation),
            });
        RuntimeResourceOwnershipException stale =
            await Assert.ThrowsAsync<RuntimeResourceOwnershipException>(() =>
                runtime.DeleteExecutionUnitAsync(new ResourceRef<ExecutionUnit>(
                    unit.Metadata.Id,
                    unit.Metadata.Scope,
                    new ResourceGeneration(unit.Metadata.Generation.Value + 1))).AsTask());

        Assert.Equal("hpd.environment.resource.stale-or-mismatched", stale.Diagnostic.Code.Value);
        Assert.DoesNotContain("unit-delete", provider.Calls);
    }

    [Fact]
    public async Task Process_handle_streams_output_and_zero_timeout_returns_timed_out_result()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        var sink = new RecordingProcessOutputSink();
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                PreferredHost = new ResourceRef<RuntimeHost>(
                    host.Metadata.Id,
                    host.Metadata.Scope,
                    host.Metadata.Generation),
            });
        var spec = new ProcessInvocationSpec
        {
            Target = unit.Status.Handle!.Value,
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
    public async Task Runtime_passes_process_isolation_to_selected_process_provider()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                Platform = new PlatformSpec("linux", "x64"),
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                PreferredHost = new ResourceRef<RuntimeHost>(
                    host.Metadata.Id,
                    host.Metadata.Scope,
                    host.Metadata.Generation),
            });
        var spec = new ProcessInvocationSpec
        {
            Target = unit.Status.Handle!.Value,
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

        Assert.Same(spec.Isolation, handle.Spec.Isolation);
        Assert.Empty(handle.Spec.ProviderExtensions);
    }

    [Fact]
    public async Task Function_sandbox_resolves_guest_binary_invokes_function_and_reports_timeout_poison_status()
    {
        EnvironmentProviderRegistry registry = CreateRegistry();
        var runtime = new InMemoryEnvironmentRuntime(registry);
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
        EnvironmentProviderRegistry registry = CreateRegistry();
        InMemoryEnvironmentProvider provider = (InMemoryEnvironmentProvider)registry.ArtifactProviders[0];
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
        EnvironmentProviderRegistry registry = CreateRegistry();
        InMemoryEnvironmentProvider provider = (InMemoryEnvironmentProvider)registry.NetworkProviders[0];
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

    private static async Task<RuntimeHostDeletionResult> RunAuthorityCleanupFailureAsync(
        CleanupFailureMode failureMode)
    {
        var provider = new RecordingRuntimeProvider($"hpd.execution.test-cleanup-{failureMode}")
        {
            FailAuthorityRevocation = true,
        };
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new RecordingRuntimeProviderModule(provider));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider = provider.ProviderId,
                Platform = new PlatformSpec("linux", "x64"),
                LifecyclePolicy = LifecyclePolicy.Default with
                {
                    Cleanup = CleanupPolicy.Default with { FailureMode = failureMode },
                },
            });
        ResourceRef<RuntimeHost> hostRef = new(host.Metadata.Id, host.Metadata.Scope, host.Metadata.Generation);
        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                Host = hostRef,
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec { PreferredHost = hostRef });
        EngineAuthorityBindingPlan plan = await runtime.PlanEngineAuthorityBindingAsync(
            new EngineAuthorityBindingRequest
            {
                Engine = new ResourceRef<EngineControlPlane>(
                    engine.Metadata.Id,
                    engine.Metadata.Scope,
                    engine.Metadata.Generation),
                Api = EngineApiKind.DockerCompatible,
                TargetUnit = unit.Status.Handle!.Value,
                TargetSocketPath = new UnixSocketPath("/run/hpd/engine.sock"),
            });
        _ = await runtime.EnsureEngineAuthorityBindingAsync(plan);
        return await runtime.DeleteHostAsync();
    }

    private static EnvironmentProviderRegistry CreateRegistry()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new InMemoryEnvironmentProviderModule());
        return registry;
    }

    private static RuntimeHostSpec HostSpecWithPayload(byte[] payload) =>
        new()
        {
            Platform = new PlatformSpec("linux", "x64"),
            Bootstrap = new RuntimeHostBootstrapSpec
            {
                InitData =
                [
                    new RuntimeHostInitDataSpec(
                        RuntimeHostInitDataKind.GuestAgentConfig,
                        Data: new ProviderExtensionData(
                            InMemoryEnvironmentProvider.InMemoryProviderId,
                            new SchemaId("test.guest-agent-config"),
                            new ContentType("application/octet-stream"),
                            payload)),
                ],
            },
        };

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

    private static ResourceRef<T> Ref<T>(ResourceMetadata<T> metadata)
        where T : IExecutionResourceMarker =>
        new(metadata.Id, metadata.Scope, metadata.Generation);

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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
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

    private sealed class RecordingRuntimeProviderModule(RecordingRuntimeProvider provider) : IProviderModule
    {
        public ProviderDescriptor Descriptor { get; } = new()
        {
            Id = provider.ProviderId,
            DisplayName = "Recording runtime provider",
            ContractVersion = new SemanticVersion(1, 0, 0),
            ProviderVersion = new SemanticVersion(1, 0, 0),
            ContractKinds =
                ProviderContractKind.RuntimeHost |
                ProviderContractKind.ExecutionUnit |
                ProviderContractKind.ProcessInvocation |
                ProviderContractKind.ContentProjection |
                ProviderContractKind.AuthorityBinding |
                ProviderContractKind.EngineControlPlane,
            TrustLevel = ProviderTrustLevel.BuiltIn,
            DefaultActivationScope = ProviderActivationScope.Runtime,
            ActivationModels =
            [
                new ProviderActivationModel(
                    ProviderActivationKind.InProcess,
                    ProviderActivationScope.Runtime,
                    ProviderTransportKind.None),
            ],
        };

        public void Register(IProviderRegistrationBuilder builder)
        {
            builder.AddRuntimeHostProvider(provider);
            builder.AddExecutionUnitProvider(provider);
            builder.AddProcessProvider(provider);
            builder.AddContentProjectionProvider(provider);
            builder.AddAuthorityBindingProvider(provider);
            builder.AddEngineControlPlaneProvider(provider);
        }

        public void RegisterJsonTypes(IProviderJsonTypeRegistry registry)
        {
        }
    }

    private sealed class RecordingRuntimeProvider(string id) :
        IRuntimeHostProvider,
        IExecutionUnitProvider,
        IProcessProvider,
        IContentProjectionProvider,
        IAuthorityBindingProvider,
        IEngineControlPlaneProvider,
        IRuntimeFinalizationParticipant
    {
        private readonly InMemoryEnvironmentProvider _inner = new();

        public ProviderId ProviderId { get; } = new(id);
        public List<string> Calls { get; } = [];
        public bool RejectArm64HostChange { get; set; }
        public bool FailAuthorityRevocation { get; set; }
        public bool BlockProcessUntilCanceled { get; set; }
        public bool IgnoreProcessCancellation { get; set; }
        public bool IgnoreUnitObservationCancellation { get; set; }
        public ProviderOpaqueHandle? UnitNamespaceHandleOnEnsure { get; set; }
        public ExecutionUnitStatus? UnitStatusOverride { get; set; }
        private ExecutionUnitStatus? LastEnsuredUnitStatus { get; set; }
        public TaskCompletionSource ProcessStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseIgnoredProcess { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RuntimeHostStatus? LastObservedHost { get; private set; }
        public EngineControlPlaneStatus? LastObservedEngine { get; private set; }
        public EngineControlPlaneStatus? FirstObservedEngine { get; private set; }

        public async ValueTask<RuntimeHostStatus> EnsureAsync(
            ResourceMetadata<RuntimeHost> metadata,
            RuntimeHostSpec spec,
            RuntimeHostStatus? observed,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("host-ensure");
            LastObservedHost = observed;
            if (RejectArm64HostChange &&
                observed is not null &&
                string.Equals(spec.Platform.Architecture, "arm64", StringComparison.Ordinal))
            {
                return observed with
                {
                    Phase = ResourcePhase.Failed,
                    HostPhase = RuntimeHostPhase.Failed,
                    ReconciliationOutcome = ResourceReconciliationOutcome.ImmutableConflict,
                    Diagnostics =
                    [
                        new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Code = new DiagnosticCode("test.provider.immutable-conflict"),
                            Message = "The test provider rejected an immutable host change.",
                            ProviderId = ProviderId,
                        },
                    ],
                };
            }
            return await _inner.EnsureAsync(metadata, spec, observed, cancellationToken);
        }

        public ValueTask<RuntimeHostStatus> StopAsync(
            TargetHandle<RuntimeHost> host,
            StopPolicy policy,
            CancellationToken cancellationToken = default) =>
            _inner.StopAsync(host, policy, cancellationToken);

        public ValueTask DeleteAsync(
            ResourceRef<RuntimeHost> host,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("host-delete");
            return _inner.DeleteAsync(host, cancellationToken);
        }

        public ValueTask<RuntimeHostStatus> GetStatusAsync(
            TargetHandle<RuntimeHost> host,
            CancellationToken cancellationToken = default) =>
            _inner.GetStatusAsync(host, cancellationToken);

        public async ValueTask<ExecutionUnitStatus> EnsureAsync(
            ResourceMetadata<ExecutionUnit> metadata,
            ExecutionUnitSpec spec,
            ExecutionUnitStatus? observed,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("unit-ensure");
            ExecutionUnitStatus status =
                await _inner.EnsureAsync(metadata, spec, observed, cancellationToken);
            if (UnitNamespaceHandleOnEnsure is { } namespaceHandle)
            {
                status = status with { NamespaceHandle = namespaceHandle };
            }
            LastEnsuredUnitStatus = status;
            return status;
        }

        public ValueTask<ExecutionUnitStatus> StopAsync(
            TargetHandle<ExecutionUnit> unit,
            StopPolicy policy,
            CancellationToken cancellationToken = default) =>
            _inner.StopAsync(unit, policy, cancellationToken);

        public ValueTask DeleteAsync(
            ResourceRef<ExecutionUnit> unit,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("unit-delete");
            return _inner.DeleteAsync(unit, cancellationToken);
        }

        public async ValueTask<ExecutionUnitStatus> GetStatusAsync(
            TargetHandle<ExecutionUnit> unit,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("unit-status");
            if (IgnoreUnitObservationCancellation)
            {
                return await NeverCompletingUnitObservation.Task;
            }
            return UnitStatusOverride is { } status
                ? status
                : LastEnsuredUnitStatus ?? await _inner.GetStatusAsync(unit, cancellationToken);
        }

        private TaskCompletionSource<ExecutionUnitStatus> NeverCompletingUnitObservation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default) =>
            _inner.StartAsync(spec, output, cancellationToken);

        public async ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
        {
            if (IgnoreProcessCancellation)
            {
                Calls.Add("process-start");
                ProcessStarted.TrySetResult();
                await ReleaseIgnoredProcess.Task;
                Calls.Add("process-stop");
                return await _inner.RunAsync(spec, output, CancellationToken.None);
            }
            if (!BlockProcessUntilCanceled)
            {
                return await _inner.RunAsync(spec, output, cancellationToken);
            }

            Calls.Add("process-start");
            ProcessStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocking process unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                Calls.Add("process-stop");
                throw;
            }
        }

        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default) =>
            _inner.SignalAsync(process, signal, cancellationToken);

        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default) =>
            _inner.ResizeTerminalAsync(process, size, cancellationToken);

        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) =>
            _inner.WaitAsync(process, cancellationToken);

        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) =>
            _inner.ReadOutputAsync(process, cancellationToken);

        public ValueTask<ContentProjectionStatus> ProjectAsync(
            ResourceMetadata<ContentProjection> metadata,
            ContentProjectionSpec spec,
            TargetHandle<RuntimeHost>? host,
            TargetHandle<ExecutionUnit>? unit,
            CancellationToken cancellationToken = default) =>
            _inner.ProjectAsync(metadata, spec, host, unit, cancellationToken);

        public ValueTask EnumerateEntriesAsync(
            ResourceRef<ContentProjection> projection,
            IContentProjectionEntrySink sink,
            CancellationToken cancellationToken = default) =>
            _inner.EnumerateEntriesAsync(projection, sink, cancellationToken);

        public ValueTask<SyncResult> SyncAsync(
            TargetHandle<ContentProjection> projection,
            SyncRequest request,
            CancellationToken cancellationToken = default) =>
            _inner.SyncAsync(projection, request, cancellationToken);

        public ValueTask<FinalizationResult> FinalizeAsync(
            TargetHandle<ContentProjection> projection,
            FinalizationRequest request,
            IExecutionEventSink? events = null,
            CancellationToken cancellationToken = default) =>
            _inner.FinalizeAsync(projection, request, events, cancellationToken);

        public ValueTask ReleaseAsync(
            TargetHandle<ContentProjection> projection,
            CancellationToken cancellationToken = default) =>
            _inner.ReleaseAsync(projection, cancellationToken);

        public ValueTask<RuntimeFinalizationResult> FinalizeRuntimeAsync(
            RuntimeFinalizationRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("finalize-content");
            return ValueTask.FromResult(new RuntimeFinalizationResult
            {
                RuntimeScope = request.RuntimeScope,
            });
        }

        public async ValueTask<AuthorityBindingStatus> EnsureAuthorityBindingAsync(
            ResourceMetadata<AuthorityBinding> metadata,
            AuthorityBindingSpec spec,
            AuthorityBindingStatus? observed,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("authority-ensure");
            return await _inner.EnsureAuthorityBindingAsync(metadata, spec, observed, cancellationToken);
        }

        public ValueTask<AuthorityBindingStatus> GetStatusAsync(
            ResourceRef<AuthorityBinding> binding,
            CancellationToken cancellationToken = default) =>
            _inner.GetStatusAsync(binding, cancellationToken);

        public ValueTask RevokeAuthorityBindingAsync(
            ResourceRef<AuthorityBinding> binding,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("authority-revoke");
            if (FailAuthorityRevocation)
            {
                throw new InvalidOperationException("Injected authority revocation failure.");
            }
            return _inner.RevokeAuthorityBindingAsync(binding, cancellationToken);
        }

        public async ValueTask<EngineControlPlaneStatus> EnsureEngineControlPlaneAsync(
            ResourceMetadata<EngineControlPlane> metadata,
            EngineControlPlaneSpec spec,
            EngineControlPlaneStatus? observed,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("engine-ensure");
            if (Calls.Count(call => call == "engine-ensure") == 1)
            {
                FirstObservedEngine = observed;
            }
            LastObservedEngine = observed;
            return await _inner.EnsureEngineControlPlaneAsync(metadata, spec, observed, cancellationToken);
        }

        public async ValueTask<EngineAuthorityBindingPlan> PlanAuthorityBindingAsync(
            EngineControlPlaneStatus engine,
            EngineAuthorityBindingRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("engine-authority-plan");
            return await _inner.PlanAuthorityBindingAsync(engine, request, cancellationToken);
        }

        public ValueTask<EngineControlPlaneStatus> GetStatusAsync(
            ResourceRef<EngineControlPlane> engine,
            CancellationToken cancellationToken = default) =>
            _inner.GetStatusAsync(engine, cancellationToken);

        public ValueTask<EngineControlPlaneStatus> StopAsync(
            TargetHandle<EngineControlPlane> engine,
            StopPolicy policy,
            CancellationToken cancellationToken = default) =>
            _inner.StopAsync(engine, policy, cancellationToken);

        public ValueTask DeleteAsync(
            ResourceRef<EngineControlPlane> engine,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("engine-delete");
            return _inner.DeleteAsync(engine, cancellationToken);
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
