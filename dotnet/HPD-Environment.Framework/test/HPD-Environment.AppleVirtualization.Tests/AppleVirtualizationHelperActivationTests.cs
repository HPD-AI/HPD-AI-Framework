namespace HPD.Environment.AppleVirtualization.Tests;

using System.Diagnostics;
using FluentAssertions;
using HPD.Environment.AppleVirtualization.Activation;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Projections;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;

public sealed class AppleVirtualizationHelperActivationTests
{
    [Fact]
    public async Task Configured_hpd_vz_fake_launches_and_handshakes_through_provider_activation()
    {
        var registry = RegisterRealActivation(ResolveBuiltHelperPath(), ["--fake"]);

        ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus> activation =
            await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec());

        activation.Status.ActivationPhase.Should().Be(ProviderActivationPhase.Ready);
        activation.Status.Diagnostics.Should().BeEmpty();
        activation.Status.Components.Should().Contain(component =>
            component.Phase == ProviderComponentPhase.Ready &&
            component.Name.Contains("hpd-vz 0.1.0 protocol 1.0 generation 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Real_vm_boot_gate_rejects_fake_helper_before_activation_handshake()
    {
        var registry = RegisterRealActivation(ResolveBuiltHelperPath(), ["--fake"], enableRealVmBoot: true);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Failed);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RealModeRequiresNonFakeHelper" &&
            diagnostic.TargetPath == "HelperArguments");
        status.PreflightChecks.Should().Contain(check =>
            check.Name == AppleVirtualizationRealModePreconditions.HelperModeFact &&
            check.State == PreflightCheckState.RequiresRemediation);
    }

