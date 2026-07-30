using HPD.Environment.AppleVirtualization.Authority;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using HPD.Environment.ProviderConformance;

namespace HPD.Environment.AppleVirtualization.Tests;

public sealed class AppleAuthorityBindingProviderConformanceTests
    : AuthorityBindingProviderConformanceTests
{
    protected override ValueTask<
        AuthorityBindingProviderConformanceFixture>
        CreateAuthorityFixtureAsync()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger =
            new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<ExecutionUnit> unitMetadata =
            AppleVirtualizationContractFixtures
                .Metadata<ExecutionUnit>(
                    "authority-conformance-unit",
                    "execution-unit");
        AppleVirtualizationLedgerEntry<
            ExecutionUnit,
            ExecutionUnitStatus> unit =
            ledger.UpsertExecutionUnit(
                unitMetadata,
                new ExecutionUnitStatus
                {
                    Phase = ResourcePhase.Ready,
                    ObservedGeneration =
                        unitMetadata.Generation,
                    UnitPhase = ExecutionUnitPhase.Ready,
                });
        DateTimeOffset current = new(
            2026,
            7,
            29,
            0,
            0,
            0,
            TimeSpan.Zero);
        var provider =
            new AppleVirtualizationAuthorityBindingProvider(
                ledger,
                helper,
                () => current);
        ResourceMetadata<AuthorityBinding> metadata =
            AppleVirtualizationContractFixtures
                .Metadata<AuthorityBinding>(
                    "authority-conformance",
                    "authority-binding");
        var spec = new AuthorityBindingSpec
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
                Unit: unit.TargetHandle),
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.SocketPath,
                TargetSocketPath = new UnixSocketPath(
                    "/run/hpd/ssh-agent.sock"),
                ReadOnly = true,
            },
            Policy = new AuthorityBindingPolicy
            {
                AuthorityClass =
                    SensitiveAuthorityClass.CredentialDelegation,
                EffectiveAuthorityClass =
                    SensitiveAuthorityClass.CredentialDelegation,
                Redaction =
                    SensitiveRedactionLevel.RedactSecretValues,
                RequireAudit = true,
                Lease = new SensitiveLeasePolicy
                {
                    Lifetime =
                        BindingLifetime.ExecutionUnit,
                    RevokeOnTargetStop = true,
                },
            },
            AuditLabel = "conformance",
        };
        return ValueTask.FromResult(
            new AuthorityBindingProviderConformanceFixture(
                provider,
                metadata,
                spec,
                spec with
                {
                    AuditLabel =
                        "conflicting-authority-conformance",
                },
                spec with
                {
                    Policy = spec.Policy with
                    {
                        Lease = spec.Policy.Lease with
                        {
                            ExpiresAfter =
                                TimeSpan.FromSeconds(1),
                        },
                    },
                },
                advancePastExpiry: () =>
                    current = current.AddMinutes(1),
                observedMutationCount: () =>
                    helper.Requests.Count));
    }
}
