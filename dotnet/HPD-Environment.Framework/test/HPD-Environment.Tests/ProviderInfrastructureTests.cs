using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

namespace HPD.Environment.Tests;

public sealed class ProviderInfrastructureTests
{
    [Fact]
    public void IncarnationValidationNamesTheChangedDimension()
    {
        var expected = new ProviderIncarnation(
            new ProviderId("test"),
            2,
            new ResourceGeneration(3),
            new RuntimeHostStartGeneration(4),
            new EngineIncarnationGeneration(5),
            new Dictionary<string, string>
            {
                ["guest-agent-generation"] = "6",
            });
        Diagnostic? stale =
            ProviderIncarnationValidator.ValidateExact(
                expected,
                expected with
                {
                    ProviderDimensions =
                        new Dictionary<string, string>
                        {
                            ["guest-agent-generation"] = "7",
                        },
                });

        Assert.Equal(
            "hpd.environment.incarnation.stale",
            stale!.Code.Value);
        Assert.Contains(
            "guest-agent-generation",
            stale.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupAttemptsEveryOperationAfterFailure()
    {
        int attempts = 0;
        IReadOnlyList<Exception> failures =
            await ProviderCleanup.RunAllAsync(
            [
                _ =>
                {
                    attempts++;
                    return ValueTask.FromException(
                        new InvalidOperationException("first"));
                },
                _ =>
                {
                    attempts++;
                    return ValueTask.CompletedTask;
                },
            ],
            TimeSpan.FromSeconds(1));

        Assert.Equal(2, attempts);
        Assert.Single(failures);
    }

    [Fact]
    public void AuditLedgerRetainsOnlyTheNewestBoundedEvents()
    {
        var ledger = new BoundedAuditLedger<string, int>(3);

        ledger.Append("binding", [1, 2]);
        ledger.Append("binding", [3, 4]);

        Assert.Equal([2, 3, 4], ledger.Get("binding"));
        Assert.True(ledger.Remove("binding"));
        Assert.Empty(ledger.Get("binding"));
    }

    [Fact]
    public void GenerationFenceAdvancesAndRejectsPriorGeneration()
    {
        var fence = new ProviderGenerationFence();

        ulong previous = fence.Current;
        ulong current = fence.Advance();

        Assert.False(fence.IsCurrent(previous));
        Assert.True(fence.IsCurrent(current));
    }

    [Theory]
    [InlineData(
        AuthorityBindingPhase.Revoked,
        RevocationVerificationStatus.Verified,
        true)]
    [InlineData(
        AuthorityBindingPhase.Revoked,
        RevocationVerificationStatus.Pending,
        false)]
    [InlineData(
        AuthorityBindingPhase.Revoking,
        RevocationVerificationStatus.Verified,
        false)]
    public void RevocationRequiresBothTerminalPhaseAndVerifiedEvidence(
        AuthorityBindingPhase phase,
        RevocationVerificationStatus evidence,
        bool expected)
    {
        AuthorityRevocationEvaluation evaluation =
            AuthorityRevocationVerifier.Evaluate(
                phase,
                evidence);

        Assert.Equal(expected, evaluation.Verified);
        Assert.Equal(
            expected
                ? AuthorityBindingPhase.Revoked
                : AuthorityBindingPhase.Revoking,
            evaluation.BindingPhase);
    }

    private static readonly ProviderId Provider = new("test.local");
    private static readonly ResourceScope Scope = new("runtime-a");
    private static readonly ProviderResourceShape HostShape = new(
        new TargetKind("runtime-host"),
        TargetRouteSegmentKind.RuntimeHost,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
        new SchemaId("test.local.runtime-host.handle.v1"));

    [Fact]
    public void ProviderLedger_RetainsHandleForSameResourceGeneration()
    {
        var ledger = new ProviderResourceLedger(Provider);
        ResourceMetadata<RuntimeHost> metadata = HostMetadata(1);

        ProviderResourceEntry<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>
            first = ledger.Upsert(
                metadata,
                HostSpec(),
                HostStatus(metadata.Generation),
                HostShape);
        ProviderResourceEntry<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>
            second = ledger.Upsert(
                metadata with { UpdatedAt = DateTimeOffset.UtcNow },
                HostSpec(),
                HostStatus(metadata.Generation),
                HostShape);

        Assert.Equal(first.ProviderHandle, second.ProviderHandle);
        Assert.Equal(first.TargetHandle, second.TargetHandle);
        Assert.True(ledger.TryGet<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus>(second.TargetHandle).Succeeded);
    }

    [Fact]
    public void ProviderLedger_ResourceGenerationInvalidatesPriorHandle()
    {
        var ledger = new ProviderResourceLedger(Provider);
        ProviderResourceEntry<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>
            first = ledger.Upsert(
                HostMetadata(1),
                HostSpec(),
                HostStatus(new ResourceGeneration(1)),
                HostShape);
        ProviderResourceEntry<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>
            second = ledger.Upsert(
                HostMetadata(2),
                HostSpec(),
                HostStatus(new ResourceGeneration(2)),
                HostShape);

        Assert.NotEqual(first.ProviderHandle, second.ProviderHandle);
        ProviderLedgerLookup<
            ProviderResourceEntry<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>> stale = ledger.TryGet<
                    RuntimeHost,
                    RuntimeHostSpec,
                    RuntimeHostStatus>(first.TargetHandle);
        Assert.False(stale.Succeeded);
        Assert.Equal(
            "hpd.environment.provider-ledger.handle-unknown",
            stale.Diagnostic?.Code.Value);
    }

    [Fact]
    public void ProviderLedger_ProviderGenerationFencesEveryExistingHandle()
    {
        var ledger = new ProviderResourceLedger(Provider);
        ProviderResourceEntry<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>
            entry = ledger.Upsert(
                HostMetadata(1),
                HostSpec(),
                HostStatus(new ResourceGeneration(1)),
                HostShape);

        ledger.AdvanceProviderGeneration();

        ProviderLedgerLookup<
            ProviderResourceEntry<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>> stale = ledger.TryGet<
                    RuntimeHost,
                    RuntimeHostSpec,
                    RuntimeHostStatus>(entry.TargetHandle);
        Assert.False(stale.Succeeded);
        Assert.Equal(
            "hpd.environment.provider-ledger.handle-generation-stale",
            stale.Diagnostic?.Code.Value);
    }

    [Fact]
    public void AuthorityClassifier_RecognizesRootlessEngineWithoutProviderPolicy()
    {
        AuthoritySourceClassification classification =
            AuthoritySourceClassifier.Classify(new AuthorityBindingSpec
            {
                Kind = AuthorityBindingKind.HostService,
                Source = new AuthorityBindingSource
                {
                    Kind = AuthoritySourceKind.UnixSocket,
                    Locus = BoundaryLocus.Host,
                    SocketPath = new UnixSocketPath(
                        "/run/user/501/podman/podman.sock"),
                },
                Target = new AuthorityBindingTarget(
                    AuthorityTargetKind.ExecutionUnit),
                Projection = new AuthorityBindingProjection
                {
                    Kind = AuthorityProjectionKind.SocketPath,
                    TargetSocketPath = new UnixSocketPath(
                        "/run/hpd/engine/podman.sock"),
                },
            });

        Assert.True(classification.IsClassified);
        Assert.Equal(
            SensitiveEndpointKind.EngineSocket,
            classification.EndpointKind);
        Assert.Equal(
            SensitiveAuthorityClass.RootlessEngineControl,
            classification.AuthorityClass);
        Assert.Equal(
            SensitiveRedactionLevel.RedactIdentifiers,
            classification.Redaction);
    }

    [Fact]
    public void AuthorityClassifier_RejectsUnboundedProviderDefinedAuthority()
    {
        AuthoritySourceClassification classification =
            AuthoritySourceClassifier.Classify(new AuthorityBindingSpec
            {
                Kind = AuthorityBindingKind.ProviderDefined,
                Source = new AuthorityBindingSource
                {
                    Kind = AuthoritySourceKind.ProviderDefined,
                    Locus = BoundaryLocus.Host,
                },
                Target = new AuthorityBindingTarget(
                    AuthorityTargetKind.ProviderDefined),
                Projection = new AuthorityBindingProjection
                {
                    Kind = AuthorityProjectionKind.ProviderDefined,
                },
            });

        Assert.False(classification.IsClassified);
        Assert.Equal(
            "hpd.environment.authority.provider-metadata-invalid",
            classification.DiagnosticCode);
    }

    private static ResourceMetadata<RuntimeHost> HostMetadata(long generation) =>
        new()
        {
            Id = new ResourceId<RuntimeHost>("local-host"),
            Kind = new ResourceKind("RuntimeHost"),
            Scope = Scope,
            Generation = new ResourceGeneration(generation),
            SchemaVersion = new SchemaVersion("1"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static RuntimeHostSpec HostSpec() =>
        new()
        {
            PreferredProvider = Provider,
            Platform = new PlatformSpec("macos", "arm64"),
        };

    private static RuntimeHostStatus HostStatus(
        ResourceGeneration generation) =>
        new()
        {
            Phase = ResourcePhase.Ready,
            HostPhase = RuntimeHostPhase.Ready,
            ObservedGeneration = generation,
        };
}