    [Fact]
    public async Task Default_real_activation_with_fake_helper_does_not_attempt_real_vm_boot_preconditions()
    {
        var registry = RegisterRealActivation(ResolveBuiltHelperPath(), ["--fake"], enableRealVmBoot: false);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Ready);
        status.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task First_activation_records_provider_generation()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var registry = RegisterRealActivation(ResolveBuiltHelperPath(), ["--fake"], ledger: ledger);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Ready);
        ledger.ProviderGeneration.Should().Be(1);
        status.Components.Should().Contain(component =>
            component.Phase == ProviderComponentPhase.Ready &&
            component.Name.EndsWith("generation 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Second_activation_restart_advances_provider_generation()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var registry = RegisterRealActivation(ResolveBuiltHelperPath(), ["--fake"], ledger: ledger);
        IProviderActivator activator = registry.ProviderActivators.Single();
        await activator.ActivateAsync(ActivationSpec());
        ulong firstGeneration = ledger.ProviderGeneration;

        ProviderActivationStatus restarted = (await activator.ActivateAsync(ActivationSpec())).Status;

        restarted.ActivationPhase.Should().Be(ProviderActivationPhase.Ready);
        ledger.ProviderGeneration.Should().Be(firstGeneration + 1);
        restarted.Components.Should().Contain(component =>
            component.Phase == ProviderComponentPhase.Ready &&
            component.Name.EndsWith("generation 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restart_stales_existing_first_slice_handles_without_claiming_ready_or_deferred_lanes()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var registry = RegisterRealActivation(ResolveBuiltHelperPath(), ["--fake"], ledger: ledger);
        IProviderActivator activator = registry.ProviderActivators.Single();
        await activator.ActivateAsync(ActivationSpec());
        RuntimeHostStatus host = SeedHost(ledger);
        ExecutionUnitStatus unit = SeedUnit(ledger);
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> projection = SeedProjection(ledger);
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> process = SeedProcess(ledger);

        ProviderActivationStatus restarted = (await activator.ActivateAsync(ActivationSpec())).Status;

        restarted.ActivationPhase.Should().Be(ProviderActivationPhase.Ready);
        RuntimeHostStatus staleHost = await registry.RuntimeHostProviders.Single().GetStatusAsync(host.Handle!.Value);
        staleHost.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(AppleVirtualizationHandleDiagnostics.StaleHandle);

        ExecutionUnitStatus staleUnit = await registry.ExecutionUnitProviders.Single().GetStatusAsync(unit.Handle!.Value);
        staleUnit.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(AppleVirtualizationHandleDiagnostics.StaleHandle);

        SyncResult staleProjection = await registry.ContentProjectionProviders.Single().SyncAsync(projection.TargetHandle, new SyncRequest());
        staleProjection.Conditions.Should().ContainSingle(condition =>
            condition.Type == AppleVirtualizationContentProjectionProvider.InvalidHandleCondition &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == "StaleHandle");

        Func<Task> waitOnStaleProcess = async () => await registry.ProcessProviders.Single().WaitAsync(process.TargetHandle);
        await waitOnStaleProcess.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("AppleVirtualization.StaleHandle:*");

        (await registry.ListAsync()).Single().ContractKinds.Should().Be(AppleVirtualizationProviderDescriptor.FirstSliceContracts);
        registry.NetworkProviders.Should().ContainSingle();
        registry.NetworkMembershipProviders.Should().ContainSingle();
        registry.ServiceDiscoveryProviders.Should().ContainSingle();
        registry.EndpointPublicationProviders.Should().ContainSingle();
        restarted.PreflightChecks.Should().Contain(check =>
            check.Name == "helper-health-not-guest-readiness" &&
            check.Detail != null &&
            check.Detail.Contains("does not imply", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Configured_helper_reports_health_through_supervised_client()
    {
        var registry = RegisterRealActivation(ResolveBuiltHelperPath(), ["--fake"]);
        IProviderActivator activator = registry.ProviderActivators.Single();
        await activator.ActivateAsync(ActivationSpec());

        var client = (IAppleVirtualizationHelperClient)activator;
        AppleVirtualizationHelperEnvelope response = await client.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(
                AppleVirtualizationHelperOperation.HealthProbe,
                "activation-health",
                101,
                AppleVirtualizationHelperProtocol.HealthResponseSchema) with
            {
                HealthProbeRequest = new AppleVirtualizationHealthProbeRequest(),
            });

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.HealthProbeResponse.Should().NotBeNull();
        response.HealthProbeResponse!.Ready.Should().BeTrue();
    }

    [Fact]
    public async Task Activation_stop_terminates_helper_and_reverts_client_to_unavailable()
    {
        var registry = RegisterRealActivation(ResolveBuiltHelperPath(), ["--fake"]);
        IProviderActivator activator = registry.ProviderActivators.Single();
        ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus> activation =
            await activator.ActivateAsync(ActivationSpec());

        await activator.StopAsync(
            activation.Status.ActivationHandle!.Value,
            new ProviderStopOptions(TimeSpan.FromSeconds(1), Force: true, "test stop"));

        ProviderActivationStatus status = await activator.GetStatusAsync(new ResourceRef<ProviderActivation>(
            activation.Metadata.Id,
            activation.Metadata.Scope,
            activation.Metadata.Generation));
        status.ActivationPhase.Should().Be(ProviderActivationPhase.Stopped);

        AppleVirtualizationHelperEnvelope response = await ((IAppleVirtualizationHelperClient)activator).SendAsync(
            AppleVirtualizationHelperEnvelope.Request(
                AppleVirtualizationHelperOperation.HealthProbe,
                "activation-health-after-stop",
                102,
                AppleVirtualizationHelperProtocol.HealthResponseSchema));
        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.Error!.Code.Should().Be("AppleVirtualization.HelperUnavailable");
    }

    [Fact]
    public async Task Missing_helper_path_returns_structured_diagnostic()
    {
        var registry = RegisterRealActivation("", ["--fake"]);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HelperPathMissing" &&
            diagnostic.TargetPath == "activation.helperPath");
    }

    [Fact]
    public async Task Helper_executable_not_found_returns_structured_diagnostic()
    {
        string missing = Path.Combine(Path.GetTempPath(), "hpd-vz-missing-" + Guid.NewGuid().ToString("N"));
        var registry = RegisterRealActivation(missing, ["--fake"]);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HelperExecutableNotFound" &&
            diagnostic.TargetPath == "activation.helperPath");
    }

    [Fact]
    public async Task Helper_exit_before_handshake_returns_structured_diagnostic()
    {
        using ScriptHelper helper = ScriptHelper.Create("printf 'startup failed\\n' >&2\nexit 42\n");
        var registry = RegisterRealActivation(helper.Path, []);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HelperExitedBeforeHandshake" &&
            diagnostic.Message.Contains("startup failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Helper_hang_before_handshake_returns_startup_timeout_diagnostic()
    {
        using ScriptHelper helper = ScriptHelper.Create("sleep 30\n");
        var registry = RegisterRealActivation(helper.Path, [], startupTimeout: TimeSpan.FromMilliseconds(200));

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HelperStartupTimeout" &&
            diagnostic.TargetPath == "activation");
    }

    [Fact]
    public async Task Malformed_hello_response_returns_structured_diagnostic()
    {
        using ScriptHelper helper = ScriptHelper.Create("read line\nprintf 'not-json\\n'\nsleep 1\n");
        var registry = RegisterRealActivation(helper.Path, []);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HelperMalformedResponse" &&
            diagnostic.TargetPath == "activation");
    }

    [Fact]
    public async Task Protocol_mismatch_hello_response_returns_structured_diagnostic()
    {
        using ScriptHelper helper = ScriptHelper.Create(
            "read line\n" +
            "printf '%s\\n' '{\"ProtocolVersion\":\"1.0\",\"MessageType\":1,\"Operation\":0,\"RequestId\":\"apple-vz-activation-1\",\"SequenceNumber\":1,\"ResponseStatus\":0,\"PayloadSchema\":{\"Value\":\"hpd.execution.apple-virtualization.helper.hello.response.v1\"},\"HelloResponse\":{\"HelperName\":\"hpd-vz\",\"HelperVersion\":\"0.1.0\",\"ProtocolVersion\":\"9.9\",\"ProviderGeneration\":1,\"ProtocolCompatible\":false,\"VirtualizationFrameworkAvailable\":true,\"VirtualizationEntitlementVerified\":false}}'\n" +
            "sleep 1\n");
        var registry = RegisterRealActivation(helper.Path, []);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HelperProtocolMismatch" &&
            diagnostic.TargetPath == "activation.hello");
    }

    [Fact]
    public async Task Health_probe_error_returns_structured_diagnostic()
    {
        using ScriptHelper helper = ScriptHelper.Create(
            "read hello\n" +
            "printf '%s\\n' '{\"ProtocolVersion\":\"1.0\",\"MessageType\":1,\"Operation\":0,\"RequestId\":\"apple-vz-activation-1\",\"SequenceNumber\":1,\"ResponseStatus\":0,\"PayloadSchema\":{\"Value\":\"hpd.execution.apple-virtualization.helper.hello.response.v1\"},\"HelloResponse\":{\"HelperName\":\"hpd-vz\",\"HelperVersion\":\"0.1.0\",\"ProtocolVersion\":\"1.0\",\"ProviderGeneration\":1,\"ProtocolCompatible\":true,\"VirtualizationFrameworkAvailable\":true,\"VirtualizationEntitlementVerified\":false}}'\n" +
            "read health\n" +
            "printf '%s\\n' '{\"ProtocolVersion\":\"1.0\",\"MessageType\":1,\"Operation\":4,\"RequestId\":\"apple-vz-activation-2\",\"SequenceNumber\":2,\"ResponseStatus\":2,\"PayloadSchema\":{\"Value\":\"hpd.execution.apple-virtualization.helper.error.v1\"},\"Error\":{\"Code\":\"AppleVirtualization.HelperHealthProbeFailed\",\"Message\":\"health failed\",\"Operation\":\"health.probe\",\"Retryable\":false,\"FailedPhase\":\"Activation\",\"Severity\":4}}'\n" +
            "sleep 1\n");
        var registry = RegisterRealActivation(helper.Path, []);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        status.ActivationPhase.Should().Be(ProviderActivationPhase.Failed);
        status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HelperHealthProbeFailed" &&
            diagnostic.TargetPath == "health.probe");
    }

    [Fact]
    public async Task Startup_stderr_capture_is_bounded()
    {
        using ScriptHelper helper = ScriptHelper.Create("printf '%0200d\\n' 0 >&2\nexit 1\n");
        var registry = RegisterRealActivation(helper.Path, [], stderrBytes: 32);

        ProviderActivationStatus status = (await registry.ProviderActivators.Single().ActivateAsync(ActivationSpec())).Status;

        Diagnostic diagnostic = status.Diagnostics.Single();
        diagnostic.Code.Value.Should().Be("AppleVirtualization.HelperExitedBeforeHandshake");
        diagnostic.Message.Should().Contain("[stderr truncated]");
        diagnostic.Message.Length.Should().BeLessThan(240);
    }

    private static EnvironmentProviderRegistry RegisterRealActivation(
        string helperPath,
        IReadOnlyList<string> arguments,
        TimeSpan? startupTimeout = null,
        int stderrBytes = 4096,
        AppleVirtualizationProviderStateLedger? ledger = null,
        bool enableRealVmBoot = false)
    {
        var registry = new EnvironmentProviderRegistry();
        var options = new AppleVirtualizationProviderOptions
        {
            HelperPath = helperPath,
            HelperArguments = arguments,
            HelperTransportMode = AppleVirtualizationHelperTransportMode.StdIo,
            HelperStartupTimeout = startupTimeout ?? TimeSpan.FromSeconds(5),
            StartupStderrCaptureBytes = stderrBytes,
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableRealHelperActivation = true,
                EnableRealVmBoot = enableRealVmBoot,
            },
        };

        registry.RegisterModule(ledger is null
            ? new AppleVirtualizationProviderModule(options)
            : new AppleVirtualizationProviderModule(options, helperClient: null, ledger: ledger));
        return registry;
    }

    private static RuntimeHostStatus SeedHost(AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<RuntimeHost> metadata =
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host");
        return ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            HostPhase = RuntimeHostPhase.Ready,
            GuestControl = new GuestControlStatus(
                Expected: true,
                Installed: true,
                Reachable: true,
                Transport: ProviderTransportKind.Vsock),
            Readiness = new RuntimeHostReadinessStatus(Ready: true),
        }).Status;
    }

    private static ExecutionUnitStatus SeedUnit(AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<ExecutionUnit> metadata =
            AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>("unit-1", "execution-unit");
        return ledger.UpsertExecutionUnit(metadata, new ExecutionUnitStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            UnitPhase = ExecutionUnitPhase.Ready,
            AssignedHost = AppleVirtualizationContractFixtures.RuntimeHostRef(),
        }).Status;
    }

    private static AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> SeedProjection(
        AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<ContentProjection> metadata =
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-1", "content-projection");
        return ledger.UpsertContentProjection(metadata, new ContentProjectionStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ProjectionPhase = ContentProjectionPhase.Projected,
        });
    }

    private static AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> SeedProcess(
        AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<ProcessInvocation> metadata =
            AppleVirtualizationContractFixtures.Metadata<ProcessInvocation>("process-1", "process-invocation");
        return ledger.UpsertProcessInvocation(metadata, new ProcessInvocationStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ProcessPhase = ProcessInvocationPhase.Running,
        });
    }

    private static ProviderActivationSpec ActivationSpec() =>
        new()
        {
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            Scope = ProviderActivationScope.Runtime,
            ScopeKey = "activation-tests",
            RequiredContracts = AppleVirtualizationProviderDescriptor.FirstSliceContracts,
            ActivationKind = ProviderActivationKind.SupervisedExecutable,
            Supervisor = new ProviderSupervisorRequirement(true, RestartOnFailure: false, TimeSpan.FromSeconds(5)),
            Transport = new ProviderTransportRequirement(ProviderTransportKind.StdIo, RequiresStreaming: true, RequiresHandlePassing: false, RequiresPeerAuthentication: false),
            AuthPolicy = new ProviderAuthPolicy("current-user", RequireSameUser: true, AllowRemoteIdentity: false),
            HealthPolicy = new ProviderHealthPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)),
            LogPolicy = new ProviderLogPolicy("memory", CaptureStartupLogs: true, CaptureDiagnosticLogs: true),
        };

    private static string ResolveBuiltHelperPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string helperRoot = Path.Combine(directory.FullName, "HPD-Environment.Framework", "src", "HPD-Environment.AppleVirtualization", "hpd-vz");
            if (Directory.Exists(helperRoot))
            {
                return FindBuiltHelper(helperRoot);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate hpd-vz source root from the test base directory.");
    }

    private static string FindBuiltHelper(string helperRoot)
    {
        string[] candidates =
        [
            Path.Combine(helperRoot, ".build", "debug", "hpd-vz"),
            Path.Combine(helperRoot, ".build", "arm64-apple-macosx", "debug", "hpd-vz"),
            Path.Combine(helperRoot, ".build", "x86_64-apple-macosx", "debug", "hpd-vz"),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string? discovered = Directory.Exists(Path.Combine(helperRoot, ".build"))
            ? Directory.EnumerateFiles(Path.Combine(helperRoot, ".build"), "hpd-vz", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}debug{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            : null;

        return discovered ?? throw new InvalidOperationException(
            $"Built hpd-vz helper was not found under '{helperRoot}'. Run `swift build` in that directory before activation tests.");
    }

    private sealed class ScriptHelper : IDisposable
    {
        private readonly string _directory;

        private ScriptHelper(string directory, string path)
        {
            _directory = directory;
            Path = path;
        }

        public string Path { get; }

        public static ScriptHelper Create(string body)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hpd-vz-helper-script-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(directory, "helper.sh");
            File.WriteAllText(path, "#!/bin/sh\n" + body);
            using var chmod = Process.Start("chmod", "+x " + path);
            chmod!.WaitForExit();
            return new ScriptHelper(directory, path);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
