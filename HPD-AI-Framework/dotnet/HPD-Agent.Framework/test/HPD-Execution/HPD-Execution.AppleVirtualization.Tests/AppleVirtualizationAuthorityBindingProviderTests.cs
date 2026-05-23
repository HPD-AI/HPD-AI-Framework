namespace HPD.Execution.AppleVirtualization.Tests;

using FluentAssertions;
    using HPD.Execution.AppleVirtualization.Authority;
    using HPD.Execution.AppleVirtualization.GuestAgent;
    using HPD.Execution.AppleVirtualization.Networks;
    using HPD.Execution.AppleVirtualization.Protocol;
    using HPD.Execution.AppleVirtualization.State;
    using HPD.Execution.AppleVirtualization.Tests.Fixtures;
    using HPD.Execution.Contracts;
    using HPD.Execution.Runtime;
    using Xunit;

public sealed class AppleVirtualizationAuthorityBindingProviderTests
{
    private static readonly DateTimeOffset ClockStart = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Authority_binding_creates_lease_with_expected_lifetime_and_expiry()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        AuthorityBindingSpec spec = SshAgentSpec(unit.TargetHandle, expiresAfter: TimeSpan.FromMinutes(5));

        AuthorityBindingStatus status = await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            spec,
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        status.BoundAuthority.Should().NotBeNull();
        status.BoundAuthority!.BoundAt.Should().Be(ClockStart);
        status.BoundAuthority.ExpiresAt.Should().Be(ClockStart.AddMinutes(5));
        status.BoundAuthority.RevocationStatus.Should().Be(RevocationVerificationStatus.Pending);
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.AuthorityAuditRecorded" &&
            condition.Status == ConditionStatus.True);
        helper.Requests.Should().ContainSingle(request =>
            request.Operation == AppleVirtualizationHelperOperation.AuthorityBind &&
            request.AuthorityBindingRequest!.Lease.Lifetime == BindingLifetime.ExecutionUnit &&
            request.AuthorityBindingRequest.Lease.ExpiresAt == ClockStart.AddMinutes(5));
    }

    [Fact]
    public async Task Endpoint_sensitive_policy_without_explicit_authority_binding_remains_rejected()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedReadyMembership(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        var endpointProvider = new AppleVirtualizationEndpointPublicationProvider(ledger, helper);

        PublishedEndpointStatus status = await endpointProvider.EnsurePublishedEndpointAsync(
            Metadata<PublishedEndpoint>("endpoint-1", "published-endpoint"),
            SensitiveEndpointSpec(),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.EndpointPhase.Should().Be(PublishedEndpointPhase.Failed);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.EndpointSshAgentRequiresAuthorityBinding");
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Authority_bound_sensitive_endpoint_gets_audit_correlation_and_redaction()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        AuthorityBindingStatus status = await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Ready);
        status.BoundAuthority.Should().NotBeNull();
        status.BoundAuthority!.AuditCorrelationId.Should().Be("authority-authority-1");
        status.BoundAuthority.EffectiveAuthorityClass.Should().Be(SensitiveAuthorityClass.CredentialDelegation);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.AuthoritySourceClassified");
        helper.Requests.Single().AuthorityBindingRequest!.Source.RedactedDisplayName.Should().Be("ssh-agent:***");
        ledger.GetAuthorityAuditEvents(AuthorityRef()).Should().ContainSingle(audit =>
            audit.Kind == AuthorityAuditKind.Projected &&
            audit.CorrelationId == "authority-authority-1");
    }

    [Fact]
    public async Task Authority_bind_retries_retryable_helper_failures_before_failing_closed()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        helper.EnqueueResponse(RetryableAuthorityBindError());
        helper.EnqueueResponse(RetryableAuthorityBindError());
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(
            ledger,
            helper,
            () => ClockStart,
            bindRetryDelay: TimeSpan.Zero);

        AuthorityBindingStatus status = await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);

        status.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        helper.Requests.Count(request => request.Operation == AppleVirtualizationHelperOperation.AuthorityBind)
            .Should().Be(3);
    }

    [Fact]
    public async Task Lease_expiry_prevents_use()
    {
        DateTimeOffset current = ClockStart;
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => current);

        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle, expiresAfter: TimeSpan.FromSeconds(1)),
            observed: null);
        current = ClockStart.AddSeconds(2);

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Degraded);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Pending);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.AuthorityLeaseExpired");
    }

    [Fact]
    public async Task Target_stop_marks_revocation_pending_when_policy_requires_it()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = ledger.UpsertExecutionUnit(
            Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                UnitPhase = ExecutionUnitPhase.Ready,
            });
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        ledger.UpsertExecutionUnit(
            Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                UnitPhase = ExecutionUnitPhase.Stopped,
            });

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());

        status.Phase.Should().Be(ResourcePhase.Deleting);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoking);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Pending);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.AuthorityTargetStoppedRevocationPending");
    }

    [Fact]
    public async Task Get_status_refreshes_projected_binding_through_helper()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());

        status.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        helper.Requests.Should().Contain(request =>
            request.Operation == AppleVirtualizationHelperOperation.AuthorityStatus &&
            request.AuthorityBindingRequest!.Action == AppleVirtualizationAuthorityBindingAction.Status);
    }

    [Fact]
    public async Task Get_status_maps_guest_degraded_authority_evidence_to_provider_diagnostics()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityStatusResponse(
            "authority-1",
            AuthorityBindingPhase.Degraded,
            "AppleVirtualization.GuestAgentAuthorityTargetMissing",
            "Projected authority socket path is missing."));

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());

        status.Phase.Should().Be(ResourcePhase.Degraded);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Degraded);
        status.Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.GuestAgentAuthorityTargetMissing");
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestAgentAuthorityTargetMissing");
    }

    [Theory]
    [InlineData("AppleVirtualization.GuestAgentAuthoritySourceMissing")]
    [InlineData("AppleVirtualization.GuestAgentAuthorityTargetMissing")]
    [InlineData("AppleVirtualization.GuestAgentAuthorityTargetUnmanaged")]
    [InlineData("AppleVirtualization.GuestAgentAuthorityWrongTarget")]
    public async Task Get_status_maps_each_guest_degraded_authority_condition_to_diagnostics(string conditionType)
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityStatusResponse(
            "authority-1",
            AuthorityBindingPhase.Degraded,
            conditionType,
            "Guest authority projection is degraded."));

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());

        status.BindingPhase.Should().Be(AuthorityBindingPhase.Degraded);
        status.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == conditionType);
        EvidenceExtension(status).Conditions.Should().Contain(condition => condition.Type == conditionType);
    }

    [Fact]
    public async Task Get_status_persists_bounded_authority_evidence_extension()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityStatusResponse(
            "authority-1",
            AuthorityBindingPhase.Degraded,
            "AppleVirtualization.GuestAgentAuthorityTargetMissing",
            "Projected authority socket path is missing.",
            [RevocationEvidence(AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketAbsent, observed: true)]));

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());

        AppleVirtualizationAuthorityEvidenceExtension evidence = EvidenceExtension(status);
        evidence.BindingId.Should().Be("authority-1");
        evidence.BindingPhase.Should().Be(AuthorityBindingPhase.Degraded);
        evidence.Conditions.Should().ContainSingle(condition =>
            condition.Type == "AppleVirtualization.GuestAgentAuthorityTargetMissing");
        evidence.RevocationEvidence.Should().ContainSingle(item =>
            item.Kind == AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketAbsent &&
            item.GuestSocketPath!.Value.Value == "/run/hpd/ssh-agent.sock");
    }

    [Fact]
    public async Task Revoke_persists_revocation_evidence_extension()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityRevokeResponse(
            "authority-1",
            RevocationVerificationStatus.Unknown,
            AuthorityBindingPhase.Revoked,
            [RevocationEvidence(AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketAbsent, observed: true)]));

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        AppleVirtualizationAuthorityEvidenceExtension evidence = EvidenceExtension(status);
        evidence.BindingPhase.Should().Be(AuthorityBindingPhase.Revoked);
        evidence.RevocationEvidence.Should().ContainSingle(item =>
            item.Kind == AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketAbsent &&
            item.Observed);
    }

    [Fact]
    public async Task Revoke_maps_guest_condition_to_provider_diagnostics_and_evidence_extension()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityRevokeResponse(
            "authority-1",
            RevocationVerificationStatus.Unknown,
            AuthorityBindingPhase.Revoked,
            [RevocationEvidence(AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketPresent, observed: true)],
            "AppleVirtualization.GuestAgentAuthorityRevokeIncomplete",
            "Authority socket projection still exists after revoke."));

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoking);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Failed);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.AuthorityRevocationVerificationFailed");
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.GuestAgentAuthorityRevokeIncomplete");
        EvidenceExtension(status).Conditions.Should().Contain(condition =>
            condition.Type == "AppleVirtualization.GuestAgentAuthorityRevokeIncomplete");
    }

    [Fact]
    public async Task Secret_values_do_not_appear_in_diagnostics_audit_status_or_helper_dto()
    {
        const string sentinel = "super-secret-token-value";
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        AuthorityBindingStatus status = await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            CredentialSpec(new CredentialRef("credential-ref-without-secret"), unit.TargetHandle),
            observed: null);

        string statusText = string.Join("|", status.Diagnostics.Select(diagnostic => diagnostic.Message));
        statusText.Should().NotContain(sentinel);
        status.BoundAuthority!.AuditCorrelationId.Should().NotContain(sentinel);
        ledger.GetAuthorityAuditEvents(AuthorityRef()).Select(audit => audit.Actor).Should().NotContain(sentinel);
        helper.Requests.Single().AuthorityBindingRequest!.Source.Credential.Should().BeNull();
        helper.Requests.Single().AuthorityBindingRequest!.Source.RedactedDisplayName.Should().Be("credential:***");
    }

    [Fact]
    public async Task Provider_registration_includes_authority_binding_but_still_excludes_engine_control_plane()
    {
        var registry = new HPD.Execution.Runtime.ExecutionProviderRegistry();

        registry.RegisterAppleVirtualizationProvider();
        ProviderDescriptor descriptor = (await registry.ListAsync()).Single();

        descriptor.ContractKinds.Should().HaveFlag(ProviderContractKind.AuthorityBinding);
        descriptor.ContractKinds.Should().NotHaveFlag(ProviderContractKind.EngineControlPlane);
        registry.AuthorityBindingProviders.Should().ContainSingle();
        registry.EngineControlPlaneProviders.Should().BeEmpty();
    }

    [Fact]
    public async Task Authority_binding_appears_on_execution_unit_status_only_after_successful_projection()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        AuthorityBindingStatus status = await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);

        status.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings
            .Should().ContainSingle()
            .Which.Id.Value.Should().Be("authority-1");
    }

    [Fact]
    public async Task Socket_projection_requires_ready_execution_unit_target_without_helper_dispatch()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = ledger.UpsertExecutionUnit(
            Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Reconciling,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Declared,
            });
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        AuthorityBindingStatus status = await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.AuthorityTargetUnitNotReady");
        helper.Requests.Should().BeEmpty();
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().BeEmpty();
    }

    [Fact]
    public async Task Environment_reference_projection_redacts_values_and_records_environment_name_only()
    {
        const string sentinel = "credential-secret-value";
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        AuthorityBindingStatus status = await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            CredentialSpec(new CredentialRef("credential-ref-without-secret"), unit.TargetHandle),
            observed: null);

        status.BoundAuthority!.EnvironmentVariableName.Should().Be("HPD_CREDENTIAL_PROXY");
        status.BoundAuthority.TargetSocketPath.Should().BeNull();
        string serializedRequest = System.Text.Json.JsonSerializer.Serialize(
            helper.Requests.Single(),
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        serializedRequest.Should().NotContain(sentinel);
        serializedRequest.Should().NotContain("credential-ref-without-secret");
        serializedRequest.Should().Contain("HPD_CREDENTIAL_PROXY");
    }

    [Fact]
    public async Task Unsupported_projection_kinds_fail_without_helper_dispatch()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        AuthorityBindingSpec spec = SshAgentSpec(unit.TargetHandle) with
        {
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.FileDescriptor,
                ReadOnly = true,
            },
        };

        AuthorityBindingStatus status = await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            spec,
            observed: null);

        status.Phase.Should().Be(ResourcePhase.Failed);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.AuthorityProjectionUnsupported");
        helper.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Authority_binding_does_not_leak_to_unrelated_execution_unit()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit1 = SeedReadyUnit(ledger);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit2 = ledger.UpsertExecutionUnit(
            Metadata<ExecutionUnit>("unit-2", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Ready,
            });
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);

        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit1.TargetHandle),
            observed: null);

        ledger.TryGetExecutionUnit(unit1.Resource).Entry!.Status.AuthorityBindings.Should().ContainSingle();
        ledger.TryGetExecutionUnit(unit2.Resource).Entry!.Status.AuthorityBindings.Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_projected_binding_transitions_to_revoked_verified()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        status.Phase.Should().Be(ResourcePhase.Deleted);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoked);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Verified);
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().BeEmpty();
        ledger.GetAuthorityAuditEvents(AuthorityRef()).Should().Contain(audit => audit.Kind == AuthorityAuditKind.Revoked);
    }

    [Fact]
    public async Task Revoke_pending_when_helper_cannot_verify()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityRevokeResponse("authority-1", RevocationVerificationStatus.Pending, AuthorityBindingPhase.Revoking));

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        status.Phase.Should().Be(ResourcePhase.Deleting);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoking);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Pending);
        status.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.AuthorityRevocationPending");
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().ContainSingle();
    }

    [Theory]
    [InlineData(AppleVirtualizationAuthorityRevocationEvidenceKind.ListenerRemoved, null)]
    [InlineData(AppleVirtualizationAuthorityRevocationEvidenceKind.ConnectionFileDescriptorClosed, -1)]
    [InlineData(AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketAbsent, null)]
    public async Task Revoke_verified_teardown_evidence_transitions_to_revoked_verified(
        AppleVirtualizationAuthorityRevocationEvidenceKind evidenceKind,
        int? fileDescriptor)
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityRevokeResponse(
            "authority-1",
            RevocationVerificationStatus.Unknown,
            AuthorityBindingPhase.Revoked,
            [RevocationEvidence(evidenceKind, observed: true, fileDescriptor)]));

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoked);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Verified);
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_pending_teardown_evidence_stays_unusable_but_not_verified()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityRevokeResponse(
            "authority-1",
            RevocationVerificationStatus.Unknown,
            AuthorityBindingPhase.Revoking,
            [RevocationEvidence(AppleVirtualizationAuthorityRevocationEvidenceKind.ListenerRemoved, observed: false)]));

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoking);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Pending);
        status.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.AuthorityRevocationPending");
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().ContainSingle();
    }

    [Fact]
    public async Task Revoke_failed_teardown_evidence_maps_to_failed_without_detaching()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityRevokeResponse(
            "authority-1",
            RevocationVerificationStatus.Unknown,
            AuthorityBindingPhase.Revoked,
            [RevocationEvidence(AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketPresent, observed: true)]));

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoking);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Failed);
        status.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.AuthorityRevocationVerificationFailed");
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().ContainSingle();
    }

    [Fact]
    public async Task Revoke_unsupported_teardown_evidence_maps_to_not_supported_without_detaching()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityRevokeResponse(
            "authority-1",
            RevocationVerificationStatus.Unknown,
            AuthorityBindingPhase.Revoked,
            [RevocationEvidence(AppleVirtualizationAuthorityRevocationEvidenceKind.Unsupported, observed: true)]));

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoking);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.NotSupported);
        status.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.AuthorityRevocationVerificationUnsupported");
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().ContainSingle();
    }

    [Fact]
    public async Task Revoke_maps_revoked_unknown_helper_evidence_to_not_supported()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        helper.EnqueueResponse(AuthorityRevokeResponse("authority-1", RevocationVerificationStatus.Unknown, AuthorityBindingPhase.Revoked));

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        status.Phase.Should().Be(ResourcePhase.Deleting);
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoking);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.NotSupported);
        status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.AuthorityRevocationVerificationUnsupported");
        ledger.TryGetExecutionUnit(unit.Resource).Entry!.Status.AuthorityBindings.Should().ContainSingle();
    }

    [Fact]
    public async Task Repeated_revoke_is_idempotent_after_verified_revocation()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());
        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        helper.Requests.Where(request => request.Operation == AppleVirtualizationHelperOperation.AuthorityRevoke)
            .Should().ContainSingle();
        AuthorityBindingStatus status = await provider.GetStatusAsync(AuthorityRef());
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoked);
        status.BoundAuthority!.RevocationStatus.Should().Be(RevocationVerificationStatus.Verified);
    }

    [Fact]
    public async Task Stale_binding_generation_fails_deterministically_without_helper_dispatch()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        var stale = new ResourceRef<AuthorityBinding>(
            new ResourceId<AuthorityBinding>("authority-1"),
            AppleVirtualizationContractFixtures.RuntimeScope,
            new ResourceGeneration(2));

        Func<Task> act = async () => await provider.RevokeAuthorityBindingAsync(stale);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*generation*");
        helper.Requests.Where(request => request.Operation == AppleVirtualizationHelperOperation.AuthorityRevoke)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Runtime_finalization_revokes_runtime_scope_authority_bindings()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        var registry = new ExecutionProviderRegistry();
        registry.RegisterModule(new AppleVirtualizationProviderModule(
            new AppleVirtualizationProviderOptions { HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake },
            helper,
            ledger));
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = SeedReadyUnit(ledger);
        var authorityProvider = (AppleVirtualizationAuthorityBindingProvider)registry.AuthorityBindingProviders.Single();
        await authorityProvider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit.TargetHandle),
            observed: null);
        var runtime = new InMemoryExecutionRuntime(registry);

        RuntimeFinalizationResult result = await runtime.FinalizeRuntimeAsync(
            new RuntimeFinalizationRequest(AppleVirtualizationContractFixtures.RuntimeScope, PromoteMemory: false, CleanupPolicy.Default));

        result.RetainedResources.Should().BeEmpty();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "hpd.execution.runtime.finalized");
        AuthorityBindingStatus status = await authorityProvider.GetStatusAsync(AuthorityRef());
        status.BindingPhase.Should().Be(AuthorityBindingPhase.Revoked);
        helper.Requests.Should().Contain(request => request.Operation == AppleVirtualizationHelperOperation.AuthorityRevoke);
    }

    [Fact]
    public async Task Revoke_only_affects_requested_binding()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit1 = SeedReadyUnit(ledger);
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit2 = ledger.UpsertExecutionUnit(
            Metadata<ExecutionUnit>("unit-2", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Ready,
            });
        var provider = new AppleVirtualizationAuthorityBindingProvider(ledger, helper, () => ClockStart);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-1", "authority-binding"),
            SshAgentSpec(unit1.TargetHandle),
            observed: null);
        await provider.EnsureAuthorityBindingAsync(
            Metadata<AuthorityBinding>("authority-2", "authority-binding"),
            SshAgentSpec(unit2.TargetHandle),
            observed: null);

        await provider.RevokeAuthorityBindingAsync(AuthorityRef());

        AuthorityBindingStatus revoked = await provider.GetStatusAsync(AuthorityRef());
        AuthorityBindingStatus retained = await provider.GetStatusAsync(new ResourceRef<AuthorityBinding>(
            new ResourceId<AuthorityBinding>("authority-2"),
            AppleVirtualizationContractFixtures.RuntimeScope,
            new ResourceGeneration(1)));
        revoked.BindingPhase.Should().Be(AuthorityBindingPhase.Revoked);
        retained.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        ledger.TryGetExecutionUnit(unit1.Resource).Entry!.Status.AuthorityBindings.Should().BeEmpty();
        ledger.TryGetExecutionUnit(unit2.Resource).Entry!.Status.AuthorityBindings.Should().ContainSingle();
    }

    private static ResourceMetadata<TResource> Metadata<TResource>(string id, string kind)
        where TResource : IExecutionResourceMarker =>
        AppleVirtualizationContractFixtures.Metadata<TResource>(id, kind);

    private static ResourceRef<AuthorityBinding> AuthorityRef() =>
        new(new ResourceId<AuthorityBinding>("authority-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private static AppleVirtualizationHelperEnvelope RetryableAuthorityBindError() =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.AuthorityBind,
            "authority-test-error",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.AuthorityBindingRequestSchema).ToErrorResponse(
            sequenceNumber: 2,
            new AppleVirtualizationHelperError
            {
                Code = "AppleVirtualization.GuestAgentAuthoritySourceSocketMissing",
                Message = "Guest engine source socket is missing.",
                Operation = "authority.bind",
                Retryable = true,
                FailedPhase = "GuestAuthority",
            });

    private static AuthorityBindingSpec SshAgentSpec(
        TargetHandle<ExecutionUnit>? unit = null,
        TimeSpan? expiresAfter = null) =>
        new()
        {
            Kind = AuthorityBindingKind.HostService,
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.HostService,
                HostService = HostServiceKind.SshAgent,
                Locus = BoundaryLocus.Host,
            },
            Target = new AuthorityBindingTarget(
                AuthorityTargetKind.ExecutionUnit,
                Unit: unit ?? AppleVirtualizationContractFixtures.ExecutionUnitHandle()),
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
                Lease = new SensitiveLeasePolicy
                {
                    Lifetime = BindingLifetime.ExecutionUnit,
                    ExpiresAfter = expiresAfter,
                    RevokeOnTargetStop = true,
                    SurviveTargetRestart = false,
                },
            },
            AuditLabel = "ssh-agent",
        };

    private static AuthorityBindingSpec CredentialSpec(CredentialRef credential, TargetHandle<ExecutionUnit>? unit = null) =>
        new()
        {
            Kind = AuthorityBindingKind.Credential,
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.Credential,
                Credential = credential,
                Locus = BoundaryLocus.Host,
            },
            Target = new AuthorityBindingTarget(
                AuthorityTargetKind.ExecutionUnit,
                Unit: unit ?? AppleVirtualizationContractFixtures.ExecutionUnitHandle()),
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.EnvironmentReference,
                EnvironmentVariableName = "HPD_CREDENTIAL_PROXY",
                ReadOnly = true,
            },
            Policy = new AuthorityBindingPolicy
            {
                AuthorityClass = SensitiveAuthorityClass.CredentialUseViaHostFunction,
                EffectiveAuthorityClass = SensitiveAuthorityClass.CredentialUseViaHostFunction,
                Redaction = SensitiveRedactionLevel.RedactSecretValues,
                RequireAudit = true,
            },
        };

    private static PublishedEndpointSpec SensitiveEndpointSpec() =>
        new()
        {
            Listener = new EndpointListenerSpec(
                EndpointListenerKind.HostAddress,
                NetworkTransport.Tcp,
                new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x7f000001),
                new PortRange(new NetworkPort(8080), 1),
                Socket: null),
            Target = new EndpointRouteTarget(
                EndpointTargetKind.NetworkMembership,
                MembershipRef(),
                Unit: null,
                Process: null,
                ServiceName: null,
                Transport: NetworkTransport.Tcp,
                Port: new NetworkPort(8080),
                SocketPath: null),
            RoutingNetwork = NetworkRef(),
            SensitivePolicy = new SensitiveEndpointPolicy
            {
                Kind = SensitiveEndpointKind.SshAgent,
                AuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                RequireAudit = true,
            },
            ExposurePolicy = new EndpointExposurePolicy
            {
                Scope = EndpointExposureScope.HostLocal,
                RequireStableListener = true,
            },
        };

    private static ResourceRef<Network> NetworkRef() =>
        new(new ResourceId<Network>("network-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private static ResourceRef<NetworkMembership> MembershipRef() =>
        new(new ResourceId<NetworkMembership>("membership-1"), AppleVirtualizationContractFixtures.RuntimeScope, new ResourceGeneration(1));

    private static void SeedReadyMembership(AppleVirtualizationProviderStateLedger ledger)
    {
        ledger.UpsertNetwork(
            Metadata<Network>("network-1", "network"),
            new NetworkStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                NetworkPhase = NetworkPhase.Ready,
                RealizedCapabilities = NetworkCapabilitySet.IPv4 | NetworkCapabilitySet.NatEgress,
            },
            new NetworkSpec
            {
                Scope = NetworkScope.Runtime,
                ConnectivityIntent = NetworkConnectivityIntent.NatEgress,
                AddressFamilies = AddressFamilyRequirement.IPv4Required,
            });

        ledger.UpsertNetworkMembership(
            Metadata<NetworkMembership>("membership-1", "network-membership"),
            new NetworkMembershipStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                MembershipPhase = NetworkMembershipPhase.Ready,
                Addresses =
                [
                    new NetworkAddressAssignment(
                        new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000002),
                        24,
                        AddressAssignmentKind.ProviderAssigned,
                        IsPrimary: true),
                ],
            },
            new NetworkMembershipSpec
            {
                Network = NetworkRef(),
                Target = new NetworkMembershipTarget(
                    NetworkMembershipTargetKind.ExecutionUnit,
                    Host: null,
                    Unit: AppleVirtualizationContractFixtures.ExecutionUnitHandle(),
                    Process: null),
            });
    }

    private static AppleVirtualizationHelperEnvelope AuthorityRevokeResponse(
        string bindingId,
        RevocationVerificationStatus revocationStatus,
        AuthorityBindingPhase phase,
        IReadOnlyList<AppleVirtualizationAuthorityRevocationEvidence>? revocationEvidence = null,
        string? conditionType = null,
        string? conditionMessage = null) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.AuthorityRevoke,
            "authority-test-response",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.AuthorityBindingResponseSchema).ToResponse(sequenceNumber: 2) with
            {
                PayloadSchema = AppleVirtualizationHelperProtocol.AuthorityBindingResponseSchema,
                AuthorityBindingResponse = new AppleVirtualizationAuthorityBindingResponse
                {
                    BindingId = bindingId,
                    BindingPhase = phase,
                    RevocationStatus = revocationStatus,
                    RevocationEvidence = revocationEvidence ?? Array.Empty<AppleVirtualizationAuthorityRevocationEvidence>(),
                    BoundAuthority = new AppleVirtualizationGuestAgentBoundAuthority
                    {
                        BindingId = bindingId,
                        SourceKind = AuthoritySourceKind.HostService,
                        ProjectionKind = AuthorityProjectionKind.SocketPath,
                        Direction = AuthorityBindingDirection.HostToGuest,
                        EffectiveAuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                        TargetSocketPath = new UnixSocketPath("/run/hpd/ssh-agent.sock"),
                        BoundAt = ClockStart,
                        RevocationStatus = revocationStatus,
                        AuditCorrelationId = "authority-" + bindingId,
                    },
                    AuditEvents =
                    [
                        new AuthorityAuditEvent
                        {
                            Binding = new ResourceRef<AuthorityBinding>(
                                new ResourceId<AuthorityBinding>(bindingId),
                                AppleVirtualizationContractFixtures.RuntimeScope,
                                new ResourceGeneration(1)),
                            Kind = AuthorityAuditKind.Revoked,
                            SourceKind = AuthoritySourceKind.HostService,
                            TargetKind = AuthorityTargetKind.ExecutionUnit,
                            Timestamp = ClockStart,
                            CorrelationId = "authority-" + bindingId,
                        },
                    ],
                    Conditions = conditionType is null
                        ? Array.Empty<Condition>()
                        :
                        [
                            new Condition(
                                conditionType,
                                ConditionStatus.True,
                                "Observed",
                                conditionMessage ?? conditionType,
                                ClockStart,
                                new ResourceGeneration(1),
                                DiagnosticSeverity.Warning),
                        ],
                },
            };

    private static AppleVirtualizationHelperEnvelope AuthorityStatusResponse(
        string bindingId,
        AuthorityBindingPhase phase,
        string conditionType,
        string conditionMessage,
        IReadOnlyList<AppleVirtualizationAuthorityRevocationEvidence>? revocationEvidence = null) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.AuthorityStatus,
            "authority-test-status-response",
            sequenceNumber: 1,
            AppleVirtualizationHelperProtocol.AuthorityBindingResponseSchema).ToResponse(sequenceNumber: 2) with
            {
                PayloadSchema = AppleVirtualizationHelperProtocol.AuthorityBindingResponseSchema,
                AuthorityBindingResponse = new AppleVirtualizationAuthorityBindingResponse
                {
                    BindingId = bindingId,
                    BindingPhase = phase,
                    RevocationStatus = RevocationVerificationStatus.Pending,
                    RevocationEvidence = revocationEvidence ?? Array.Empty<AppleVirtualizationAuthorityRevocationEvidence>(),
                    BoundAuthority = new AppleVirtualizationGuestAgentBoundAuthority
                    {
                        BindingId = bindingId,
                        SourceKind = AuthoritySourceKind.HostService,
                        ProjectionKind = AuthorityProjectionKind.SocketPath,
                        Direction = AuthorityBindingDirection.HostToGuest,
                        EffectiveAuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                        TargetSocketPath = new UnixSocketPath("/run/hpd/ssh-agent.sock"),
                        BoundAt = ClockStart,
                        RevocationStatus = RevocationVerificationStatus.Pending,
                        AuditCorrelationId = "authority-" + bindingId,
                    },
                    AuditEvents =
                    [
                        new AuthorityAuditEvent
                        {
                            Binding = new ResourceRef<AuthorityBinding>(
                                new ResourceId<AuthorityBinding>(bindingId),
                                AppleVirtualizationContractFixtures.RuntimeScope,
                                new ResourceGeneration(1)),
                            Kind = AuthorityAuditKind.Degraded,
                            SourceKind = AuthoritySourceKind.HostService,
                            TargetKind = AuthorityTargetKind.ExecutionUnit,
                            Timestamp = ClockStart,
                            CorrelationId = "authority-" + bindingId,
                        },
                    ],
                    Conditions =
                    [
                        new Condition(
                            conditionType,
                            ConditionStatus.True,
                            "Observed",
                            conditionMessage,
                            ClockStart,
                            new ResourceGeneration(1),
                            DiagnosticSeverity.Warning),
                    ],
                },
            };

    private static AppleVirtualizationAuthorityEvidenceExtension EvidenceExtension(AuthorityBindingStatus status)
    {
        AppleVirtualizationAuthorityEvidenceReader.TryRead(status, out AppleVirtualizationAuthorityEvidenceExtension? evidence)
            .Should().BeTrue();
        return evidence;
    }

    private static AppleVirtualizationAuthorityRevocationEvidence RevocationEvidence(
        AppleVirtualizationAuthorityRevocationEvidenceKind kind,
        bool observed,
        int? fileDescriptor = null) =>
        new()
        {
            Kind = kind,
            Observed = observed,
            FileDescriptor = fileDescriptor,
            GuestSocketPath = new UnixSocketPath("/run/hpd/ssh-agent.sock"),
            ObservedAt = ClockStart,
        };

    private static AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> SeedReadyUnit(
        AppleVirtualizationProviderStateLedger ledger) =>
        ledger.UpsertExecutionUnit(
            Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            new ExecutionUnitStatus
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = new ResourceGeneration(1),
                UnitPhase = ExecutionUnitPhase.Ready,
            });
}
