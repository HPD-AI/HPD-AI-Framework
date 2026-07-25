namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Hosts;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationRuntimeHostProviderTests
{
    private static readonly PlatformSpec SupportedHost = new("macos", "arm64");

    [Fact]
    public async Task Ensure_host_creates_starts_and_fetches_status_through_helper()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Starting));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Running);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Handle.Should().NotBeNull();
        status.ProviderHandle.Should().NotBeNull();
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.HostEnsure,
            AppleVirtualizationHelperOperation.HostStart,
            AppleVirtualizationHelperOperation.HostStatus,
            AppleVirtualizationHelperOperation.GuestAgentReadinessProbe);
        helper.Requests[0].HostEnsureRequest!.HostId.Should().Be("runtime-host-1");
    }

    [Fact]
    public async Task Ensure_host_returns_ready_only_when_guest_agent_readiness_is_verified()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running, ResourcePhase.Ready));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Ready);
        status.Phase.Should().Be(ResourcePhase.Ready);
        status.GuestControl.Should().NotBeNull();
        status.GuestControl!.Reachable.Should().BeTrue();
        status.GuestControl.Transport.Should().Be(ProviderTransportKind.Vsock);
        status.Readiness.Should().NotBeNull();
        status.Readiness!.Ready.Should().BeTrue();
        status.Readiness.Gates.Should().OnlyContain(gate => gate.Status == ConditionStatus.True);
    }

    [Fact]
    public async Task Ensure_host_keeps_running_state_while_guest_control_is_not_ready()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.NotReady));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Running);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.GuestControl!.Reachable.Should().BeFalse();
        status.Readiness!.Ready.Should().BeFalse();
    }

    [Fact]
    public async Task Transport_connected_alone_leaves_runtimehost_readiness_false()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Handshaking, transportConnected: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Running);
        status.Readiness!.Ready.Should().BeFalse();
        status.GuestControl!.Reachable.Should().BeFalse();
        status.Readiness.Gates.Should().OnlyContain(gate => gate.Status == ConditionStatus.False);
    }

    [Fact]
    public async Task Incompatible_guest_agent_leaves_runtimehost_not_ready_with_diagnostic()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(
            AppleVirtualizationGuestAgentReadinessState.IncompatibleAgentVersion,
            message: "Guest agent version is not compatible."));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Running);
        status.Readiness!.Ready.Should().BeFalse();
        status.GuestControl!.Reachable.Should().BeFalse();
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestAgentReadiness.IncompatibleAgentVersion" &&
            diagnostic.TargetPath == "guestAgent.readinessProbe");
    }

    [Fact]
    public async Task Guest_generations_map_from_verified_ready_response_without_conflating_provider_generation()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(
            AppleVirtualizationGuestAgentReadinessState.Ready,
            verifiedReady: true,
            guestBootId: "boot-42",
            guestBootGeneration: 7,
            guestAgentGeneration: 99));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Generations.HostStartGeneration.Should().Be(new RuntimeHostStartGeneration(1));
        status.Generations.GuestBootGeneration.Should().Be(new GuestBootGeneration("boot-42:7"));
        status.Readiness!.ObservedHostStartGeneration.Should().Be(new RuntimeHostStartGeneration(1));
        status.Handle!.Value.ProviderGeneration.Should().Be(ledger.ProviderGeneration);
        status.Handle.Value.ProviderGeneration.Should().NotBe(99);
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.GuestAgentGeneration" &&
            condition.Message == "99");
    }

    [Fact]
    public async Task Missing_capability_failure_produces_stable_bounded_diagnostic()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(
            AppleVirtualizationGuestAgentReadinessState.MissingCapability,
            missingCapabilities: ["projection.mount", "process.start"]));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Readiness!.Ready.Should().BeFalse();
        Diagnostic diagnostic = status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestAgentReadiness.MissingCapability").Subject;
        diagnostic.Message.Should().Contain("projection.mount");
        diagnostic.Message.Length.Should().BeLessThanOrEqualTo(512);
    }

    [Fact]
    public async Task Malformed_frame_and_disconnect_failures_produce_stable_bounded_diagnostics()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(
            AppleVirtualizationGuestAgentReadinessState.MalformedFrame,
            errorCode: "AppleVirtualization.GuestAgentReadiness.MalformedFrame",
            message: new string('x', 800)));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Readiness!.Ready.Should().BeFalse();
        Diagnostic diagnostic = status.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestAgentReadiness.MalformedFrame").Subject;
        diagnostic.Message.Length.Should().BeLessThanOrEqualTo(512);
    }

    [Fact]
    public async Task Vm_stops_during_readiness_produces_stable_diagnostic_and_readiness_false()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedRunningWaitingHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Stopped, ResourcePhase.Ready));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.GetStatusAsync(seeded.Handle!.Value);

        status.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        status.Readiness!.Ready.Should().BeFalse();
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestAgentReadiness.VmStoppedDuringReadiness" &&
            diagnostic.TargetPath == "guestAgent.readinessProbe");
        helper.Requests.Should().ContainSingle().Which.Operation.Should().Be(AppleVirtualizationHelperOperation.HostStatus);
    }

    [Fact]
    public async Task Readiness_timeout_is_bounded_and_retryable_diagnostic()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(
            AppleVirtualizationGuestAgentReadinessState.Timeout,
            errorCode: "AppleVirtualization.GuestAgentReadiness.Timeout",
            retryable: true,
            message: "Timed out waiting for guest-agent readiness."));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Readiness!.Ready.Should().BeFalse();
        helper.Requests[3].GuestAgentReadinessProbeRequest!.TimeoutMilliseconds.Should().Be(30000);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestAgentReadiness.Timeout" &&
            diagnostic.Message.Length <= 512);
    }

    [Fact]
    public async Task Ensure_host_maps_degraded_helper_status_to_diagnostics()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(
            AppleVirtualizationHelperOperation.HostStatus,
            RuntimeHostPhase.Degraded,
            ResourcePhase.Degraded,
            diagnostics:
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = new DiagnosticCode("AppleVirtualization.GuestControlUnreachable"),
                    Message = "Guest control has not answered yet.",
                    ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                    TargetPath = "guest-control",
                },
            ]));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Degraded);
        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.Diagnostics.Should().ContainSingle().Which.Code.Value.Should().Be("AppleVirtualization.GuestControlUnreachable");
    }

    [Fact]
    public async Task Stop_host_sends_graceful_request_and_returns_stopped_status()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostRequestStop, RuntimeHostPhase.Stopped, ResourcePhase.Ready));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus stopped = await provider.StopAsync(
            seeded.Handle!.Value,
            StopPolicy.Default with { Kind = StopKind.Graceful });

        stopped.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        helper.Requests.Should().ContainSingle().Which.Operation.Should().Be(AppleVirtualizationHelperOperation.HostRequestStop);
        helper.Requests[0].HostLifecycleRequest!.StopKind.Should().Be(StopKind.Graceful);
    }

    [Fact]
    public async Task Stop_host_sends_force_request_for_kill_policy()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStop, RuntimeHostPhase.Stopped, ResourcePhase.Ready));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus stopped = await provider.StopAsync(
            seeded.Handle!.Value,
            StopPolicy.Default with { Kind = StopKind.Kill });

        stopped.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        helper.Requests.Should().ContainSingle().Which.Operation.Should().Be(AppleVirtualizationHelperOperation.HostStop);
        helper.Requests[0].HostLifecycleRequest!.StopKind.Should().Be(StopKind.Kill);
    }

    [Fact]
    public async Task Delete_host_sends_delete_and_releases_ledger_state()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostDelete, RuntimeHostPhase.Deleted, ResourcePhase.Deleted));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceRef<RuntimeHost> host = AppleVirtualizationContractFixtures.RuntimeHostRef();

        await provider.DeleteAsync(host);
        await provider.DeleteAsync(host);

        ledger.TryGetRuntimeHost(seeded.Handle!.Value).Diagnostic!.Code.Should().Be(AppleVirtualizationHandleDiagnostics.MissingHandle);
        helper.Requests.Count(request => request.Operation == AppleVirtualizationHelperOperation.HostDelete).Should().Be(1);
    }

    [Fact]
    public async Task Delete_host_keeps_ledger_state_when_helper_rejects_deletion()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostError(AppleVirtualizationHelperOperation.HostDelete, "AppleVirtualization.HostDeleteFailed"));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceRef<RuntimeHost> host = AppleVirtualizationContractFixtures.RuntimeHostRef();

        Func<Task> act = async () => await provider.DeleteAsync(host);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*HostDeleteFailed*");
        ledger.TryGetRuntimeHost(seeded.Handle!.Value).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_host_waits_for_native_stop_before_releasing_ledger_state()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostDelete, RuntimeHostPhase.Stopping));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Stopped));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostDelete, RuntimeHostPhase.Deleted));
        var provider = new AppleVirtualizationRuntimeHostProvider(
            helper,
            ledger,
            SupportedHost,
            new AppleVirtualizationProviderOptions { HostDeletionTimeout = TimeSpan.FromSeconds(1) });
        ResourceRef<RuntimeHost> host = AppleVirtualizationContractFixtures.RuntimeHostRef();

        await provider.DeleteAsync(host);

        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.HostDelete,
            AppleVirtualizationHelperOperation.HostStatus,
            AppleVirtualizationHelperOperation.HostDelete);
        ledger.TryGetRuntimeHost(seeded.Handle!.Value).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_host_keeps_ledger_state_for_mismatched_helper_host()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        AppleVirtualizationHelperEnvelope mismatched =
            HostResponse(AppleVirtualizationHelperOperation.HostDelete, RuntimeHostPhase.Deleted) with
            {
                HostStatusResponse = new AppleVirtualizationHostStatusResponse
                {
                    HostId = "runtime-host-other",
                    HostPhase = RuntimeHostPhase.Deleted,
                    Phase = ResourcePhase.Deleted,
                },
            };
        helper.EnqueueResponse(mismatched);
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceRef<RuntimeHost> host = AppleVirtualizationContractFixtures.RuntimeHostRef();

        Func<Task> act = async () => await provider.DeleteAsync(host);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched host identity*");
        ledger.TryGetRuntimeHost(seeded.Handle!.Value).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_host_timeout_bounds_blocked_helper_io_and_keeps_ledger_state()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var provider = new AppleVirtualizationRuntimeHostProvider(
            new BlockingHelperClient(),
            ledger,
            SupportedHost,
            new AppleVirtualizationProviderOptions { HostDeletionTimeout = TimeSpan.FromMilliseconds(100) });
        ResourceRef<RuntimeHost> host = AppleVirtualizationContractFixtures.RuntimeHostRef();

        Func<Task> act = async () => await provider.DeleteAsync(host);

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*runtime-host-1*");
        ledger.TryGetRuntimeHost(seeded.Handle!.Value).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_host_preserves_caller_cancellation()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var provider = new AppleVirtualizationRuntimeHostProvider(
            new BlockingHelperClient(),
            ledger,
            SupportedHost,
            new AppleVirtualizationProviderOptions { HostDeletionTimeout = TimeSpan.FromSeconds(10) });
        using var callerCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Func<Task> act = async () => await provider.DeleteAsync(
            AppleVirtualizationContractFixtures.RuntimeHostRef(),
            callerCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        ledger.TryGetRuntimeHost(seeded.Handle!.Value).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Get_status_resolves_handle_and_refreshes_current_host_status()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Ready, ResourcePhase.Ready, reachable: true));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.GetStatusAsync(seeded.Handle!.Value);

        status.HostPhase.Should().Be(RuntimeHostPhase.Ready);
        status.Readiness!.Ready.Should().BeTrue();
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.HostStatus,
            AppleVirtualizationHelperOperation.GuestAgentReadinessProbe);
    }

    [Fact]
    public async Task Get_status_returns_stale_handle_diagnostic()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        ledger.AdvanceProviderGeneration();
        var provider = new AppleVirtualizationRuntimeHostProvider(new FakeAppleVirtualizationHelperClient(), ledger, SupportedHost);

        RuntimeHostStatus status = await provider.GetStatusAsync(seeded.Handle!.Value);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.HostPhase.Should().Be(RuntimeHostPhase.Failed);
        status.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(AppleVirtualizationHandleDiagnostics.StaleHandle);
    }

    [Fact]
    public async Task Ensure_host_reports_unsupported_host_platform_without_calling_helper()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, new PlatformSpec("linux", "x64"));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.HostPhase.Should().Be(RuntimeHostPhase.Failed);
        status.Diagnostics.Should().ContainSingle().Which.Code.Value.Should().Be("AppleVirtualization.HostUnsupported");
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RuntimeHost_validation_request_maps_provider_options_and_spec_to_helper_dto()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(ValidationResponse(passed: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, ValidationOptions());

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        helper.Requests.Should().ContainSingle().Which.Operation.Should().Be(AppleVirtualizationHelperOperation.VmConfigurationValidate);
        AppleVirtualizationVmConfigurationValidationRequest request = helper.Requests[0].VmConfigurationValidationRequest!;
        request.HostId.Should().Be("runtime-host-1");
        request.CpuCount.Should().Be(4);
        request.MemorySizeBytes.Should().Be(4L * 1024 * 1024 * 1024);
        request.IncludeSerialConsole.Should().BeTrue();
        request.IncludeVirtioSocketPlaceholder.Should().BeTrue();
        request.GuestImage.KernelPath.Should().Be("/opt/hpd/guests/applevz-linux-arm64/vmlinuz");
        request.GuestImage.InitrdPath.Should().Be("/opt/hpd/guests/applevz-linux-arm64/initrd.img");
        request.GuestImage.DiskImagePath.Should().Be("/opt/hpd/guests/applevz-linux-arm64/root.raw");
        request.GuestImage.SerialLogPath.Should().Be("/var/log/hpd/apple-vz/runtime-host.serial.log");
        request.GuestImage.ExpectedGuestAgentVersion.Should().Be("0.1.0");
        status.HostPhase.Should().Be(RuntimeHostPhase.Preparing);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Readiness!.Ready.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_boot_input_validation_maps_to_failed_status_with_stable_diagnostic()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationRuntimeHostProvider(
            helper,
            ledger,
            SupportedHost,
            ValidationOptions() with
            {
                GuestImage = CompleteGuestImage() with { KernelPath = null },
            });

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.HostPhase.Should().Be(RuntimeHostPhase.Failed);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RuntimeHostBootInputMissing" &&
            diagnostic.TargetPath == "guestImage");
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.BootInputsConfigured" &&
            condition.Status == ConditionStatus.False &&
            condition.Reason == "MissingRequiredBootInputs");
        status.Readiness!.Ready.Should().BeFalse();
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Valid_configuration_validation_does_not_claim_runtimehost_ready_or_running()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(ValidationResponse(passed: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, ValidationOptions());

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Preparing);
        status.HostPhase.Should().NotBe(RuntimeHostPhase.Running);
        status.HostPhase.Should().NotBe(RuntimeHostPhase.Ready);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Readiness!.Ready.Should().BeFalse();
        status.GuestControl!.Reachable.Should().BeFalse();
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.HostStart);
    }

    [Fact]
    public async Task Helper_health_does_not_promote_host_readiness()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.HealthProbe,
            RequestId = "response-1",
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            HealthProbeResponse = new AppleVirtualizationHealthProbeResponse(true, "helper healthy; not HPD guest readiness"),
        });
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, ValidationOptions());

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Degraded);
        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.Readiness!.Ready.Should().BeFalse();
        status.GuestControl!.Reachable.Should().BeFalse();
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HostHelperError" &&
            diagnostic.TargetPath == "vmConfiguration.validate");
    }

    [Fact]
    public async Task Guest_agent_missing_remains_requires_configuration_and_not_ready()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationRuntimeHostProvider(
            helper,
            ledger,
            SupportedHost,
            ValidationOptions() with
            {
                GuestImage = CompleteGuestImage() with { ExpectedGuestAgentVersion = null },
            });

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.HostPhase.Should().Be(RuntimeHostPhase.Degraded);
        status.Readiness!.Ready.Should().BeFalse();
        status.GuestControl!.Installed.Should().BeFalse();
        status.GuestControl.Reachable.Should().BeFalse();
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestAgentConfigurationMissing" &&
            diagnostic.TargetPath == "guestImage.expectedGuestAgentVersion");
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.GuestAgentConfigured" &&
            condition.Reason == "RequiresConfiguration");
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Virtiofs_missing_expectation_remains_requires_configuration_without_projection_claim()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationRuntimeHostProvider(
            helper,
            ledger,
            SupportedHost,
            ValidationOptions() with
            {
                GuestImage = CompleteGuestImage() with { ExpectVirtiofsSupport = false },
            });

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.HostPhase.Should().Be(RuntimeHostPhase.Degraded);
        status.Readiness!.Ready.Should().BeFalse();
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.VirtiofsConfigurationMissing" &&
            diagnostic.TargetPath == "guestImage.expectVirtiofsSupport");
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.VirtiofsConfigured" &&
            condition.Reason == "RequiresConfiguration");
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Default_provider_path_does_not_attempt_real_boot()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Starting));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Readiness!.Ready.Should().BeFalse();
        helper.Requests.Select(request => request.Operation).Should().ContainInOrder(
            AppleVirtualizationHelperOperation.HostEnsure,
            AppleVirtualizationHelperOperation.HostStart,
            AppleVirtualizationHelperOperation.HostStatus);
        helper.Requests.Any(request =>
            request.HostLifecycleRequest is not null &&
            (request.HostLifecycleRequest.ExplicitRealMode ||
            request.HostLifecycleRequest.VmConfigurationValidationRequest is not null)).Should().BeFalse();
    }

    [Fact]
    public async Task Default_host_bootstrap_does_not_install_or_enable_container_runtime()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions());

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EngineStatus);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EngineProvision);
        status.ControlPlane!.Components.Should().NotContain(component => component.Kind == ProviderComponentKind.EngineDaemon);
        status.Bootstrap!.Conditions.Should().BeEmpty();
    }

    [Fact]
    public async Task Opt_in_container_runtime_without_bootstrap_configuration_is_structured_degraded()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.HostPhase.Should().Be(RuntimeHostPhase.Degraded);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineBootstrapConfigurationMissing" &&
            diagnostic.TargetPath == "bootstrap.guestComponents.containerRuntime");
        status.Bootstrap!.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.EngineBootstrap" &&
            condition.Reason == "RequiresConfiguration");
        status.ControlPlane!.Components.Should().Contain(component =>
            component.Kind == ProviderComponentKind.EngineDaemon &&
            component.Name == "docker" &&
            component.Phase == ProviderComponentPhase.Degraded);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EngineStatus);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EngineProvision);
    }

    [Fact]
    public async Task Engine_component_readiness_does_not_promote_runtimehost_when_guest_readiness_is_invalid()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.NotReady));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: true,
            state: AppleVirtualizationEngineObservationState.Ready));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        status.Readiness!.Ready.Should().BeFalse();
        status.HostPhase.Should().Be(RuntimeHostPhase.Running);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Bootstrap!.Conditions.Should().Contain(condition =>
            condition.Reason == "WaitingForGuestReadiness");
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EngineStatus);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EngineProvision);
    }

    [Theory]
    [InlineData(EngineAuthorityMode.Rootless, SensitiveAuthorityClass.RootlessEngineControl)]
    [InlineData(EngineAuthorityMode.Rootful, SensitiveAuthorityClass.RootfulEngineControl)]
    public async Task Opt_in_container_runtime_records_explicit_authority_mode(
        EngineAuthorityMode mode,
        SensitiveAuthorityClass expectedAuthorityClass)
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: true,
            authorityMode: mode,
            state: AppleVirtualizationEngineObservationState.Ready));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Ready);
        status.Phase.Should().Be(ResourcePhase.Ready);
        helper.Requests.Select(request => request.Operation).Should().Contain(AppleVirtualizationHelperOperation.EngineStatus);
        AppleVirtualizationEngineStatusRequest request = helper.Requests.Single(request => request.Operation == AppleVirtualizationHelperOperation.EngineStatus).EngineStatusRequest!;
        request.AuthorityMode.Should().Be(mode);
        request.ScriptedObservationState.Should().Be(AppleVirtualizationEngineObservationState.Ready);
        status.ControlPlane!.Components.Should().Contain(component =>
            component.Kind == ProviderComponentKind.EngineDaemon &&
            component.Phase == ProviderComponentPhase.Ready);
        AppleVirtualizationHelperEnvelope engineResponse = await helper.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(
                AppleVirtualizationHelperOperation.EngineStatus,
                "assert-engine",
                100,
                AppleVirtualizationHelperProtocol.EngineStatusRequestSchema) with
            {
                EngineStatusRequest = request,
            });
        engineResponse.EngineStatusResponse!.Endpoints[0].SensitivePolicy.AuthorityClass.Should().Be(expectedAuthorityClass);
    }

    [Fact]
    public async Task Explicit_opt_in_engine_provisioning_without_install_gates_stays_degraded_without_default_install()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: true,
            state: AppleVirtualizationEngineObservationState.NotInstalled,
            provisioningEnabled: true));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.Provisioning!.Complete.Should().BeFalse();
        status.Diagnostics.Select(diagnostic => diagnostic.Code.Value).Should().Contain([
            "AppleVirtualization.EngineProvisioning.PackageInstallDisabled",
            "AppleVirtualization.EngineProvisioning.ServiceEnablementDisabled",
        ]);
        helper.Requests.Select(request => request.Operation).Should().ContainInOrder(
            AppleVirtualizationHelperOperation.EngineStatus,
            AppleVirtualizationHelperOperation.EngineProvision);
        AppleVirtualizationEngineProvisioningRequest request =
            helper.Requests.Single(request => request.Operation == AppleVirtualizationHelperOperation.EngineProvision)
                .EngineProvisioningRequest!;
        request.AllowPackageInstall.Should().BeFalse();
        request.AllowServiceEnablement.Should().BeFalse();
        request.AuthorityMode.Should().Be(EngineAuthorityMode.Rootless);
        request.MaxCapturedOutputBytes.Should().Be(AppleVirtualizationEngineProvisioningOptions.DefaultMaxCapturedOutputBytes);
    }

    [Fact]
    public async Task Explicit_install_and_service_gates_send_status_bearing_guest_agent_plan()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: true,
            state: AppleVirtualizationEngineObservationState.NotInstalled,
            provisioningEnabled: true,
            allowPackageInstall: true,
            allowServiceEnablement: true));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Provisioning!.Complete.Should().BeFalse();
        AppleVirtualizationEngineProvisioningRequest request =
            helper.Requests.Single(request => request.Operation == AppleVirtualizationHelperOperation.EngineProvision)
                .EngineProvisioningRequest!;
        request.AllowPackageInstall.Should().BeTrue();
        request.AllowServiceEnablement.Should().BeTrue();
    }

    [Fact]
    public async Task Existing_ready_engine_is_observed_and_does_not_run_provisioning()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: true,
            state: AppleVirtualizationEngineObservationState.Ready,
            provisioningEnabled: true,
            allowPackageInstall: true,
            allowServiceEnablement: true));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        helper.Requests.Select(request => request.Operation).Should().Contain(AppleVirtualizationHelperOperation.EngineStatus);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EngineProvision);
    }

    [Fact]
    public async Task Provisioning_timeout_is_reported_with_bounded_diagnostics()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: true,
            state: AppleVirtualizationEngineObservationState.NotInstalled,
            provisioningEnabled: true,
            allowPackageInstall: true,
            allowServiceEnablement: true,
            provisioningTimeout: TimeSpan.FromMilliseconds(50),
            scriptedExecutionState: AppleVirtualizationEngineProvisioningExecutionState.TimedOut,
            scriptedStdout: new string('o', 32),
            scriptedStderr: new string('e', 32)));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.Provisioning!.Complete.Should().BeFalse();
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineProvisioning.Timeout" &&
            diagnostic.Severity == DiagnosticSeverity.Error);
        AppleVirtualizationEngineProvisioningRequest request =
            helper.Requests.Single(request => request.Operation == AppleVirtualizationHelperOperation.EngineProvision)
                .EngineProvisioningRequest!;
        request.ProvisioningTimeoutMilliseconds.Should().Be(50);
        AppleVirtualizationHelperEnvelope evidenceResponse =
            await helper.SendAsync(helper.Requests.Single(request => request.Operation == AppleVirtualizationHelperOperation.EngineProvision));
        evidenceResponse.EngineProvisioningResponse!.Evidence.TimedOut.Should().BeTrue();
        evidenceResponse.EngineProvisioningResponse.Evidence.TimeoutMilliseconds.Should().Be(50);
        evidenceResponse.EngineProvisioningResponse.Evidence.StdoutTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task Engine_provisioning_reports_missing_guest_prerequisite_diagnostics()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: true,
            authorityMode: EngineAuthorityMode.Rootless,
            state: AppleVirtualizationEngineObservationState.NotInstalled,
            provisioningEnabled: true,
            allowPackageInstall: true,
            allowServiceEnablement: true,
            prerequisites: new AppleVirtualizationEngineProvisioningPrerequisiteStatus
            {
                PackageManagerAvailable = false,
                SystemdAvailable = false,
                UserSystemdAvailable = false,
                GuestAgentAvailable = false,
                RootlessSupported = false,
                ImageStoreSupported = false,
                NetworkAvailable = false,
                WritableGuestStorageAvailable = false,
                GuestOsSupported = false,
            }));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.Diagnostics.Select(diagnostic => diagnostic.Code.Value).Should().Contain([
            "AppleVirtualization.EngineProvisioning.PackageManagerMissing",
            "AppleVirtualization.EngineProvisioning.SystemdMissing",
            "AppleVirtualization.EngineProvisioning.UserSystemdMissing",
            "AppleVirtualization.EngineProvisioning.GuestAgentMissing",
            "AppleVirtualization.EngineProvisioning.RootlessUnsupported",
            "AppleVirtualization.EngineProvisioning.ImageStoreUnsupported",
            "AppleVirtualization.EngineProvisioning.NetworkMissing",
            "AppleVirtualization.EngineProvisioning.WritableStorageMissing",
            "AppleVirtualization.EngineProvisioning.GuestOsUnsupported",
        ]);
        AppleVirtualizationHelperEnvelope evidenceResponse =
            await helper.SendAsync(helper.Requests.Single(request => request.Operation == AppleVirtualizationHelperOperation.EngineProvision));
        evidenceResponse.EngineProvisioningResponse!.Evidence.PackageManagerAvailable.Should().BeFalse();
        evidenceResponse.EngineProvisioningResponse.Evidence.NetworkAvailable.Should().BeFalse();
        evidenceResponse.EngineProvisioningResponse.Evidence.WritableGuestStorageAvailable.Should().BeFalse();
        evidenceResponse.EngineProvisioningResponse.Evidence.SystemdAvailable.Should().BeFalse();
        evidenceResponse.EngineProvisioningResponse.Evidence.UserSystemdAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData(EngineAuthorityMode.Rootless, "/run/user/1000/docker.sock")]
    [InlineData(EngineAuthorityMode.Rootful, "/var/run/docker.sock")]
    public async Task Engine_provisioning_selects_rootless_or_rootful_guest_socket_path(
        EngineAuthorityMode authorityMode,
        string expectedSocketPath)
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(
                AppleVirtualizationHelperOperation.EngineProvision,
                "engine-provision",
                1,
                AppleVirtualizationHelperProtocol.EngineProvisionRequestSchema) with
            {
                EngineProvisioningRequest = new AppleVirtualizationEngineProvisioningRequest
                {
                    HostId = "runtime-host-1",
                    EngineId = "docker",
                    AuthorityMode = authorityMode,
                    AllowPackageInstall = true,
                    AllowServiceEnablement = true,
                },
            });

        response.EngineProvisioningResponse!.GuestSocketPath.Should().Be(expectedSocketPath);
        response.EngineProvisioningResponse.InstallAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task Engine_provisioning_does_not_publish_or_use_host_engine_socket()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: true,
            state: AppleVirtualizationEngineObservationState.NotInstalled,
            provisioningEnabled: true));

        await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EndpointPublish);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.AuthorityBind);
        AppleVirtualizationEngineProvisioningRequest request =
            helper.Requests.Single(request => request.Operation == AppleVirtualizationHelperOperation.EngineProvision)
                .EngineProvisioningRequest!;
        request.Api.Should().Be(EngineApiKind.DockerCompatible);
        request.HostId.Should().Be("runtime-host-1");
    }

    [Fact]
    public async Task Engine_bootstrap_requires_explicit_authority_mode()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(AppleVirtualizationGuestAgentReadinessState.Ready, verifiedReady: true));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: false,
            state: AppleVirtualizationEngineObservationState.Ready));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), EngineSpec("docker"), observed: null);

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EngineBootstrapAuthorityModeMissing" &&
            diagnostic.TargetPath == "engineBootstrap.authorityMode");
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EngineStatus);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.EngineProvision);
    }

    [Fact]
    public async Task Engine_bootstrap_cleanup_does_not_release_workspace_projection_content()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostRequestStop, RuntimeHostPhase.Stopped, ResourcePhase.Ready));
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, EngineBootstrapOptions(
            enabled: true,
            authorityModeConfigured: true,
            state: AppleVirtualizationEngineObservationState.Ready));

        RuntimeHostStatus stopped = await provider.StopAsync(
            seeded.Handle!.Value,
            StopPolicy.Default with { Kind = StopKind.Graceful });

        stopped.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        helper.Requests.Should().ContainSingle().Which.Operation.Should().Be(AppleVirtualizationHelperOperation.HostRequestStop);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.ProjectionRelease);
        helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.ProjectionFinalize);
    }

    [Fact]
    public async Task Explicit_real_mode_with_failed_preconditions_maps_to_failed_status_without_helper_call()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationRuntimeHostProvider(
            helper,
            ledger,
            SupportedHost,
            RealBootOptions(CompleteGuestImage() with { KernelPath = null }));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.HostPhase.Should().Be(RuntimeHostPhase.Failed);
        status.Readiness!.Ready.Should().BeFalse();
        status.GuestControl!.Reachable.Should().BeFalse();
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RealModeKernelMissing" &&
            diagnostic.TargetPath == "GuestImage.KernelPath");
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.RealModePrecondition.real-mode-boot-inputs" &&
            condition.Status == ConditionStatus.False);
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Explicit_real_mode_start_sends_vm_configuration_and_never_claims_readiness()
    {
        using RealBootFiles files = RealBootFiles.Create();
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Starting));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        var provider = new AppleVirtualizationRuntimeHostProvider(
            helper,
            ledger,
            SupportedHost,
            RealBootOptions(files.GuestImage));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.HostPhase.Should().Be(RuntimeHostPhase.Running);
        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.Readiness!.Ready.Should().BeFalse();
        status.GuestControl!.Reachable.Should().BeFalse();
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.HostStart,
            AppleVirtualizationHelperOperation.HostStatus,
            AppleVirtualizationHelperOperation.GuestAgentReadinessProbe);
        AppleVirtualizationHostLifecycleRequest start = helper.Requests[0].HostLifecycleRequest!;
        start.ExplicitRealMode.Should().BeTrue();
        start.Reason.Should().Be("ensure-real-vm");
        start.VmConfigurationValidationRequest.Should().NotBeNull();
        start.VmConfigurationValidationRequest!.HostId.Should().Be("runtime-host-1");
        start.VmConfigurationValidationRequest.GuestImage.KernelPath.Should().Be(files.GuestImage.KernelPath);
        start.VmConfigurationValidationRequest.GuestImage.InitrdPath.Should().Be(files.GuestImage.InitrdPath);
        start.VmConfigurationValidationRequest.GuestImage.DiskImagePath.Should().Be(files.GuestImage.DiskImagePath);
        start.VmConfigurationValidationRequest.GuestImage.SerialLogPath.Should().Be(files.GuestImage.SerialLogPath);
        AppleVirtualizationGuestAgentReadinessProbeRequest readiness = helper.Requests[2].GuestAgentReadinessProbeRequest!;
        readiness.ExplicitRealMode.Should().BeTrue();
        readiness.ExpectedAgentVersion.Should().Be("0.1.0");
    }

    [Fact]
    public async Task Explicit_real_mode_helper_failure_maps_to_failed_status_with_diagnostics()
    {
        using RealBootFiles files = RealBootFiles.Create();
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostError(AppleVirtualizationHelperOperation.HostStart, "AppleVirtualization.HostStartFailed"));
        var provider = new AppleVirtualizationRuntimeHostProvider(
            helper,
            ledger,
            SupportedHost,
            RealBootOptions(files.GuestImage));

        RuntimeHostStatus status = await provider.EnsureAsync(Metadata("runtime-host-1"), Spec(), observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.HostPhase.Should().Be(RuntimeHostPhase.Failed);
        status.Readiness!.Ready.Should().BeFalse();
        status.Diagnostics.Should().ContainSingle().Which.Code.Value.Should().Be("AppleVirtualization.HostStartFailed");
        helper.Requests.Should().ContainSingle().Which.Operation.Should().Be(AppleVirtualizationHelperOperation.HostStart);
    }

    [Fact]
    public async Task Explicit_real_mode_stop_sends_helper_stop_and_clears_readiness()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus seeded = SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostRequestStop, RuntimeHostPhase.Stopped, ResourcePhase.Ready));
        var provider = new AppleVirtualizationRuntimeHostProvider(
            helper,
            ledger,
            SupportedHost,
            RealBootOptions(CompleteGuestImage()));

        RuntimeHostStatus stopped = await provider.StopAsync(
            seeded.Handle!.Value,
            StopPolicy.Default with { Kind = StopKind.Graceful, GracePeriod = TimeSpan.FromMilliseconds(1500) });

        stopped.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        stopped.Readiness!.Ready.Should().BeFalse();
        helper.Requests.Should().ContainSingle().Which.Operation.Should().Be(AppleVirtualizationHelperOperation.HostRequestStop);
        helper.Requests[0].HostLifecycleRequest!.ExplicitRealMode.Should().BeTrue();
        helper.Requests[0].HostLifecycleRequest!.GracePeriodMilliseconds.Should().Be(1500);
    }

    private static RuntimeHostStatus SeedHost(AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
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

    private static RuntimeHostStatus SeedRunningWaitingHost(AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
        return ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
        {
            Phase = ResourcePhase.Reconciling,
            ObservedGeneration = metadata.Generation,
            HostPhase = RuntimeHostPhase.Running,
            GuestControl = new GuestControlStatus(
                Expected: true,
                Installed: true,
                Reachable: false,
                Transport: ProviderTransportKind.Vsock),
            Readiness = new RuntimeHostReadinessStatus(Ready: false),
        }).Status;
    }

    [Fact]
    public async Task Repeated_identical_ensure_reuses_active_vm()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        EnqueueRunningHost(helper);
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
        RuntimeHostSpec spec = Spec();

        RuntimeHostStatus first = await provider.EnsureAsync(metadata, spec, observed: null);
        int createsBeforeSecondEnsure = helper.Requests.Count(request =>
            request.Operation is AppleVirtualizationHelperOperation.HostEnsure or AppleVirtualizationHelperOperation.HostStart);

        RuntimeHostStatus second = await provider.EnsureAsync(metadata, spec, first);

        second.Handle.Should().Be(first.Handle);
        helper.Requests.Count(request =>
            request.Operation is AppleVirtualizationHelperOperation.HostEnsure or AppleVirtualizationHelperOperation.HostStart)
            .Should().Be(createsBeforeSecondEnsure);
    }

    [Fact]
    public async Task Active_vm_rejects_cpu_or_memory_change()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        EnqueueRunningHost(helper);
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
        RuntimeHostStatus first = await provider.EnsureAsync(metadata, Spec(), observed: null);
        RuntimeHostSpec changed = Spec() with
        {
            Capacity = Spec().Capacity with
            {
                CpuCores = (Spec().Capacity.CpuCores ?? 1) + 1,
                MemoryBytes = (Spec().Capacity.MemoryBytes ?? 1024) + 1024,
            },
        };

        RuntimeHostStatus result = await provider.EnsureAsync(metadata, changed, first);

        result.Phase.Should().Be(ResourcePhase.Failed);
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RuntimeHostImmutableConfigurationConflict");
    }

    [Fact]
    public async Task Active_vm_rejects_storage_change()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        EnqueueRunningHost(helper);
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
        RuntimeHostStatus first = await provider.EnsureAsync(metadata, Spec(), observed: null);
        RuntimeHostSpec changed = Spec() with
        {
            Capacity = Spec().Capacity with
            {
                StorageBytes = (Spec().Capacity.StorageBytes ?? 1024) + 1024,
            },
        };

        RuntimeHostStatus result = await provider.EnsureAsync(metadata, changed, first);

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RuntimeHostImmutableConfigurationConflict");
    }

    [Fact]
    public async Task Active_vm_rejects_boot_image_or_engine_configuration_change()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        EnqueueRunningHost(helper);
        var originalOptions = EngineBootstrapOptions(enabled: true, authorityModeConfigured: true) with
        {
            GuestImage = CompleteGuestImage(),
        };
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, originalOptions);
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
        RuntimeHostStatus first = await provider.EnsureAsync(metadata, EngineSpec("docker"), observed: null);
        AppleVirtualizationProviderOptions changedOptions = originalOptions with
        {
            GuestImage = originalOptions.GuestImage with { DiskImagePath = "/different/root.raw" },
            EngineBootstrap = originalOptions.EngineBootstrap with { Api = EngineApiKind.ContainerdApi },
        };
        var changedProvider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost, changedOptions);

        RuntimeHostStatus result = await changedProvider.EnsureAsync(metadata, EngineSpec("docker"), first);

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RuntimeHostImmutableConfigurationConflict");
    }

    [Theory]
    [InlineData(RuntimeHostPhase.Stopped)]
    [InlineData(RuntimeHostPhase.Failed)]
    public async Task Stopped_or_failed_observation_is_reconciled_instead_of_reused(RuntimeHostPhase phase)
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        EnqueueRunningHost(helper);
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
        RuntimeHostStatus first = await provider.EnsureAsync(metadata, Spec(), observed: null);
        RuntimeHostStatus inactive = first with { HostPhase = phase };
        int lifecycleRequests = helper.Requests.Count;

        await provider.EnsureAsync(metadata, Spec(), inactive);

        helper.Requests.Count.Should().BeGreaterThan(lifecycleRequests);
        helper.Requests.Skip(lifecycleRequests).Should().Contain(request =>
            request.Operation == AppleVirtualizationHelperOperation.HostEnsure);
    }

    [Fact]
    public async Task Active_vm_rejects_stale_provider_generation()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        EnqueueRunningHost(helper);
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
        RuntimeHostStatus first = await provider.EnsureAsync(metadata, Spec(), observed: null);
        ledger.AdvanceProviderGeneration();

        RuntimeHostStatus result = await provider.EnsureAsync(metadata, Spec(), first);

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RuntimeHostStaleObservedHandle");
    }

    [Fact]
    public async Task Active_vm_fingerprint_hashes_same_length_extension_payload_bytes()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        EnqueueRunningHost(helper);
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
        ProviderExtensionData firstExtension = Extension([0x01, 0x02, 0x03, 0x04]);
        ProviderExtensionData secondExtension = Extension([0x01, 0x02, 0x03, 0x05]);
        RuntimeHostSpec firstSpec = Spec() with { ProviderExtensions = [firstExtension] };
        RuntimeHostStatus first = await provider.EnsureAsync(metadata, firstSpec, observed: null);
        RuntimeHostSpec changed = firstSpec with { ProviderExtensions = [secondExtension] };

        RuntimeHostStatus result = await provider.EnsureAsync(metadata, changed, first);

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RuntimeHostImmutableConfigurationConflict");
    }

    [Fact]
    public async Task Active_vm_fingerprint_hashes_nested_same_length_extension_payload_bytes()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var helper = new FakeAppleVirtualizationHelperClient();
        EnqueueRunningHost(helper);
        var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
        ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
        RuntimeHostSpec firstSpec = Spec() with
        {
            Bootstrap = Spec().Bootstrap! with
            {
                GuestComponents =
                [
                    new GuestComponentSpec(
                        GuestComponentKind.ProviderDefined,
                        "extension",
                        Data: Extension([0x10, 0x20, 0x30, 0x40])),
                ],
            },
        };
        RuntimeHostStatus first = await provider.EnsureAsync(metadata, firstSpec, observed: null);
        RuntimeHostSpec changed = firstSpec with
        {
            Bootstrap = firstSpec.Bootstrap! with
            {
                GuestComponents =
                [
                    new GuestComponentSpec(
                        GuestComponentKind.ProviderDefined,
                        "extension",
                        Data: Extension([0x10, 0x20, 0x30, 0x41])),
                ],
            },
        };

        RuntimeHostStatus result = await provider.EnsureAsync(metadata, changed, first);

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RuntimeHostImmutableConfigurationConflict");
    }

    [Fact]
    public async Task Active_vm_fingerprint_includes_all_host_and_bootstrap_policies()
    {
        RuntimeHostSpec baseline = Spec();
        RuntimeHostSpec[] changedSpecs =
        [
            baseline with { SecurityPolicy = baseline.SecurityPolicy with { AllowHostNetwork = true } },
            baseline with { TopologyPolicy = baseline.TopologyPolicy with { AllowHostSharing = false } },
            baseline with { LifecyclePolicy = baseline.LifecyclePolicy with { AutoStart = true } },
            baseline with { HostPolicy = baseline.HostPolicy with { ProtectFromDelete = true } },
            baseline with
            {
                Bootstrap = baseline.Bootstrap! with
                {
                    Provisioning = new RuntimeHostProvisioningSpec(),
                },
            },
            baseline with
            {
                Bootstrap = baseline.Bootstrap! with
                {
                    ReadinessGates =
                    [
                        new ReadinessGateSpec(
                            "engine",
                            ReadinessGateKind.EngineReady,
                            ReadinessGateScope.Engine,
                            new RetryPolicy()),
                    ],
                },
            },
            baseline with
            {
                Bootstrap = baseline.Bootstrap! with
                {
                    RegenerationPolicy = RuntimeHostBootstrapRegenerationPolicy.OnEveryStart,
                },
            },
        ];

        foreach (RuntimeHostSpec changed in changedSpecs)
        {
            var ledger = new AppleVirtualizationProviderStateLedger();
            var helper = new FakeAppleVirtualizationHelperClient();
            EnqueueRunningHost(helper);
            var provider = new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost);
            ResourceMetadata<RuntimeHost> metadata = Metadata("runtime-host-1");
            RuntimeHostStatus first = await provider.EnsureAsync(metadata, baseline, observed: null);

            RuntimeHostStatus result = await provider.EnsureAsync(metadata, changed, first);

            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code.Value == "AppleVirtualization.RuntimeHostImmutableConfigurationConflict");
        }
    }

    private static void EnqueueRunningHost(FakeAppleVirtualizationHelperClient helper)
    {
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Starting));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running));
        helper.EnqueueResponse(GuestAgentReadinessResponse(
            AppleVirtualizationGuestAgentReadinessState.Ready,
            verifiedReady: true));
    }

    private static ProviderExtensionData Extension(byte[] payload) =>
        new(
            AppleVirtualizationProviderDescriptor.ProviderId,
            new SchemaId("hpd.test.runtime-host-extension.v1"),
            new ContentType("application/octet-stream"),
            payload);

    private static ResourceMetadata<RuntimeHost> Metadata(string id) =>
        AppleVirtualizationContractFixtures.Metadata<RuntimeHost>(id, "runtime-host");

    private static RuntimeHostSpec Spec() =>
        AppleVirtualizationContractFixtures.RuntimeHostSpec();

    private static RuntimeHostSpec EngineSpec(string engineName) =>
        Spec() with
        {
            Bootstrap = Spec().Bootstrap! with
            {
                GuestComponents =
                [
                    .. Spec().Bootstrap!.GuestComponents,
                    new GuestComponentSpec(GuestComponentKind.ContainerRuntime, engineName),
                ],
            },
        };

    private static AppleVirtualizationProviderOptions EngineBootstrapOptions(
        bool enabled = false,
        bool authorityModeConfigured = false,
        EngineAuthorityMode authorityMode = EngineAuthorityMode.Rootless,
        AppleVirtualizationEngineObservationState? state = null,
        bool provisioningEnabled = false,
        bool allowPackageInstall = false,
        bool allowServiceEnablement = false,
        TimeSpan? provisioningTimeout = null,
        AppleVirtualizationEngineProvisioningExecutionState scriptedExecutionState =
            AppleVirtualizationEngineProvisioningExecutionState.NotRequested,
        string? scriptedOutput = null,
        string? scriptedStdout = null,
        string? scriptedStderr = null,
        AppleVirtualizationEngineProvisioningPrerequisiteStatus? prerequisites = null) =>
        new()
        {
            EngineBootstrap = new AppleVirtualizationEngineBootstrapOptions
            {
                Enabled = enabled,
                AuthorityModeConfigured = authorityModeConfigured,
                AuthorityMode = authorityMode,
                ScriptedObservationState = state,
                Provisioning = new AppleVirtualizationEngineProvisioningOptions
                {
                    Enabled = provisioningEnabled,
                    AllowPackageInstall = allowPackageInstall,
                    AllowServiceEnablement = allowServiceEnablement,
                    ProvisioningTimeout = provisioningTimeout ?? TimeSpan.FromMinutes(2),
                    ScriptedExecutionState = scriptedExecutionState,
                    ScriptedPrerequisites = prerequisites ?? AppleVirtualizationEngineProvisioningPrerequisiteStatus.Supported,
                    ScriptedOutput = scriptedOutput,
                    ScriptedStdout = scriptedStdout,
                    ScriptedStderr = scriptedStderr,
                },
            },
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableEngineControlPlane = enabled,
            },
        };

    private static AppleVirtualizationProviderOptions ValidationOptions() =>
        new()
        {
            GuestImage = CompleteGuestImage(),
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableRealHelperActivation = true,
                EnableVmConfigurationValidation = true,
            },
        };

    private static AppleVirtualizationProviderOptions RealBootOptions(AppleVirtualizationGuestImageOptions guestImage) =>
        new()
        {
            HelperPath = guestImage.GuestAgentBootstrapPath ?? AppleVirtualizationProviderDescriptor.HelperExecutableName,
            GuestImage = guestImage,
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableRealHelperActivation = true,
                EnableRealVmBoot = true,
            },
        };

    private static AppleVirtualizationGuestImageOptions CompleteGuestImage() =>
        new()
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
        };

    private static AppleVirtualizationHelperEnvelope ValidationResponse(bool passed) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.VmConfigurationValidate,
            RequestId = "response-1",
            ResponseStatus = passed ? AppleVirtualizationHelperResponseStatus.Ok : AppleVirtualizationHelperResponseStatus.Error,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.VmConfigurationValidationResponseSchema,
            VmConfigurationValidationResponse = new AppleVirtualizationVmConfigurationValidationResponse
            {
                Phase = AppleVirtualizationVmConfigurationValidationPhase.Completed,
                State = passed
                    ? AppleVirtualizationVmConfigurationValidationState.Passed
                    : AppleVirtualizationVmConfigurationValidationState.Failed,
                Passed = passed,
                HostRunning = false,
                HpdReady = false,
            },
        };

    private static AppleVirtualizationHelperEnvelope HostResponse(
        AppleVirtualizationHelperOperation operation,
        RuntimeHostPhase phase,
        ResourcePhase? resourcePhase = null,
        bool reachable = false,
        IReadOnlyList<Diagnostic>? diagnostics = null) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            RequestId = "response-1",
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.HostResponseSchema,
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = "runtime-host-1",
                HostPhase = phase,
                Phase = resourcePhase ?? PhaseFor(phase),
                GuestControlReachable = reachable,
                Diagnostics = diagnostics ?? Array.Empty<Diagnostic>(),
            },
        };

    private static AppleVirtualizationHelperEnvelope GuestAgentReadinessResponse(
        AppleVirtualizationGuestAgentReadinessState state,
        bool verifiedReady = false,
        bool transportConnected = false,
        string? message = null,
        string? guestBootId = null,
        ulong guestBootGeneration = 0,
        ulong guestAgentGeneration = 0,
        IReadOnlyList<string>? missingCapabilities = null,
        string? errorCode = null,
        bool retryable = false) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            RequestId = "response-1",
            ResponseStatus = errorCode is null ? AppleVirtualizationHelperResponseStatus.Ok : AppleVirtualizationHelperResponseStatus.Error,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.GuestAgentReadinessResponseSchema,
            Error = errorCode is null
                ? null
                : new AppleVirtualizationHelperError
                {
                    Code = errorCode,
                    Message = message ?? "The guest-agent readiness probe failed.",
                    Operation = "guestAgent.readinessProbe",
                    Retryable = retryable,
                    FailedPhase = "GuestAgentReadiness",
                    Severity = DiagnosticSeverity.Warning,
                },
            GuestAgentReadinessProbeResponse = new AppleVirtualizationGuestAgentReadinessProbeResponse
            {
                HostId = "runtime-host-1",
                State = state,
                TransportState = transportConnected || verifiedReady
                    ? AppleVirtualizationGuestAgentTransportState.Connected
                    : AppleVirtualizationGuestAgentTransportState.NotAttempted,
                VmRunning = true,
                TransportConnected = transportConnected || verifiedReady,
                VerifiedReady = verifiedReady,
                ProtocolVersion = state is AppleVirtualizationGuestAgentReadinessState.Ready
                    or AppleVirtualizationGuestAgentReadinessState.NotReady
                    or AppleVirtualizationGuestAgentReadinessState.IncompatibleAgentVersion
                    ? "1.0"
                    : null,
                AgentVersion = state is AppleVirtualizationGuestAgentReadinessState.Ready
                    or AppleVirtualizationGuestAgentReadinessState.NotReady
                    ? "0.1.0"
                    : state == AppleVirtualizationGuestAgentReadinessState.IncompatibleAgentVersion
                        ? "0.0.0"
                        : null,
                GuestBootId = verifiedReady ? guestBootId ?? "boot-1" : null,
                GuestBootGeneration = verifiedReady ? guestBootGeneration == 0 ? 1UL : guestBootGeneration : 0UL,
                GuestAgentGeneration = verifiedReady ? guestAgentGeneration == 0 ? 1UL : guestAgentGeneration : 0UL,
                MissingCapabilities = missingCapabilities ?? Array.Empty<string>(),
                Message = message,
                Error = errorCode is null
                    ? null
                    : new AppleVirtualizationHelperError
                    {
                        Code = errorCode,
                        Message = message ?? "The guest-agent readiness probe failed.",
                        Operation = "guestAgent.readinessProbe",
                        Retryable = retryable,
                        FailedPhase = "GuestAgentReadiness",
                        Severity = DiagnosticSeverity.Warning,
                    },
            },
        };

    private static AppleVirtualizationHelperEnvelope HostError(
        AppleVirtualizationHelperOperation operation,
        string code) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            RequestId = "response-1",
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Error,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.ErrorSchema,
            Error = new AppleVirtualizationHelperError
            {
                Code = code,
                Message = "The helper failed the host lifecycle operation.",
                Operation = AppleVirtualizationHelperOperationNames.ToWireName(operation),
                FailedPhase = "HostLifecycle",
                Severity = DiagnosticSeverity.Error,
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

    private sealed class BlockingHelperClient : IAppleVirtualizationHelperClient
    {
        public async ValueTask<AppleVirtualizationHelperEnvelope> SendAsync(
            AppleVirtualizationHelperEnvelope request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking helper should only complete through cancellation.");
        }

        public async IAsyncEnumerable<AppleVirtualizationHelperEnvelope> ReadEventsAsync(
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RealBootFiles : IDisposable
    {
        private readonly string _root;

        private RealBootFiles(string root, AppleVirtualizationGuestImageOptions guestImage)
        {
            _root = root;
            GuestImage = guestImage;
        }

        public AppleVirtualizationGuestImageOptions GuestImage { get; }

        public static RealBootFiles Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "hpd-applevz-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string helper = Path.Combine(root, "hpd-vz");
            string kernel = Path.Combine(root, "vmlinuz");
            string initrd = Path.Combine(root, "initrd.img");
            string disk = Path.Combine(root, "root.raw");
            string serial = Path.Combine(root, "logs", "runtime-host.serial.log");
            Directory.CreateDirectory(Path.GetDirectoryName(serial)!);
            File.WriteAllText(helper, "#!/bin/sh\nexit 0\n");
            MakeExecutable(helper);
            File.WriteAllBytes(kernel, [0x48, 0x50, 0x44]);
            File.WriteAllBytes(initrd, [0x48, 0x50, 0x44]);
            File.WriteAllBytes(disk, new byte[4096]);

            return new RealBootFiles(
                root,
                CompleteGuestImage() with
                {
                    BundleRoot = root,
                    KernelPath = kernel,
                    InitrdPath = initrd,
                    DiskImagePath = disk,
                    SerialLogPath = serial,
                    Architecture = AppleVirtualizationGuestArchitectureExpectation.HostNative,
                    GuestAgentBootstrapPath = helper,
                });
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void MakeExecutable(string path)
        {
            try
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch (PlatformNotSupportedException)
            {
            }
        }
    }
}
