namespace HPD.Environment.AppleVirtualization.Authority;

using System.Globalization;
using System.Text.Json;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

public sealed class AppleVirtualizationAuthorityBindingProvider : IAuthorityBindingProvider, IRuntimeFinalizationParticipant
{
    internal static readonly SchemaId AuthorityEvidenceExtensionSchema = new("hpd.execution.apple-virtualization.authority.evidence.v1");
    private static readonly ContentType AuthorityEvidenceExtensionContentType = new("application/json");
    private const int MaxPersistedEvidenceItems = 8;
    private const int MaxPersistedConditions = 8;
    private const int BindRetryAttempts = 8;
    private static readonly TimeSpan DefaultBindRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly IAppleVirtualizationHelperClient _helper;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _bindRetryDelay;
    private long _requestSequence;

    internal AppleVirtualizationAuthorityBindingProvider(
        AppleVirtualizationProviderStateLedger ledger,
        IAppleVirtualizationHelperClient helper,
        Func<DateTimeOffset>? now = null,
        TimeSpan? bindRetryDelay = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _helper = helper ?? throw new ArgumentNullException(nameof(helper));
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _bindRetryDelay = bindRetryDelay ?? DefaultBindRetryDelay;
    }

    public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;

    public async ValueTask<AuthorityBindingStatus> EnsureAuthorityBindingAsync(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingSpec spec,
        AuthorityBindingStatus? observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset now = _now();
        AppleVirtualizationAuthoritySourceClassification classification =
            AppleVirtualizationAuthoritySourceClassifier.Classify(spec);
        if (!classification.IsSupported)
        {
            AuthorityBindingStatus failed = FailedStatus(
                metadata,
                spec,
                classification,
                AuthorityDiagnostic(DiagnosticSeverity.Error, classification.DiagnosticCode, classification.DiagnosticMessage, "authority.source"));
            return Store(metadata, spec, failed, Audit(metadata, spec, AuthorityAuditKind.Failed, now, ReasonCode: classification.DiagnosticCode));
        }

        if (ValidateProjectionPolicy(metadata, spec, classification) is { } projectionDiagnostic)
        {
            AuthorityBindingStatus failed = FailedStatus(metadata, spec, classification, projectionDiagnostic);
            return Store(metadata, spec, failed, Audit(metadata, spec, AuthorityAuditKind.Failed, now, ReasonCode: projectionDiagnostic.Code.Value));
        }

        if (spec.Policy.RequireExplicitUserApproval)
        {
            AuthorityBindingStatus failed = FailedStatus(
                metadata,
                spec,
                classification,
                AuthorityDiagnostic(
                    DiagnosticSeverity.Error,
                    "AppleVirtualization.AuthorityExplicitApprovalRequired",
                    "The Apple Virtualization provider does not accept real sensitive authority requiring explicit user approval in the default provider path.",
                    "authority.policy.approval"));
            return Store(metadata, spec, failed, Audit(metadata, spec, AuthorityAuditKind.Failed, now, ReasonCode: "AppleVirtualization.AuthorityExplicitApprovalRequired"));
        }

        LeaseComputation lease = ComputeLease(spec.Policy.Lease, now);
        if (lease.Diagnostic is { } leaseDiagnostic)
        {
            AuthorityBindingStatus failed = FailedStatus(metadata, spec, classification, leaseDiagnostic);
            return Store(metadata, spec, failed, Audit(metadata, spec, AuthorityAuditKind.Failed, now, ReasonCode: leaseDiagnostic.Code.Value));
        }

        AppleVirtualizationHelperEnvelope bindRequest = Request(AppleVirtualizationHelperOperation.AuthorityBind) with
        {
            ResourceKind = metadata.Kind,
            ResourceId = metadata.Id.Value,
            ResourceScope = metadata.Scope,
            ResourceGeneration = metadata.Generation,
            ProviderGeneration = _ledger.ProviderGeneration,
            AuthorityBindingRequest = RequestFromSpec(
                metadata,
                spec,
                AppleVirtualizationAuthorityBindingAction.Bind,
                classification,
                lease),
        };
        AppleVirtualizationHelperEnvelope response = await SendBindWithRetryAsync(bindRequest, cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            Diagnostic diagnostic = ToDiagnostic(response.Error, "authority.bind");
            AuthorityBindingStatus failed = FailedStatus(metadata, spec, classification, diagnostic);
            return Store(metadata, spec, failed, Audit(metadata, spec, AuthorityAuditKind.Failed, now, ReasonCode: diagnostic.Code.Value));
        }

        if (response.AuthorityBindingResponse is not { } authority)
        {
            Diagnostic diagnostic = AuthorityDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.AuthorityMissingHelperPayload",
                "The Apple Virtualization helper did not return an authority binding payload.",
                "authority.bind");
            AuthorityBindingStatus failed = FailedStatus(metadata, spec, classification, diagnostic);
            return Store(metadata, spec, failed, Audit(metadata, spec, AuthorityAuditKind.Failed, now, ReasonCode: diagnostic.Code.Value));
        }

        AuthorityBindingStatus status = StatusFromHelper(metadata.Generation, spec, classification, authority, lease);
        AuthorityBindingStatus stored = Store(metadata, spec, status, authority.AuditEvents);
        if (stored.BindingPhase == AuthorityBindingPhase.Projected &&
            spec.Target.Kind == AuthorityTargetKind.ExecutionUnit &&
            spec.Target.Unit is { } unit)
        {
            ResourceRef<AuthorityBinding> binding = new(metadata.Id, metadata.Scope, metadata.Generation);
            if (!_ledger.AttachAuthorityBindingToExecutionUnit(unit, binding) &&
                !string.IsNullOrWhiteSpace(unit.Route.BackingResourceId))
            {
                _ledger.AttachAuthorityBindingToExecutionUnit(
                    new ResourceRef<ExecutionUnit>(
                        new ResourceId<ExecutionUnit>(unit.Route.BackingResourceId),
                        unit.Route.Scope),
                    binding);
            }
        }

