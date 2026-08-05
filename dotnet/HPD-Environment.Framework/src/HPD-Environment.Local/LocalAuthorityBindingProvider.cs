namespace HPD.Environment.Local;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalAuthorityBindingProvider(LocalProviderState state)
    : IAuthorityBindingProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("authority-binding"),
        TargetRouteSegmentKind.ProviderOpaque,
        TargetHandleLifetime.LiveCapability,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control |
        TargetHandleAuthority.Invoke,
        new SchemaId("hpd.execution.local.authority.handle.v1"));

    public ProviderId ProviderId =>
        LocalEnvironmentProviderDescriptor.ProviderId;

    public ValueTask<AuthorityBindingStatus> EnsureAuthorityBindingAsync(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingSpec spec,
        AuthorityBindingStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = new ResourceRef<AuthorityBinding>(
            metadata.Id,
            metadata.Scope,
            metadata.Generation);
        ProviderLedgerLookup<
            ProviderResourceEntry<
                AuthorityBinding,
                AuthorityBindingSpec,
                AuthorityBindingStatus>> existing =
            state.Ledger.TryGet<
                AuthorityBinding,
                AuthorityBindingSpec,
                AuthorityBindingStatus>(resource);
        if (existing.Succeeded)
        {
            if (existing.Entry!.Spec != spec)
                return ValueTask.FromResult(Failed(
                    metadata,
                    "LocalEnvironment.AuthoritySpecConflict",
                    "An authority binding with the same identity already exists with a different immutable specification."));
            return ValueTask.FromResult(
                existing.Entry.Status);
        }
        if (existing.Diagnostic?.Code.Value !=
            "hpd.environment.provider-ledger.resource-unknown")
        {
            return ValueTask.FromResult(Failed(
                metadata,
                existing.Diagnostic!.Code.Value,
                existing.Diagnostic.Message));
        }
        AuthoritySourceClassification classification =
            AuthoritySourceClassifier.Classify(spec);
        if (!classification.IsClassified)
        {
            return ValueTask.FromResult(Failed(
                metadata,
                classification.DiagnosticCode,
                classification.DiagnosticMessage));
        }
        if (classification.EndpointKind !=
            SensitiveEndpointKind.EngineSocket)
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "LocalEnvironment.AuthoritySourceUnsupported",
                "The first Local provider slice accepts only mediated container-engine authority."));
        }
        if (spec.Target.Kind != AuthorityTargetKind.ExecutionUnit ||
            spec.Target.Unit is not { } unitHandle)
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "LocalEnvironment.AuthorityTargetInvalid",
                "Local engine authority requires an exact execution-unit target."));
        }
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> unit = state.Ledger.TryGet<
                    ExecutionUnit,
                    ExecutionUnitSpec,
                    ExecutionUnitStatus>(unitHandle);
        if (!unit.Succeeded ||
            unit.Entry!.Status.UnitPhase is not (
                ExecutionUnitPhase.Ready or
                ExecutionUnitPhase.Running))
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "LocalEnvironment.AuthorityTargetStale",
                unit.Diagnostic?.Message ??
                "The target execution unit is not ready."));
        }
        SensitiveAuthorityClass expectedAuthorityClass =
            state.CurrentEngineAuthorityMode switch
            {
                EngineAuthorityMode.Rootless =>
                    SensitiveAuthorityClass.RootlessEngineControl,
                EngineAuthorityMode.Rootful =>
                    SensitiveAuthorityClass.RootfulEngineControl,
                _ => SensitiveAuthorityClass.ProviderDefined,
            };
        if (expectedAuthorityClass ==
            SensitiveAuthorityClass.ProviderDefined)
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "LocalEnvironment.EngineAuthorityModeUnknown",
                "The current Local engine authority mode is unavailable."));
        }
        if (spec.Policy.EffectiveAuthorityClass !=
            expectedAuthorityClass)
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "LocalEnvironment.AuthorityClassMismatch",
                $"The requested effective authority '{spec.Policy.EffectiveAuthorityClass}' does not match the currently observed Local engine authority '{expectedAuthorityClass}'."));
        }
        if (!spec.Policy.RequireAudit ||
            !spec.Policy.AllowProviderSideProxy)
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "LocalEnvironment.AuthorityPolicyInsufficient",
                "Local engine authority requires provider mediation and audit."));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt =
            spec.Policy.Lease.ExpiresAfter is { } duration
                ? now + duration
                : null;
        var status = new AuthorityBindingStatus
        {
            Phase = ResourcePhase.Ready,
            BindingPhase = AuthorityBindingPhase.Projected,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = now,
            BoundAuthority = new BoundAuthority
            {
                SourceKind = spec.Source.Kind,
                ProjectionKind = spec.Projection.Kind,
                Direction = spec.Policy.Direction,
                EffectiveAuthorityClass =
                    spec.Policy.EffectiveAuthorityClass,
                TargetSocketPath =
                    spec.Projection.TargetSocketPath,
                BoundAt = now,
                ExpiresAt = expiresAt,
                RotationGeneration =
                    state.Ledger.ProviderGeneration,
                RevocationStatus =
                    RevocationVerificationStatus.Pending,
                AuditCorrelationId =
                    $"local-authority-{metadata.Id.Value}",
            },
            Conditions =
            [
                new Condition(
                    "LocalEnvironment.AuthorityMediated",
                    ConditionStatus.True,
                    "ProviderLeaseCreated",
                    "Engine authority remains inside the Local provider and is available only to the bounded HPDOS operation.",
                    now,
                    metadata.Generation),
            ],
        };
        ProviderResourceEntry<
            AuthorityBinding,
            AuthorityBindingSpec,
            AuthorityBindingStatus> entry =
            state.Ledger.Upsert(metadata, spec, status, Shape);
        status = status with { ProviderHandle = entry.TargetHandle };
        state.Ledger.Upsert(metadata, spec, status, Shape);
        state.BindAuthorityToCurrentEngine(metadata.Id.Value);
        state.AppendAuthorityAudit(
            metadata.Id.Value,
            Audit(
                new ResourceRef<AuthorityBinding>(
                    metadata.Id,
                    metadata.Scope,
                    metadata.Generation),
                spec,
                AuthorityAuditKind.Projected,
                now));
        return ValueTask.FromResult(status);
    }

    public ValueTask<AuthorityBindingStatus> GetStatusAsync(
        ResourceRef<AuthorityBinding> binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                AuthorityBinding,
                AuthorityBindingSpec,
                AuthorityBindingStatus>> lookup = state.Ledger.TryGet<
                    AuthorityBinding,
                    AuthorityBindingSpec,
                    AuthorityBindingStatus>(binding);
        if (!lookup.Succeeded)
            return ValueTask.FromException<AuthorityBindingStatus>(
                new InvalidOperationException(
                    $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}"));
        ProviderResourceEntry<
            AuthorityBinding,
            AuthorityBindingSpec,
            AuthorityBindingStatus> entry = lookup.Entry!;
        if (entry.Status.BindingPhase ==
                AuthorityBindingPhase.Projected &&
            entry.Status.BoundAuthority?.ExpiresAt is { } expires &&
            expires <= DateTimeOffset.UtcNow)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            AuthorityBindingStatus expired = entry.Status with
            {
                Phase = ResourcePhase.Degraded,
                BindingPhase = AuthorityBindingPhase.Degraded,
                LastTransitionAt = now,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = new DiagnosticCode(
                            "LocalEnvironment.AuthorityLeaseExpired"),
                        Message =
                            "The Local engine-authority lease expired and is no longer usable.",
                        ProviderId = ProviderId,
                    },
                ],
            };
            state.Ledger.Upsert(
                Metadata(entry.Resource),
                entry.Spec,
                expired,
                Shape);
            state.ReleaseAuthority(binding.Id.Value);
            return ValueTask.FromResult(expired);
        }
        return ValueTask.FromResult(entry.Status);
    }

    public ValueTask RevokeAuthorityBindingAsync(
        ResourceRef<AuthorityBinding> binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                AuthorityBinding,
                AuthorityBindingSpec,
                AuthorityBindingStatus>> lookup = state.Ledger.TryGet<
                    AuthorityBinding,
                    AuthorityBindingSpec,
                    AuthorityBindingStatus>(binding);
        if (!lookup.Succeeded)
        {
            if (lookup.Diagnostic?.Code.Value ==
                "hpd.environment.provider-ledger.resource-unknown")
                return ValueTask.CompletedTask;
            throw new InvalidOperationException(
                $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}");
        }

        ProviderResourceEntry<
            AuthorityBinding,
            AuthorityBindingSpec,
            AuthorityBindingStatus> entry = lookup.Entry!;
        AuthorityBindingStatus revoked = entry.Status with
        {
            Phase = ResourcePhase.Ready,
            BindingPhase = AuthorityBindingPhase.Revoked,
            LastTransitionAt = DateTimeOffset.UtcNow,
            BoundAuthority = entry.Status.BoundAuthority is { } bound
                ? bound with
                {
                    RevocationStatus =
                        RevocationVerificationStatus.Verified,
                }
                : null,
        };
        state.Ledger.Upsert(
            Metadata(entry.Resource),
            entry.Spec,
            revoked,
            Shape);
        state.AppendAuthorityAudit(
            binding.Id.Value,
            Audit(
                entry.Resource,
                entry.Spec,
                AuthorityAuditKind.Revoked,
                DateTimeOffset.UtcNow)
            .Concat(Audit(
                entry.Resource,
                entry.Spec,
                AuthorityAuditKind.RevocationVerified,
                DateTimeOffset.UtcNow))
            .ToArray());
        state.ReleaseAuthority(binding.Id.Value);
        return ValueTask.CompletedTask;
    }

    private static IReadOnlyList<AuthorityAuditEvent> Audit(
        ResourceRef<AuthorityBinding> binding,
        AuthorityBindingSpec spec,
        AuthorityAuditKind kind,
        DateTimeOffset timestamp) =>
    [
        new AuthorityAuditEvent
        {
            Binding = binding,
            Kind = kind,
            SourceKind = spec.Source.Kind,
            TargetKind = spec.Target.Kind,
            Timestamp = timestamp,
            Actor = SensitiveValueRedactor.Redact(
                spec.Policy.Provenance?.Actor),
            CorrelationId =
                $"local-authority-{binding.Id.Value}",
        },
    ];

    private AuthorityBindingStatus Failed(
        ResourceMetadata<AuthorityBinding> metadata,
        string code,
        string message) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            BindingPhase = AuthorityBindingPhase.Failed,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Rejected,
            ObservedGeneration = metadata.Generation,
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = new DiagnosticCode(code),
                    Message = message,
                    ProviderId = ProviderId,
                },
            ],
        };

    private static ResourceMetadata<AuthorityBinding> Metadata(
        ResourceRef<AuthorityBinding> resource) =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind("AuthorityBinding"),
            Scope = resource.Scope,
            Generation =
                resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
}
