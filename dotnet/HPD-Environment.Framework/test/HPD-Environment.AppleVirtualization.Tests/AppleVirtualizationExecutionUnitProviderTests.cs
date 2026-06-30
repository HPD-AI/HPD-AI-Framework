namespace HPD.Environment.AppleVirtualization.Tests;

using System.Text.Json;
using FluentAssertions;
using HPD.Environment.AppleVirtualization.Authority;
using HPD.Environment.AppleVirtualization.ExecutionUnits;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Processes;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationExecutionUnitProviderTests
{
    [Fact]
    public async Task Ensure_unit_assigns_to_preferred_host_and_calls_helper()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready, "/workspace/unit-1"));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ResourceMetadata<ExecutionUnit> metadata = Metadata("unit-1");

        ExecutionUnitStatus status = await provider.EnsureAsync(
            metadata,
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        status.UnitPhase.Should().Be(ExecutionUnitPhase.Ready);
        status.AssignedHost.Should().Be(AppleVirtualizationContractFixtures.RuntimeHostRef());
        helper.Requests.Should().ContainSingle();
        AppleVirtualizationUnitEnsureRequest request = helper.Requests[0].UnitEnsureRequest!;
        request.HostId.Should().Be("runtime-host-1");
        request.UnitId.Should().Be("unit-1");
        request.WorkingDirectory.Should().Be("/hpd/units/unit-1");
        request.Environment.Should().ContainKey("HPD_EXECUTION_USER").WhoseValue.Should().Be("hpd");
    }

    [Fact]
    public async Task Ensure_unit_creates_target_handle_in_ledger()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ResourceMetadata<ExecutionUnit> metadata = Metadata("unit-1");

        ExecutionUnitStatus status = await provider.EnsureAsync(
            metadata,
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        status.Handle.Should().NotBeNull();
        status.Handle!.Value.ProviderGeneration.Should().Be(ledger.ProviderGeneration);
        status.NamespaceHandle.Should().NotBeNull();
        ledger.TryGetExecutionUnit(status.Handle.Value).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Get_unit_status_through_handle_returns_deterministic_status()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitStatus, ExecutionUnitPhase.Running));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ExecutionUnitStatus ensured = await provider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);

        ExecutionUnitStatus status = await provider.GetStatusAsync(ensured.Handle!.Value);

        status.UnitPhase.Should().Be(ExecutionUnitPhase.Running);
        status.Handle.Should().Be(ensured.Handle);
        helper.Requests.Should().HaveCount(2);
        helper.Requests[1].Operation.Should().Be(AppleVirtualizationHelperOperation.UnitStatus);
    }

    [Fact]
    public async Task Unit_records_projected_content_refs_and_context_extension()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready, "/workspace/custom"));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);

        ExecutionUnitStatus status = await provider.EnsureAsync(
            Metadata("unit-1"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        status.RealizedContentProjections.Should().ContainSingle().Which.Id.Value.Should().Be("projection-1");
        status.Extensions.Should().ContainSingle();
        AppleVirtualizationExecutionUnitContextExtension extension = JsonSerializer.Deserialize(
            status.Extensions[0].Payload.Span,
            AppleVirtualizationExecutionUnitJsonContext.Default.AppleVirtualizationExecutionUnitContextExtension)!;
        extension.WorkingDirectory.Should().Be("/workspace/custom");
        extension.ContentProjectionIds.Should().ContainSingle().Which.Should().Be("projection-1");
    }

    [Fact]
    public async Task Stop_unit_transitions_to_stopped_and_calls_helper()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ExecutionUnitStatus ensured = await provider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);

        ExecutionUnitStatus stopped = await provider.StopAsync(ensured.Handle!.Value, StopPolicy.Default);

        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        stopped.Handle.Should().Be(ensured.Handle);
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProjectionRelease,
            AppleVirtualizationHelperOperation.UnitStop);
    }

    [Fact]
    public async Task Stop_unit_stops_owned_processes_releases_owned_projections_then_stops_unit()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var process = SeedProcess(ledger, "process-1", ProcessInvocationPhase.Running);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitStop, ExecutionUnitPhase.Stopped));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ResourceMetadata<ExecutionUnit> metadata = Metadata("unit-1");
        ExecutionUnitStatus ensured = await provider.EnsureAsync(metadata, AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ledger.UpsertExecutionUnit(metadata, ensured with
        {
            ActiveProcesses = [process.Resource],
            RealizedContentProjections = [AppleVirtualizationContractFixtures.ContentProjectionRef()],
        });

        ExecutionUnitStatus stopped = await provider.StopAsync(ensured.Handle!.Value, StopPolicy.Default);

        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        stopped.ActiveProcesses.Should().BeEmpty();
        stopped.RealizedContentProjections.Should().BeEmpty();
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProcessStop,
            AppleVirtualizationHelperOperation.ProjectionRelease,
            AppleVirtualizationHelperOperation.UnitStop);
        ledger.TryGetProcessInvocation(process.Resource).Entry!.Status.ProcessPhase.Should().Be(ProcessInvocationPhase.Stopped);
        ledger.TryGetContentProjection(AppleVirtualizationContractFixtures.ContentProjectionRef()).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_unit_revokes_authority_before_process_and_projection_cleanup()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var process = SeedProcess(ledger, "process-1", ProcessInvocationPhase.Running);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        var authorityProvider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper);
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper, projectionProvider: null, authorityProvider);
        ResourceMetadata<ExecutionUnit> metadata = Metadata("unit-1");
        ExecutionUnitStatus ensured = await provider.EnsureAsync(metadata, AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        await authorityProvider.EnsureAuthorityBindingAsync(
            AppleVirtualizationContractFixtures.Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            AuthoritySpec(ensured.Handle!.Value),
            observed: null);
        ledger.UpsertExecutionUnit(metadata, ensured with
        {
            ActiveProcesses = [process.Resource],
            RealizedContentProjections = [AppleVirtualizationContractFixtures.ContentProjectionRef()],
            AuthorityBindings = [AuthorityRef()],
        });

        ExecutionUnitStatus stopped = await provider.StopAsync(ensured.Handle.Value, StopPolicy.Default);

        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        stopped.AuthorityBindings.Should().BeEmpty();
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.AuthorityBind,
            AppleVirtualizationHelperOperation.AuthorityRevoke,
            AppleVirtualizationHelperOperation.ProcessStop,
            AppleVirtualizationHelperOperation.ProjectionRelease,
            AppleVirtualizationHelperOperation.UnitStop);
        ledger.GetAuthorityAuditEvents(AuthorityRef()).Should().Contain(audit => audit.Kind == AuthorityAuditKind.Revoked);
    }

    [Fact]
    public async Task Stop_unit_cleans_process_and_projection_refs_recorded_by_provider_operations()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        var unitProvider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        var processProvider = new AppleVirtualizationProcessProvider(ledger, helper);
        ExecutionUnitStatus ensured = await unitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        IProcessInvocationHandle process = await processProvider.StartAsync(AppleVirtualizationContractFixtures.ProcessInvocationSpec(ensured.Handle!.Value));
        ledger.TryGetExecutionUnit(ensured.Handle.Value).Entry!.Status.ActiveProcesses
            .Should().ContainSingle().Which.Id.Value.Should().Be(process.Resource!.Value.Id.Value);

        ExecutionUnitStatus stopped = await unitProvider.StopAsync(ensured.Handle.Value, StopPolicy.Default);

        stopped.ActiveProcesses.Should().BeEmpty();
        stopped.RealizedContentProjections.Should().BeEmpty();
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProcessStart,
            AppleVirtualizationHelperOperation.ProcessStop,
            AppleVirtualizationHelperOperation.ProjectionRelease,
            AppleVirtualizationHelperOperation.UnitStop);
    }

    [Fact]
    public async Task Stop_unit_cleanup_failure_returns_degraded_without_releasing_projection_or_stopping_unit()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var process = SeedProcess(ledger, "process-1", ProcessInvocationPhase.Running);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        helper.EnqueueResponse(ErrorResponse(AppleVirtualizationHelperOperation.ProcessStop, "AppleVirtualization.ProcessStopFailed"));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ResourceMetadata<ExecutionUnit> metadata = Metadata("unit-1");
        ExecutionUnitStatus ensured = await provider.EnsureAsync(metadata, AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ledger.UpsertExecutionUnit(metadata, ensured with
        {
            ActiveProcesses = [process.Resource],
            RealizedContentProjections = [AppleVirtualizationContractFixtures.ContentProjectionRef()],
        });

        ExecutionUnitStatus stopped = await provider.StopAsync(ensured.Handle!.Value, StopPolicy.Default);

        stopped.Phase.Should().Be(ResourcePhase.Degraded);
        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopping);
        stopped.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.ExecutionUnitCleanupFailed");
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProcessStop);
        ledger.TryGetContentProjection(AppleVirtualizationContractFixtures.ContentProjectionRef()).Succeeded.Should().BeTrue();
        ledger.TryGetExecutionUnit(ensured.Handle.Value).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_unit_releases_ledger_state_idempotently()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitDelete, ExecutionUnitPhase.Deleted));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ResourceMetadata<ExecutionUnit> metadata = Metadata("unit-1");
        ExecutionUnitStatus ensured = await provider.EnsureAsync(metadata, AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        var unit = new ResourceRef<ExecutionUnit>(metadata.Id, metadata.Scope, metadata.Generation);

        await provider.DeleteAsync(unit);
        await provider.DeleteAsync(unit);

        ledger.TryGetExecutionUnit(ensured.Handle!.Value).Diagnostic!.Code.Should().Be(AppleVirtualizationHandleDiagnostics.MissingHandle);
        helper.Requests.Count(request => request.Operation == AppleVirtualizationHelperOperation.UnitDelete).Should().Be(1);
    }

    [Fact]
    public async Task Delete_unit_releases_owned_projection_and_preserves_shared_host()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ResourceMetadata<ExecutionUnit> metadata = Metadata("unit-1");
        ExecutionUnitStatus ensured = await provider.EnsureAsync(metadata, AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        var unit = new ResourceRef<ExecutionUnit>(metadata.Id, metadata.Scope, metadata.Generation);

        await provider.DeleteAsync(unit);

        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProjectionRelease,
            AppleVirtualizationHelperOperation.UnitDelete);
        ledger.TryGetExecutionUnit(unit).Succeeded.Should().BeFalse();
        ledger.TryGetContentProjection(AppleVirtualizationContractFixtures.ContentProjectionRef()).Succeeded.Should().BeFalse();
        ledger.TryGetRuntimeHost(AppleVirtualizationContractFixtures.RuntimeHostRef()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Stop_one_unit_does_not_release_projection_referenced_by_another_unit()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitStop, ExecutionUnitPhase.Stopped));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ResourceMetadata<ExecutionUnit> metadata = Metadata("unit-1");
        ExecutionUnitStatus ensured = await provider.EnsureAsync(metadata, AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ledger.UpsertExecutionUnit(Metadata("unit-2"), ensured with
        {
            ActiveProcesses = Array.Empty<ResourceRef<ProcessInvocation>>(),
            RealizedContentProjections = [AppleVirtualizationContractFixtures.ContentProjectionRef()],
        });

        ExecutionUnitStatus stopped = await provider.StopAsync(ensured.Handle!.Value, StopPolicy.Default);

        stopped.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        helper.Requests.Select(request => request.Operation).Should().Equal(
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.UnitStop);
        ledger.TryGetContentProjection(AppleVirtualizationContractFixtures.ContentProjectionRef()).Succeeded.Should().BeTrue();
        ledger.TryGetExecutionUnit(AppleVirtualizationContractFixtures.ExecutionUnitRef("unit-2")).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Stale_handle_behavior_returns_structured_diagnostic_status()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        ExecutionUnitStatus ensured = await provider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ledger.AdvanceProviderGeneration();

        ExecutionUnitStatus status = await provider.GetStatusAsync(ensured.Handle!.Value);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.UnitPhase.Should().Be(ExecutionUnitPhase.Failed);
        status.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(AppleVirtualizationHandleDiagnostics.StaleHandle);
    }

    [Fact]
    public async Task Missing_host_behavior_returns_structured_diagnostic()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, new FakeAppleVirtualizationHelperClient());

        ExecutionUnitStatus status = await provider.EnsureAsync(
            Metadata("unit-1"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.UnitPhase.Should().Be(ExecutionUnitPhase.Failed);
        status.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(AppleVirtualizationHandleDiagnostics.MissingHandle);
    }

    [Fact]
    public async Task Ensure_unit_waits_for_host_readiness_before_helper_call()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger, hostPhase: RuntimeHostPhase.Running, phase: ResourcePhase.Ready, ready: false);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);

        ExecutionUnitStatus status = await provider.EnsureAsync(
            Metadata("unit-1"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.UnitPhase.Should().Be(ExecutionUnitPhase.Declared);
        status.Diagnostics.Should().ContainSingle().Which.Code.Value.Should().Be("AppleVirtualization.ExecutionUnitHostNotReady");
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.ExecutionUnitHostReady" &&
            condition.Status == ConditionStatus.False);
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Ensure_unit_waits_for_required_projection_before_helper_call()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);

        ExecutionUnitStatus status = await provider.EnsureAsync(
            Metadata("unit-1"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.UnitPhase.Should().Be(ExecutionUnitPhase.ProjectingContent);
        status.RealizedContentProjections.Should().BeEmpty();
        status.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(AppleVirtualizationHandleDiagnostics.MissingHandle);
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.ExecutionUnitProjectionsReady" &&
            condition.Status == ConditionStatus.False);
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Ensure_unit_waits_until_required_projection_is_projected()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger, projectionPhase: ContentProjectionPhase.Projecting, phase: ResourcePhase.Reconciling);
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);

        ExecutionUnitStatus status = await provider.EnsureAsync(
            Metadata("unit-1"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Reconciling);
        status.UnitPhase.Should().Be(ExecutionUnitPhase.ProjectingContent);
        status.Diagnostics.Should().ContainSingle().Which.Code.Value.Should().Be("AppleVirtualization.ExecutionUnitProjectionNotProjected");
        helper.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExecutionUnitPhase.Declared, ResourcePhase.Reconciling)]
    [InlineData(ExecutionUnitPhase.ProjectingContent, ResourcePhase.Reconciling)]
    [InlineData(ExecutionUnitPhase.Ready, ResourcePhase.Ready)]
    [InlineData(ExecutionUnitPhase.Running, ResourcePhase.Ready)]
    [InlineData(ExecutionUnitPhase.Stopping, ResourcePhase.Deleting)]
    [InlineData(ExecutionUnitPhase.Stopped, ResourcePhase.Ready)]
    [InlineData(ExecutionUnitPhase.Deleting, ResourcePhase.Deleting)]
    [InlineData(ExecutionUnitPhase.Deleted, ResourcePhase.Deleted)]
    [InlineData(ExecutionUnitPhase.Failed, ResourcePhase.Failed)]
    public async Task Ensure_unit_phase_transitions_map_deterministically(
        ExecutionUnitPhase unitPhase,
        ResourcePhase resourcePhase)
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, unitPhase));
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);

        ExecutionUnitStatus status = await provider.EnsureAsync(
            Metadata("unit-1"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        status.UnitPhase.Should().Be(unitPhase);
        status.Phase.Should().Be(resourcePhase);
    }

    [Fact]
    public async Task Wrong_kind_handle_behavior_returns_structured_diagnostic_status()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        var process = ledger.UpsertProcessInvocation(
            AppleVirtualizationContractFixtures.Metadata<ProcessInvocation>("process-1", "process-invocation"),
            new ProcessInvocationStatus
            {
                Phase = ResourcePhase.Ready,
                ProcessPhase = ProcessInvocationPhase.Running,
            });
        var wrongKind = new TargetHandle<ExecutionUnit>(
            process.TargetHandle.Route,
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Control,
            process.TargetHandle.ProviderGeneration);
        var provider = new AppleVirtualizationExecutionUnitProvider(ledger, new FakeAppleVirtualizationHelperClient());

        ExecutionUnitStatus status = await provider.GetStatusAsync(wrongKind);

        status.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(AppleVirtualizationHandleDiagnostics.WrongHandleKind);
    }

    private static ResourceMetadata<ExecutionUnit> Metadata(string id) =>
        AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>(id, "execution-unit");

    private static ResourceRef<AuthorityBinding> AuthorityRef() =>
        new(new ResourceId<AuthorityBinding>("authority-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private static AuthorityBindingSpec AuthoritySpec(TargetHandle<ExecutionUnit> targetUnit) =>
        new()
        {
            Kind = AuthorityBindingKind.HostService,
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.HostService,
                HostService = HostServiceKind.SshAgent,
                Locus = BoundaryLocus.Host,
            },
            Target = new AuthorityBindingTarget(AuthorityTargetKind.ExecutionUnit, Unit: targetUnit),
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.SocketPath,
                TargetSocketPath = new UnixSocketPath("/run/hpd/ssh-agent.sock"),
                ReadOnly = true,
            },
            Policy = new AuthorityBindingPolicy
            {
                AuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                EffectiveAuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                Redaction = SensitiveRedactionLevel.RedactSecretValues,
                RequireAudit = true,
            },
        };

    private static void SeedHost(
        AppleVirtualizationProviderStateLedger ledger,
        RuntimeHostPhase hostPhase = RuntimeHostPhase.Ready,
        ResourcePhase phase = ResourcePhase.Ready,
        bool ready = true)
    {
        ResourceMetadata<RuntimeHost> hostMetadata =
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host");
        ledger.UpsertRuntimeHost(hostMetadata, new RuntimeHostStatus
        {
            Phase = phase,
            ObservedGeneration = hostMetadata.Generation,
            HostPhase = hostPhase,
            GuestControl = new GuestControlStatus(
                Expected: true,
                Installed: true,
                Reachable: ready,
                Transport: ProviderTransportKind.Vsock),
            Readiness = new RuntimeHostReadinessStatus(Ready: ready),
        });
    }

    private static void SeedProjectedProjection(
        AppleVirtualizationProviderStateLedger ledger,
        ContentProjectionPhase projectionPhase = ContentProjectionPhase.Projected,
        ResourcePhase phase = ResourcePhase.Ready)
    {
        ResourceMetadata<ContentProjection> projectionMetadata =
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-1", "content-projection");
        ledger.UpsertContentProjection(projectionMetadata, new ContentProjectionStatus
        {
            Phase = phase,
            ObservedGeneration = projectionMetadata.Generation,
            ProjectionPhase = projectionPhase,
            Views =
            [
                new RealizedProjectionView
                {
                    Kind = ProjectionViewKind.FilesystemTree,
                    GuestPath = new GuestPath("/workspace"),
                    EffectiveAccess = AccessMode.ReadOnly,
                    EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                    EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                    EffectiveCoherence = CoherenceClass.CloseToOpen,
                },
            ],
        });
    }

    private static AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> SeedProcess(
        AppleVirtualizationProviderStateLedger ledger,
        string id,
        ProcessInvocationPhase processPhase)
    {
        ResourceMetadata<ProcessInvocation> metadata =
            AppleVirtualizationContractFixtures.Metadata<ProcessInvocation>(id, "process-invocation");
        return ledger.UpsertProcessInvocation(metadata, new ProcessInvocationStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ProcessPhase = processPhase,
            IoState = ProcessIoState.Open,
            StartedAt = DateTimeOffset.UtcNow,
        });
    }

    private static AppleVirtualizationHelperEnvelope UnitResponse(
        AppleVirtualizationHelperOperation operation,
        ExecutionUnitPhase phase,
        string? workingDirectory = null) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            RequestId = "response-1",
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

    private static AppleVirtualizationHelperEnvelope ErrorResponse(
        AppleVirtualizationHelperOperation operation,
        string code) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            RequestId = "error-1",
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Error,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.ErrorSchema,
            Error = new AppleVirtualizationHelperError
            {
                Code = code,
                Message = code,
                Severity = DiagnosticSeverity.Error,
                Operation = AppleVirtualizationHelperOperationNames.ToWireName(operation),
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
}