        return stored;
    }

    private async ValueTask<AppleVirtualizationHelperEnvelope> SendBindWithRetryAsync(
        AppleVirtualizationHelperEnvelope request,
        CancellationToken cancellationToken)
    {
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(request, cancellationToken).ConfigureAwait(false);
        for (int attempt = 1;
             attempt < BindRetryAttempts &&
             response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error &&
             response.Error?.Retryable == true;
             attempt++)
        {
            if (_bindRetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(_bindRetryDelay, cancellationToken).ConfigureAwait(false);
            }

            response = await _helper.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    public async ValueTask<AuthorityBindingStatus> GetStatusAsync(
        ResourceRef<AuthorityBinding> binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>> lookup =
            _ledger.TryGetAuthorityBinding(binding);
        if (!lookup.Succeeded)
        {
            return new AuthorityBindingStatus
            {
                Phase = ResourcePhase.Failed,
                BindingPhase = AuthorityBindingPhase.Failed,
                LastTransitionAt = _now(),
                Diagnostics = [lookup.Diagnostic ?? AppleVirtualizationHandleDiagnostics.Missing(ProviderId, "authority-binding/" + binding.Id.Value)],
            };
        }

        AuthorityBindingStatus status = ApplyRuntimeLeaseState(binding, lookup.Entry!.Status);
        if (status.BindingPhase != AuthorityBindingPhase.Projected)
        {
            return status;
        }

        AuthorityBindingSpec? spec = _ledger.TryGetAuthorityBindingSpec(binding);
        if (spec is null)
        {
            return status;
        }

        AppleVirtualizationAuthoritySourceClassification classification =
            AppleVirtualizationAuthoritySourceClassifier.Classify(spec);
        if (!classification.IsSupported)
        {
            return status;
        }

        DateTimeOffset now = _now();
        LeaseComputation lease = ComputeLease(spec.Policy.Lease, status.BoundAuthority?.BoundAt ?? now);
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.AuthorityStatus) with
            {
                ResourceKind = new ResourceKind("authority-binding"),
                ResourceId = binding.Id.Value,
                ResourceScope = binding.Scope,
                ResourceGeneration = binding.Generation,
                ProviderGeneration = _ledger.ProviderGeneration,
                AuthorityBindingRequest = RequestFromSpec(
                    binding.Id.Value,
                    spec,
                    AppleVirtualizationAuthorityBindingAction.Status,
                    classification,
                    lease),
            },
            cancellationToken).ConfigureAwait(false);

        AuthorityBindingStatus refreshed = StatusRefreshFromHelper(
            binding,
            spec,
            status,
            classification,
            lease,
            response,
            now);
        _ledger.UpdateAuthorityBindingStatus(
            binding,
            refreshed,
            response.AuthorityBindingResponse?.AuditEvents);
        if (refreshed.BindingPhase == AuthorityBindingPhase.Revoked &&
            refreshed.BoundAuthority?.RevocationStatus == RevocationVerificationStatus.Verified)
        {
            DetachVerifiedBindingFromTarget(binding, spec);
        }

        return refreshed;
    }

    public async ValueTask RevokeAuthorityBindingAsync(
        ResourceRef<AuthorityBinding> binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>> lookup =
            _ledger.TryGetAuthorityBinding(binding);
        if (!lookup.Succeeded || lookup.Entry is null)
        {
            throw new InvalidOperationException(lookup.Diagnostic?.Message ?? "Authority binding could not be resolved for revocation.");
        }

        AuthorityBindingSpec? spec = _ledger.TryGetAuthorityBindingSpec(binding);
        if (spec is null)
        {
            throw new InvalidOperationException("Authority binding spec could not be resolved for revocation.");
        }

        if (lookup.Entry.Status.BindingPhase == AuthorityBindingPhase.Revoked &&
            lookup.Entry.Status.BoundAuthority?.RevocationStatus == RevocationVerificationStatus.Verified)
        {
            return;
        }

        AppleVirtualizationAuthoritySourceClassification classification =
            AppleVirtualizationAuthoritySourceClassifier.Classify(spec);
        DateTimeOffset now = _now();
        LeaseComputation lease = ComputeLease(spec.Policy.Lease, lookup.Entry.Status.BoundAuthority?.BoundAt ?? now);
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.AuthorityRevoke) with
            {
                ResourceKind = new ResourceKind("authority-binding"),
                ResourceId = binding.Id.Value,
                ResourceScope = binding.Scope,
                ResourceGeneration = binding.Generation,
                ProviderGeneration = _ledger.ProviderGeneration,
                AuthorityBindingRequest = RequestFromSpec(
                    binding.Id.Value,
                    spec,
                    AppleVirtualizationAuthorityBindingAction.Revoke,
                    classification,
                    lease),
            },
            cancellationToken).ConfigureAwait(false);

        AuthorityBindingStatus status = RevocationStatusFromHelper(
            binding,
            spec,
            lookup.Entry.Status,
            response,
            now);

        _ledger.UpdateAuthorityBindingStatus(
            binding,
            status,
            response.AuthorityBindingResponse?.AuditEvents ?? Audit(binding, spec, AuthorityAuditKind.Revoked, now));
        if (status.BindingPhase == AuthorityBindingPhase.Revoked &&
            status.BoundAuthority?.RevocationStatus == RevocationVerificationStatus.Verified)
        {
            DetachVerifiedBindingFromTarget(binding, spec);
        }
    }

    public async ValueTask<RuntimeFinalizationResult> FinalizeRuntimeAsync(
        RuntimeFinalizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.CleanupPolicy.RevokeAuthorityBindingsFirst)
        {
            return new RuntimeFinalizationResult
            {
                RuntimeScope = request.RuntimeScope,
            };
        }

        ResourceRef<AuthorityBinding>[] bindings = _ledger.GetAuthorityBindings(request.RuntimeScope);
        Diagnostic[]? diagnostics = null;
        var retained = bindings.Length == 0 ? null : new UntypedResourceRef[bindings.Length];
        int retainedCount = 0;
        for (int i = 0; i < bindings.Length; i++)
        {
            ResourceRef<AuthorityBinding> binding = bindings[i];
            try
            {
                await RevokeAuthorityBindingAsync(binding, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics = AddDiagnostic(diagnostics, AuthorityDiagnostic(
                    DiagnosticSeverity.Error,
                    "AppleVirtualization.RuntimeAuthorityRevocationFailed",
                    "Runtime finalization failed to revoke authority binding '" + binding.Id.Value + "': " + ex.Message,
                    "authority.runtimeFinalization"));
            }

            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>> lookup =
                _ledger.TryGetAuthorityBinding(binding);
            if (lookup.Succeeded &&
                lookup.Entry!.Status.BindingPhase != AuthorityBindingPhase.Revoked)
            {
                retained![retainedCount++] = new UntypedResourceRef(
                    new ResourceKind("authority-binding"),
                    binding.Id.Value,
                    binding.Scope,
                    binding.Generation);
            }
        }

        return new RuntimeFinalizationResult
        {
            RuntimeScope = request.RuntimeScope,
            RetainedResources = retainedCount == 0
                ? Array.Empty<UntypedResourceRef>()
                : Trim(retained!, retainedCount),
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>(),
        };
    }

    private static AuthorityBindingStatus RevocationStatusFromHelper(
        ResourceRef<AuthorityBinding> binding,
        AuthorityBindingSpec spec,
        AuthorityBindingStatus current,
        AppleVirtualizationHelperEnvelope response,
        DateTimeOffset now)
    {
        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            Diagnostic helperError = ToDiagnostic(response.Error, "authority.revoke");
            return current with
            {
                Phase = ResourcePhase.Deleting,
                BindingPhase = AuthorityBindingPhase.Revoking,
                BoundAuthority = current.BoundAuthority is { } authority
                    ? authority with { RevocationStatus = RevocationVerificationStatus.Pending }
                    : null,
                Diagnostics = AppendIfMissing(helperError, current.Diagnostics),
                LastTransitionAt = now,
            };
        }

        if (response.AuthorityBindingResponse is not { } helper)
        {
            Diagnostic missingPayload = AuthorityDiagnostic(
                DiagnosticSeverity.Warning,
                "AppleVirtualization.AuthorityRevocationMissingHelperPayload",
                "The Apple Virtualization helper accepted authority revocation but did not return a verification payload.",
                "authority.revoke");
            return current with
            {
                Phase = ResourcePhase.Deleting,
                BindingPhase = AuthorityBindingPhase.Revoking,
                BoundAuthority = current.BoundAuthority is { } authority
                    ? authority with { RevocationStatus = RevocationVerificationStatus.Pending }
                    : null,
                Diagnostics = AppendIfMissing(missingPayload, current.Diagnostics),
                LastTransitionAt = now,
            };
        }

        RevocationVerificationStatus revocation = MapRevocationVerification(helper);
        AuthorityBindingPhase phase = helper.BindingPhase == AuthorityBindingPhase.Revoked &&
            revocation == RevocationVerificationStatus.Verified
                ? AuthorityBindingPhase.Revoked
                : AuthorityBindingPhase.Revoking;
        ResourcePhase resourcePhase = phase == AuthorityBindingPhase.Revoked
            ? ResourcePhase.Deleted
            : ResourcePhase.Deleting;
        Diagnostic? diagnostic = RevocationDiagnostic(revocation);

        BoundAuthority? boundAuthority = current.BoundAuthority is { } currentAuthority
            ? currentAuthority with { RevocationStatus = revocation }
            : BoundAuthorityFromHelper(spec, AppleVirtualizationAuthoritySourceClassifier.Classify(spec), helper.BoundAuthority, ComputeLease(spec.Policy.Lease, now)) is { } helperAuthority
                ? helperAuthority with { RevocationStatus = revocation }
                : null;

        return current with
        {
            Phase = resourcePhase,
            BindingPhase = phase,
            BoundAuthority = boundAuthority,
            Conditions = MergeConditions(current.Conditions, helper.Conditions),
            Diagnostics = AppendDiagnostics(
                diagnostic is null ? current.Diagnostics : AppendIfMissing(diagnostic, current.Diagnostics),
                DiagnosticsFromHelperConditions(phase, helper.Conditions, helper.Diagnostics)),
            Extensions = MergeAuthorityEvidenceExtension(current.Extensions, helper),
            LastTransitionAt = now,
        };
    }

    private static AuthorityBindingStatus StatusRefreshFromHelper(
        ResourceRef<AuthorityBinding> binding,
        AuthorityBindingSpec spec,
        AuthorityBindingStatus current,
        AppleVirtualizationAuthoritySourceClassification classification,
        LeaseComputation lease,
        AppleVirtualizationHelperEnvelope response,
        DateTimeOffset now)
    {
        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            Diagnostic helperError = ToDiagnostic(response.Error, "authority.status");
            return current with
            {
                Phase = ResourcePhase.Degraded,
                BindingPhase = AuthorityBindingPhase.Degraded,
                Diagnostics = AppendIfMissing(helperError, current.Diagnostics),
                LastTransitionAt = now,
            };
        }

        if (response.AuthorityBindingResponse is not { } helper)
        {
            Diagnostic missingPayload = AuthorityDiagnostic(
                DiagnosticSeverity.Warning,
                "AppleVirtualization.AuthorityStatusMissingHelperPayload",
                "The Apple Virtualization helper accepted authority status refresh but did not return a status payload.",
                "authority.status");
            return current with
            {
                Phase = ResourcePhase.Degraded,
                BindingPhase = AuthorityBindingPhase.Degraded,
                Diagnostics = AppendIfMissing(missingPayload, current.Diagnostics),
                LastTransitionAt = now,
            };
        }

        ResourceGeneration observedGeneration = binding.Generation ?? current.ObservedGeneration;
        AuthorityBindingStatus refreshed = StatusFromHelper(
            observedGeneration,
            spec,
            classification,
            helper,
            lease);
        if (helper.BindingPhase == AuthorityBindingPhase.Revoked)
        {
            RevocationVerificationStatus revocation = MapRevocationVerification(helper);
            refreshed = refreshed with
            {
                Phase = revocation == RevocationVerificationStatus.Verified
                    ? ResourcePhase.Deleted
                    : ResourcePhase.Deleting,
                BindingPhase = revocation == RevocationVerificationStatus.Verified
                    ? AuthorityBindingPhase.Revoked
                    : AuthorityBindingPhase.Revoking,
                BoundAuthority = refreshed.BoundAuthority is { } authority
                    ? authority with { RevocationStatus = revocation }
                    : null,
                Diagnostics = RevocationDiagnostic(revocation) is { } diagnostic
                    ? AppendIfMissing(diagnostic, refreshed.Diagnostics)
                    : refreshed.Diagnostics,
                LastTransitionAt = now,
            };
        }

        return refreshed;
    }

    private static RevocationVerificationStatus MapRevocationVerification(AppleVirtualizationAuthorityBindingResponse helper)
    {
        RevocationVerificationStatus evidenceStatus = MapRevocationEvidence(helper.RevocationEvidence);
        if (evidenceStatus != RevocationVerificationStatus.Unknown)
        {
            return evidenceStatus;
        }

        if (helper.RevocationStatus != RevocationVerificationStatus.Unknown)
        {
            return helper.RevocationStatus;
        }

        return helper.BindingPhase == AuthorityBindingPhase.Revoked
            ? RevocationVerificationStatus.NotSupported
            : RevocationVerificationStatus.Pending;
    }

    private static Diagnostic? RevocationDiagnostic(RevocationVerificationStatus revocation) =>
        revocation switch
        {
            RevocationVerificationStatus.Pending => AuthorityDiagnostic(
                DiagnosticSeverity.Warning,
                "AppleVirtualization.AuthorityRevocationPending",
                "Authority binding revocation was accepted but could not be verified yet.",
                "authority.revoke"),
            RevocationVerificationStatus.Failed => AuthorityDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.AuthorityRevocationVerificationFailed",
                "Authority binding revocation verification failed.",
                "authority.revoke"),
            RevocationVerificationStatus.NotSupported => AuthorityDiagnostic(
                DiagnosticSeverity.Warning,
                "AppleVirtualization.AuthorityRevocationVerificationUnsupported",
                "Authority binding revocation verification is not supported by the helper response.",
                "authority.revoke"),
            _ => null,
        };

    private static RevocationVerificationStatus MapRevocationEvidence(
        IReadOnlyList<AppleVirtualizationAuthorityRevocationEvidence> evidence)
    {
        if (evidence.Count == 0)
        {
            return RevocationVerificationStatus.Unknown;
        }

        bool hasUnsupported = false;
        bool hasVerified = false;
        for (int i = 0; i < evidence.Count; i++)
        {
            AppleVirtualizationAuthorityRevocationEvidence item = evidence[i];
            switch (item.Kind)
            {
                case AppleVirtualizationAuthorityRevocationEvidenceKind.ListenerStillRegistered:
                case AppleVirtualizationAuthorityRevocationEvidenceKind.ConnectionFileDescriptorOpen:
                case AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketPresent:
                    if (item.Observed)
                    {
                        return RevocationVerificationStatus.Failed;
                    }

                    break;
                case AppleVirtualizationAuthorityRevocationEvidenceKind.ListenerRemoved:
                case AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketAbsent:
                    hasVerified |= item.Observed;
                    break;
                case AppleVirtualizationAuthorityRevocationEvidenceKind.ConnectionFileDescriptorClosed:
                    hasVerified |= item.Observed || item.FileDescriptor == -1;
                    break;
                case AppleVirtualizationAuthorityRevocationEvidenceKind.Unsupported:
                    hasUnsupported = true;
                    break;
            }
        }

        if (hasVerified)
        {
            return RevocationVerificationStatus.Verified;
        }

        return hasUnsupported
            ? RevocationVerificationStatus.NotSupported
            : RevocationVerificationStatus.Pending;
    }

    private void DetachVerifiedBindingFromTarget(
        ResourceRef<AuthorityBinding> binding,
        AuthorityBindingSpec spec)
    {
        if (spec.Target.Kind == AuthorityTargetKind.ExecutionUnit &&
            spec.Target.Unit is { } unit)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> lookup =
                _ledger.TryGetExecutionUnit(unit);
            if (lookup.Succeeded && lookup.Entry is not null)
            {
                _ledger.DetachAuthorityBindingFromExecutionUnit(lookup.Entry.Resource, binding);
            }
        }
    }

    internal static Diagnostic? ValidateProjectionPolicy(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingSpec spec,
        AppleVirtualizationAuthoritySourceClassification classification,
        AppleVirtualizationProviderStateLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(ledger);

        if (ValidateProjectionShape(spec, classification) is { } projectionDiagnostic)
        {
            return projectionDiagnostic;
        }

        return ValidateTarget(metadata, spec, ledger);
    }

    private Diagnostic? ValidateProjectionPolicy(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingSpec spec,
        AppleVirtualizationAuthoritySourceClassification classification) =>
        ValidateProjectionPolicy(metadata, spec, classification, _ledger);

    private static Diagnostic? ValidateProjectionShape(
        AuthorityBindingSpec spec,
        AppleVirtualizationAuthoritySourceClassification classification)
    {
        switch (spec.Projection.Kind)
        {
            case AuthorityProjectionKind.SocketPath:
                if (spec.Projection.TargetSocketPath is null)
                {
                    return AuthorityDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.AuthoritySocketProjectionMissingPath",
                        "Socket-path authority projection requires a target socket path.",
                        "authority.projection.targetSocketPath");
                }

                if (classification.SensitiveEndpointKind is SensitiveEndpointKind.TrustService or SensitiveEndpointKind.FunctionDebug)
                {
                    return UnsupportedProjectionDiagnostic(
                        spec.Projection.Kind,
                        "Trust mutation and host-function authorities require proven guest-agent projection semantics before socket projection can be enabled.");
                }

                if (classification.SensitiveEndpointKind == SensitiveEndpointKind.EngineSocket)
                {
                    return ValidateEngineSocketProjection(spec);
                }

                return null;
            case AuthorityProjectionKind.EnvironmentReference:
                if (string.IsNullOrWhiteSpace(spec.Projection.EnvironmentVariableName))
                {
                    return AuthorityDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.AuthorityEnvironmentProjectionMissingName",
                        "Environment-reference authority projection requires an environment variable name.",
                        "authority.projection.environmentVariableName");
                }

                return null;
            case AuthorityProjectionKind.FileDescriptor:
            case AuthorityProjectionKind.ProxyEndpoint:
            case AuthorityProjectionKind.AgentProtocol:
            case AuthorityProjectionKind.TrustStore:
            case AuthorityProjectionKind.TypedCallback:
            case AuthorityProjectionKind.ProviderDefined:
                return UnsupportedProjectionDiagnostic(
                    spec.Projection.Kind,
                    "The Apple Virtualization provider has not proven this authority projection kind through guest-agent projection yet.");
            default:
                return UnsupportedProjectionDiagnostic(
                    spec.Projection.Kind,
                    "The Apple Virtualization provider does not support this authority projection kind.");
        }
    }

    private static Diagnostic? ValidateEngineSocketProjection(AuthorityBindingSpec spec)
    {
        if (spec.Source.Kind != AuthoritySourceKind.UnixSocket)
        {
            return AuthorityDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.AuthorityEngineSocketSourceUnsupported",
                "Engine API authority projection requires a UnixSocket source observed inside the runtime host.",
                "authority.source.kind");
        }

        if (spec.Source.Locus != BoundaryLocus.RuntimeHost)
        {
            return AuthorityDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.AuthorityEngineSocketLocusRejected",
                "Engine API sockets must originate at the runtime-host guest locus; host, provider, and external engine sockets fail closed.",
                "authority.source.locus");
        }

        if (spec.Source.SocketPath is not { } sourcePath || !IsAbsolutePath(sourcePath))
        {
            return AuthorityDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.AuthorityEngineSocketSourcePathInvalid",
                "Engine API authority projection requires an absolute guest-visible source socket path.",
                "authority.source.socketPath");
        }

        if (spec.Projection.TargetSocketPath is not { } targetPath || !IsAbsolutePath(targetPath))
        {
            return AuthorityDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.AuthorityEngineSocketTargetPathInvalid",
                "Engine API authority projection requires an absolute target socket path inside the execution unit.",
                "authority.projection.targetSocketPath");
        }

        if (spec.Target.Kind != AuthorityTargetKind.ExecutionUnit)
        {
            return AuthorityDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.AuthorityEngineSocketTargetUnsupported",
                "Engine API authority projection must target the execution unit that will consume the socket.",
                "authority.target.kind");
        }

        if (spec.Policy.Direction != AuthorityBindingDirection.ProviderToGuest)
        {
            return AuthorityDiagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.AuthorityEngineSocketDirectionRejected",
                "Engine API authority projection must use provider-to-guest direction through AuthorityBinding.",
                "authority.policy.direction");
        }

        return null;
    }

    private static bool IsAbsolutePath(UnixSocketPath path) =>
        path.Value.Length > 0 && path.Value[0] == '/';

    private static Diagnostic? ValidateTarget(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingSpec spec,
        AppleVirtualizationProviderStateLedger ledger)
    {
        switch (spec.Target.Kind)
        {
            case AuthorityTargetKind.ExecutionUnit:
                if (spec.Target.Unit is not { } unit)
                {
                    return AuthorityDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.AuthorityTargetUnitMissing",
                        "Execution-unit authority targets must include an execution-unit handle.",
                        "authority.target.unit");
                }

                AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> unitLookup =
                    ledger.TryGetExecutionUnit(unit);
                if (!unitLookup.Succeeded || unitLookup.Entry is null)
                {
                    return unitLookup.Diagnostic ?? AuthorityDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.AuthorityTargetUnitMissing",
                        "The authority binding target execution unit could not be resolved.",
                        "authority.target.unit");
                }

                ExecutionUnitStatus unitStatus = unitLookup.Entry.Status;
                if (unitStatus.Phase != ResourcePhase.Ready ||
                    unitStatus.UnitPhase is not (ExecutionUnitPhase.Ready or ExecutionUnitPhase.Running))
                {
                    return AuthorityDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.AuthorityTargetUnitNotReady",
                        "The authority binding target execution unit is not ready for authority projection.",
                        "authority.target.unit");
                }

                return null;
            case AuthorityTargetKind.ProcessInvocation:
                if (spec.Target.Process is not { } process)
                {
                    return AuthorityDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.AuthorityTargetProcessMissing",
                        "Process authority targets must include a process handle.",
                        "authority.target.process");
                }

                AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>> processLookup =
                    ledger.TryGetProcessInvocation(process);
                if (!processLookup.Succeeded || processLookup.Entry is null)
                {
                    return processLookup.Diagnostic ?? AuthorityDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.AuthorityTargetProcessMissing",
                        "The authority binding target process could not be resolved.",
                        "authority.target.process");
                }

                ProcessInvocationStatus processStatus = processLookup.Entry.Status;
                if (processStatus.Phase != ResourcePhase.Ready ||
                    processStatus.ProcessPhase is ProcessInvocationPhase.Stopping or ProcessInvocationPhase.Stopped or ProcessInvocationPhase.Exited or ProcessInvocationPhase.Failed)
                {
                    return AuthorityDiagnostic(
                        DiagnosticSeverity.Error,
                        "AppleVirtualization.AuthorityTargetProcessNotReady",
                        "The authority binding target process is not ready for authority projection.",
                        "authority.target.process");
                }

                return null;
            case AuthorityTargetKind.Service:
                return AuthorityDiagnostic(
                    DiagnosticSeverity.Error,
                    "AppleVirtualization.AuthorityTargetServiceUnsupported",
                    "Service authority targets require service-to-unit ownership semantics that are not implemented in this L13 slice.",
                    "authority.target.service");
            case AuthorityTargetKind.FunctionSandbox:
                return AuthorityDiagnostic(
                    DiagnosticSeverity.Error,
                    "AppleVirtualization.AuthorityTargetFunctionSandboxUnsupported",
                    "Function sandbox authority projection is deferred until the function sandbox lane is implemented.",
                    "authority.target.functionSandbox");
            default:
                return AuthorityDiagnostic(
                    DiagnosticSeverity.Error,
                    "AppleVirtualization.AuthorityTargetUnsupported",
                    "Authority target kind is not supported by the Apple Virtualization provider.",
                    "authority.target");
        }
    }

    private static Diagnostic UnsupportedProjectionDiagnostic(AuthorityProjectionKind projectionKind, string message) =>
        AuthorityDiagnostic(
            DiagnosticSeverity.Error,
            "AppleVirtualization.AuthorityProjectionUnsupported",
            message + " Projection kind: " + projectionKind + ".",
            "authority.projection.kind");

    private AuthorityBindingStatus ApplyRuntimeLeaseState(
        ResourceRef<AuthorityBinding> binding,
        AuthorityBindingStatus status)
    {
        AuthorityBindingSpec? spec = _ledger.TryGetAuthorityBindingSpec(binding);
        if (status.BindingPhase != AuthorityBindingPhase.Projected || spec is null || status.BoundAuthority is not { } bound)
        {
            return status;
        }

        DateTimeOffset now = _now();
        if (bound.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            Diagnostic diagnostic = AuthorityDiagnostic(
                DiagnosticSeverity.Warning,
                "AppleVirtualization.AuthorityLeaseExpired",
                "The authority binding lease expired; the provider will not treat this authority as usable.",
                "authority.lease.expiresAt");
            AuthorityBindingStatus expired = status with
            {
                Phase = ResourcePhase.Degraded,
                BindingPhase = AuthorityBindingPhase.Degraded,
                BoundAuthority = bound with { RevocationStatus = RevocationVerificationStatus.Pending },
                Diagnostics = AppendIfMissing(diagnostic, status.Diagnostics),
                LastTransitionAt = now,
            };
            _ledger.UpdateAuthorityBindingStatus(binding, expired, Audit(binding, spec, AuthorityAuditKind.Degraded, now, ReasonCode: diagnostic.Code.Value));
            return expired;
        }

        if (spec.Policy.Lease.RevokeOnTargetStop && _ledger.IsAuthorityTargetStopped(spec))
        {
            Diagnostic diagnostic = AuthorityDiagnostic(
                DiagnosticSeverity.Warning,
                "AppleVirtualization.AuthorityTargetStoppedRevocationPending",
                "The authority binding target stopped; revocation is marked pending until the revocation verification wave completes.",
                "authority.target");
            AuthorityBindingStatus revoked = status with
            {
                Phase = ResourcePhase.Deleting,
                BindingPhase = AuthorityBindingPhase.Revoking,
                BoundAuthority = bound with { RevocationStatus = RevocationVerificationStatus.Pending },
                Diagnostics = AppendIfMissing(diagnostic, status.Diagnostics),
                LastTransitionAt = now,
            };
            _ledger.UpdateAuthorityBindingStatus(binding, revoked, Audit(binding, spec, AuthorityAuditKind.Revoked, now, ReasonCode: diagnostic.Code.Value));
            return revoked;
        }

        return status;
    }

    private AuthorityBindingStatus Store(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingSpec spec,
        AuthorityBindingStatus status,
        IReadOnlyList<AuthorityAuditEvent>? auditEvents = null) =>
        _ledger.UpsertAuthorityBinding(metadata, status, spec, auditEvents).Status;

    private AppleVirtualizationHelperEnvelope Request(AppleVirtualizationHelperOperation operation) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            "apple-vz-authority-" + Interlocked.Increment(ref _requestSequence).ToString(CultureInfo.InvariantCulture),
            Interlocked.Read(ref _requestSequence),
            AppleVirtualizationHelperProtocol.AuthorityBindingRequestSchema);

    private static AppleVirtualizationAuthorityBindingRequest RequestFromSpec(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingSpec spec,
        AppleVirtualizationAuthorityBindingAction action,
        AppleVirtualizationAuthoritySourceClassification classification,
        LeaseComputation lease) =>
        RequestFromSpec(metadata.Id.Value, spec, action, classification, lease);

    private static AppleVirtualizationAuthorityBindingRequest RequestFromSpec(
        string bindingId,
        AuthorityBindingSpec spec,
        AppleVirtualizationAuthorityBindingAction action,
        AppleVirtualizationAuthoritySourceClassification classification,
        LeaseComputation lease) =>
        new()
        {
            BindingId = bindingId,
            Action = action,
            Source = SourceDescriptor(spec.Source, classification),
            Target = TargetDescriptor(spec.Target),
            Projection = ProjectionDescriptor(spec.Projection),
            Direction = spec.Policy.Direction,
            RequestedAuthorityClass = spec.Policy.AuthorityClass,
            EffectiveAuthorityClass = classification.AuthorityClass,
            Redaction = classification.Redaction,
            RequireAudit = spec.Policy.RequireAudit,
            AllowProviderSideProxy = spec.Policy.AllowProviderSideProxy,
            AuditLabel = RedactedAuditLabel(spec.AuditLabel),
            AuditCorrelationId = AuditCorrelationId(bindingId),
            Lease = new AppleVirtualizationAuthorityLeaseDescriptor
            {
                Lifetime = spec.Policy.Lease.Lifetime,
                BoundAt = lease.BoundAt,
                ExpiresAt = lease.ExpiresAt,
                RevokeOnTargetStop = spec.Policy.Lease.RevokeOnTargetStop,
                SurviveTargetRestart = spec.Policy.Lease.SurviveTargetRestart,
                RevocationGracePeriodMilliseconds = spec.Policy.Lease.RevocationGracePeriod is { } grace
                    ? checked((int)Math.Min(grace.TotalMilliseconds, int.MaxValue))
                    : null,
            },
        };

    private static AppleVirtualizationAuthoritySourceDescriptor SourceDescriptor(
        AuthorityBindingSource source,
        AppleVirtualizationAuthoritySourceClassification classification) =>
        new()
        {
            Kind = source.Kind,
            Locus = source.Locus,
            HostService = source.HostService,
            SocketPath = null,
            Credential = null,
            ProviderCapabilityName = source.ProviderCapabilityName,
            SensitiveEndpointKind = classification.SensitiveEndpointKind,
            AuthorityClass = classification.AuthorityClass,
            RedactedDisplayName = classification.RedactedDisplayName,
        };

    private static AppleVirtualizationAuthorityTargetDescriptor TargetDescriptor(AuthorityBindingTarget target) =>
        new()
        {
            Kind = target.Kind,
            UnitId = target.Unit?.Route.BackingResourceId,
            ProcessId = target.Process?.Route.BackingResourceId,
            ServiceName = target.ServiceName?.Value,
            Locus = target.Locus,
        };

    private static AppleVirtualizationAuthorityProjectionDescriptor ProjectionDescriptor(AuthorityBindingProjection projection) =>
        new()
        {
            Kind = projection.Kind,
            TargetSocketPath = projection.TargetSocketPath,
            EnvironmentVariableName = RedactedEnvironmentName(projection.EnvironmentVariableName),
            SocketPermissions = projection.SocketPermissions,
            ReadOnly = projection.ReadOnly,
        };

    private static AuthorityBindingStatus StatusFromHelper(
        ResourceGeneration observedGeneration,
        AuthorityBindingSpec spec,
        AppleVirtualizationAuthoritySourceClassification classification,
        AppleVirtualizationAuthorityBindingResponse helper,
        LeaseComputation lease)
    {
        AuthorityBindingPhase phase = helper.BindingPhase;
        ResourcePhase resourcePhase = phase switch
        {
            AuthorityBindingPhase.Projected => ResourcePhase.Ready,
            AuthorityBindingPhase.Failed => ResourcePhase.Failed,
            AuthorityBindingPhase.Degraded => ResourcePhase.Degraded,
            AuthorityBindingPhase.Revoking => ResourcePhase.Deleting,
            AuthorityBindingPhase.Revoked => ResourcePhase.Deleted,
            _ => ResourcePhase.Reconciling,
        };

        return new AuthorityBindingStatus
        {
            Phase = resourcePhase,
            ObservedGeneration = observedGeneration,
            LastTransitionAt = DateTimeOffset.UtcNow,
            BindingPhase = phase,
            BoundAuthority = BoundAuthorityFromHelper(spec, classification, helper.BoundAuthority, lease),
            Limitations = helper.Limitations,
            Conditions = MergeConditions(
                AuthorityConditions(observedGeneration, phase, helper.AuditEvents.Count != 0 && !helper.AuditEventsTruncated),
                helper.Conditions),
            Diagnostics = AppendIfMissing(
                AuthorityDiagnostic(DiagnosticSeverity.Info, classification.DiagnosticCode, classification.DiagnosticMessage, "authority.source"),
                DiagnosticsFromHelperConditions(phase, helper.Conditions, helper.Diagnostics)),
            Extensions = MergeAuthorityEvidenceExtension(Array.Empty<ProviderExtensionData>(), helper),
        };
    }

    private static AuthorityBindingStatus FailedStatus(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingSpec spec,
        AppleVirtualizationAuthoritySourceClassification classification,
        Diagnostic diagnostic) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            BindingPhase = AuthorityBindingPhase.Failed,
            BoundAuthority = null,
            Limitations =
            [
                new NetworkLimitation(
                    NetworkDegradedFeature.BindingAudit,
                    CapabilityDegradationMode.DisabledByPolicy,
                    diagnostic.Code.Value,
                    diagnostic.Message),
            ],
            Conditions = AuthorityConditions(metadata.Generation, AuthorityBindingPhase.Failed, auditRecorded: spec.Policy.RequireAudit is false),
            Diagnostics =
            [
                diagnostic,
                AuthorityDiagnostic(DiagnosticSeverity.Info, classification.DiagnosticCode, classification.DiagnosticMessage, "authority.source"),
            ],
        };

    private static BoundAuthority? BoundAuthorityFromHelper(
        AuthorityBindingSpec spec,
        AppleVirtualizationAuthoritySourceClassification classification,
        AppleVirtualizationGuestAgentBoundAuthority? helper,
        LeaseComputation lease)
    {
        if (helper is null)
        {
            return null;
        }

        return new BoundAuthority
        {
            SourceKind = helper.SourceKind,
            ProjectionKind = helper.ProjectionKind,
            Direction = helper.Direction,
            EffectiveAuthorityClass = classification.AuthorityClass,
            TargetSocketPath = spec.Projection.Kind == AuthorityProjectionKind.SocketPath ? helper.TargetSocketPath : null,
            EnvironmentVariableName = spec.Projection.Kind == AuthorityProjectionKind.EnvironmentReference ? helper.EnvironmentVariableName : null,
            HostFunctionName = spec.Source.HostFunction?.Name,
            BoundAt = helper.BoundAt == default ? lease.BoundAt : helper.BoundAt,
            ExpiresAt = helper.ExpiresAt ?? lease.ExpiresAt,
            RotationGeneration = helper.RotationGeneration,
            RevocationStatus = helper.RevocationStatus,
            AuditCorrelationId = helper.AuditCorrelationId,
        };
    }

    private static LeaseComputation ComputeLease(SensitiveLeasePolicy policy, DateTimeOffset now)
    {
        DateTimeOffset? expiresAt = policy.ExpiresAfter is { } expiresAfter ? now + expiresAfter : null;
        if (policy.ExpiresAfter is { } duration && duration <= TimeSpan.Zero)
        {
            return new LeaseComputation(
                now,
                expiresAt,
                AuthorityDiagnostic(
                    DiagnosticSeverity.Error,
                    "AppleVirtualization.AuthorityLeaseAlreadyExpired",
                    "Authority binding lease duration must be greater than zero.",
                    "authority.lease.expiresAfter"));
        }

        return new LeaseComputation(now, expiresAt, Diagnostic: null);
    }

    private static IReadOnlyList<AuthorityAuditEvent> Audit(
        ResourceMetadata<AuthorityBinding> metadata,
        AuthorityBindingSpec spec,
        AuthorityAuditKind kind,
        DateTimeOffset timestamp,
        string? ReasonCode = null) =>
        Audit(
            new ResourceRef<AuthorityBinding>(metadata.Id, metadata.Scope, metadata.Generation),
            spec,
            kind,
            timestamp,
            ReasonCode);

    private static IReadOnlyList<AuthorityAuditEvent> Audit(
        ResourceRef<AuthorityBinding> binding,
        AuthorityBindingSpec spec,
        AuthorityAuditKind kind,
        DateTimeOffset timestamp,
        string? ReasonCode = null) =>
    [
        new AuthorityAuditEvent
        {
            Binding = binding,
            Kind = kind,
            SourceKind = spec.Source.Kind,
            TargetKind = spec.Target.Kind,
            Timestamp = timestamp,
            Actor = RedactedActor(spec.Policy.Provenance?.Actor),
            CorrelationId = AuditCorrelationId(binding.Id.Value),
            ReasonCode = ReasonCode is null ? null : new DiagnosticCode(ReasonCode),
        },
    ];

    private static IReadOnlyList<Condition> AuthorityConditions(
        ResourceGeneration generation,
        AuthorityBindingPhase phase,
        bool auditRecorded) =>
    [
        new Condition(
            "AppleVirtualization.AuthorityBindingReady",
            phase == AuthorityBindingPhase.Projected ? ConditionStatus.True : ConditionStatus.False,
            phase.ToString(),
            phase == AuthorityBindingPhase.Projected
                ? "The authority binding has a lease-bound provider handle."
                : "The authority binding is not projected.",
            DateTimeOffset.UtcNow,
            generation,
            phase == AuthorityBindingPhase.Failed ? DiagnosticSeverity.Error : DiagnosticSeverity.Info),
        new Condition(
            "AppleVirtualization.AuthorityAuditRecorded",
            auditRecorded ? ConditionStatus.True : ConditionStatus.False,
            auditRecorded ? "AuditRecorded" : "AuditPending",
            auditRecorded
                ? "The authority binding recorded bounded audit metadata."
                : "The authority binding has not recorded audit metadata.",
            DateTimeOffset.UtcNow,
            generation,
            auditRecorded ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning),
    ];

    private static IReadOnlyList<Condition> MergeConditions(
        IReadOnlyList<Condition> first,
        IReadOnlyList<Condition> second)
    {
        if (first.Count == 0)
        {
            return second;
        }

        if (second.Count == 0)
        {
            return first;
        }

        var result = new Condition[first.Count + second.Count];
        for (int i = 0; i < first.Count; i++)
        {
            result[i] = first[i];
        }

        for (int i = 0; i < second.Count; i++)
        {
            result[first.Count + i] = second[i];
        }

        return result;
    }

    private static IReadOnlyList<Diagnostic> DiagnosticsFromHelperConditions(
        AuthorityBindingPhase phase,
        IReadOnlyList<Condition> conditions,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        IReadOnlyList<Diagnostic> result = diagnostics;
        for (int i = 0; i < conditions.Count; i++)
        {
            Condition condition = conditions[i];
            if (phase == AuthorityBindingPhase.Projected &&
                condition.Severity < DiagnosticSeverity.Warning)
            {
                continue;
            }

            result = AppendIfMissing(
                AuthorityDiagnostic(
                    condition.Severity < DiagnosticSeverity.Warning ? DiagnosticSeverity.Warning : condition.Severity,
                    condition.Type,
                    condition.Message,
                    "authority.status"),
                result);
        }

        return result;
    }

    private static IReadOnlyList<Diagnostic> AppendDiagnostics(
        IReadOnlyList<Diagnostic> first,
        IReadOnlyList<Diagnostic> second)
    {
        IReadOnlyList<Diagnostic> result = first;
        for (int i = 0; i < second.Count; i++)
        {
            result = AppendIfMissing(second[i], result);
        }

        return result;
    }

    private static IReadOnlyList<ProviderExtensionData> MergeAuthorityEvidenceExtension(
        IReadOnlyList<ProviderExtensionData> existing,
        AppleVirtualizationAuthorityBindingResponse helper)
    {
        ProviderExtensionData extension = CreateAuthorityEvidenceExtension(helper);
        if (existing.Count == 0)
        {
            return [extension];
        }

        int existingEvidenceIndex = -1;
        for (int i = 0; i < existing.Count; i++)
        {
            if (existing[i].ProviderId == AppleVirtualizationProviderDescriptor.ProviderId &&
                existing[i].SchemaId == AuthorityEvidenceExtensionSchema)
            {
                existingEvidenceIndex = i;
                break;
            }
        }

        if (existingEvidenceIndex >= 0)
        {
            var result = new ProviderExtensionData[existing.Count];
            for (int i = 0; i < existing.Count; i++)
            {
                result[i] = i == existingEvidenceIndex ? extension : existing[i];
            }

            return result;
        }

        var appended = new ProviderExtensionData[existing.Count + 1];
        for (int i = 0; i < existing.Count; i++)
        {
            appended[i] = existing[i];
        }

        appended[^1] = extension;
        return appended;
    }

    private static ProviderExtensionData CreateAuthorityEvidenceExtension(
        AppleVirtualizationAuthorityBindingResponse helper)
    {
        var extension = new AppleVirtualizationAuthorityEvidenceExtension
        {
            BindingId = helper.BindingId,
            BindingPhase = helper.BindingPhase,
            RevocationStatus = helper.RevocationStatus,
            AuditEventsTruncated = helper.AuditEventsTruncated,
            Conditions = TrimConditions(helper.Conditions),
            RevocationEvidence = TrimEvidence(helper.RevocationEvidence),
        };

        return new ProviderExtensionData(
            AppleVirtualizationProviderDescriptor.ProviderId,
            AuthorityEvidenceExtensionSchema,
            AuthorityEvidenceExtensionContentType,
            JsonSerializer.SerializeToUtf8Bytes(
                extension,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationAuthorityEvidenceExtension));
    }

    private static IReadOnlyList<Condition> TrimConditions(IReadOnlyList<Condition> conditions)
    {
        if (conditions.Count <= MaxPersistedConditions)
        {
            return conditions;
        }

        var result = new Condition[MaxPersistedConditions];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = conditions[i];
        }

        return result;
    }

    private static IReadOnlyList<AppleVirtualizationAuthorityRevocationEvidence> TrimEvidence(
        IReadOnlyList<AppleVirtualizationAuthorityRevocationEvidence> evidence)
    {
        if (evidence.Count <= MaxPersistedEvidenceItems)
        {
            return evidence;
        }

        var result = new AppleVirtualizationAuthorityRevocationEvidence[MaxPersistedEvidenceItems];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = evidence[i];
        }

        return result;
    }

    private static string AuditCorrelationId(string bindingId) => "authority-" + bindingId;

    private static string? RedactedEnvironmentName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name;

    private static string? RedactedAuditLabel(string? label) =>
        string.IsNullOrWhiteSpace(label) ? null : label.Length > 64 ? label[..64] : label;

    private static string? RedactedActor(string? actor) =>
        string.IsNullOrWhiteSpace(actor) ? null : actor.Length > 64 ? actor[..64] : actor;

    private static IReadOnlyList<Diagnostic> Append(Diagnostic diagnostic, IReadOnlyList<Diagnostic> to)
    {
        if (to.Count == 0)
        {
            return [diagnostic];
        }

        Diagnostic[] result = new Diagnostic[to.Count + 1];
        result[0] = diagnostic;
        for (int i = 0; i < to.Count; i++)
        {
            result[i + 1] = to[i];
        }

        return result;
    }

    private static Diagnostic[] AddDiagnostic(Diagnostic[]? diagnostics, Diagnostic diagnostic)
    {
        if (diagnostics is null)
        {
            return [diagnostic];
        }

        var result = new Diagnostic[diagnostics.Length + 1];
        Array.Copy(diagnostics, result, diagnostics.Length);
        result[^1] = diagnostic;
        return result;
    }

    private static UntypedResourceRef[] Trim(UntypedResourceRef[] resources, int count)
    {
        if (resources.Length == count)
        {
            return resources;
        }

        var result = new UntypedResourceRef[count];
        Array.Copy(resources, result, count);
        return result;
    }

    private static IReadOnlyList<Diagnostic> AppendIfMissing(Diagnostic diagnostic, IReadOnlyList<Diagnostic> to)
    {
        for (int i = 0; i < to.Count; i++)
        {
            if (to[i].Code == diagnostic.Code)
            {
                return to;
            }
        }

        return Append(diagnostic, to);
    }

    private static Diagnostic ToDiagnostic(AppleVirtualizationHelperError? error, string operation) =>
        error is null
            ? AuthorityDiagnostic(DiagnosticSeverity.Error, "AppleVirtualization.AuthorityHelperError", "The Apple Virtualization helper failed the authority operation.", operation)
            : AuthorityDiagnostic(error.Severity, error.Code, error.Message, error.Operation ?? operation);

    private static Diagnostic AuthorityDiagnostic(DiagnosticSeverity severity, string code, string message, string targetPath) =>
        new()
        {
            Severity = severity,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    private readonly record struct LeaseComputation(
        DateTimeOffset BoundAt,
        DateTimeOffset? ExpiresAt,
        Diagnostic? Diagnostic);
}

public sealed record AppleVirtualizationAuthorityEvidenceExtension
{
    public string EvidenceProtocolVersion { get; init; } = "v1";
    public required string BindingId { get; init; }
    public AuthorityBindingPhase BindingPhase { get; init; }
    public RevocationVerificationStatus RevocationStatus { get; init; }
    public bool AuditEventsTruncated { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<AppleVirtualizationAuthorityRevocationEvidence> RevocationEvidence { get; init; } =
        Array.Empty<AppleVirtualizationAuthorityRevocationEvidence>();
}
