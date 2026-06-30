namespace HPD.Environment.AppleVirtualization.Projections;

using System.Globalization;
using System.Text;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class AppleVirtualizationContentProjectionProvider : IContentProjectionProvider, IRuntimeFinalizationParticipant
{
    internal const string HostShareConfiguredCondition = "AppleVirtualization.HostShareConfigured";
    internal const string GuestMountVerifiedCondition = "AppleVirtualization.GuestMountVerified";
    internal const string DirectHostMutationCondition = "AppleVirtualization.DirectHostMutationAllowed";
    internal const string SyncNoopCondition = "AppleVirtualization.SyncNoopLiveProjection";
    internal const string FinalizationNoopCondition = "AppleVirtualization.FinalizationNoopLiveProjection";
    internal const string SyncFailedCondition = "AppleVirtualization.ProjectionSyncFailed";
    internal const string FinalizationFailedCondition = "AppleVirtualization.ProjectionFinalizationFailed";
    internal const string SyncConflictPolicyCondition = "AppleVirtualization.ProjectionSyncConflictPolicy";
    internal const string SyncResultBoundedCondition = "AppleVirtualization.ProjectionSyncResultBounded";
    internal const string FinalizationResultBoundedCondition = "AppleVirtualization.ProjectionFinalizationResultBounded";
    internal const string InvalidHandleCondition = "AppleVirtualization.ProjectionHandleInvalid";
    internal const int MaxProjectionDiagnosticMessageLength = 512;

    internal static readonly DiagnosticCode HostPathDenied = new("AppleVirtualization.HostPathDenied");
    internal static readonly DiagnosticCode HostPathUnavailable = new("AppleVirtualization.HostPathUnavailable");
    internal static readonly DiagnosticCode DirectHostMutationDenied = new("AppleVirtualization.DirectHostMutationDenied");
    internal static readonly DiagnosticCode ProjectionUnsupported = new("AppleVirtualization.ProjectionUnsupported");
    internal static readonly DiagnosticCode HelperProjectionFailed = new("AppleVirtualization.HelperProjectionFailed");
    internal static readonly DiagnosticCode ProjectionGuestNotReady = new("AppleVirtualization.ProjectionGuestNotReady");
    internal static readonly DiagnosticCode ProjectionGuestNotVisible = new("AppleVirtualization.ProjectionGuestNotVisible");
    internal static readonly DiagnosticCode ProjectionMountPathMissing = new("AppleVirtualization.ProjectionMountPathMissing");
    internal static readonly DiagnosticCode ProjectionAccessMismatch = new("AppleVirtualization.ProjectionAccessMismatch");
    internal static readonly DiagnosticCode ProjectionWriteModeDegraded = new("AppleVirtualization.ProjectionWriteModeDegraded");
    internal static readonly DiagnosticCode ProjectionCoherenceUnverified = new("AppleVirtualization.ProjectionCoherenceUnverified");
    internal static readonly DiagnosticCode ProjectionResponseMissing = new("AppleVirtualization.ProjectionResponseMissing");
    internal static readonly DiagnosticCode ProjectionGuestBootMismatch = new("AppleVirtualization.ProjectionGuestBootMismatch");

    private readonly IAppleVirtualizationHelperClient _helper;
    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private long _requestSequence;

    public AppleVirtualizationContentProjectionProvider(
        IAppleVirtualizationHelperClient helper,
        AppleVirtualizationProviderStateLedger ledger)
    {
        _helper = helper ?? throw new ArgumentNullException(nameof(helper));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;

    public async ValueTask<ContentProjectionStatus> ProjectAsync(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionSpec spec,
        TargetHandle<RuntimeHost>? host,
        TargetHandle<ExecutionUnit>? unit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        Diagnostic? targetDiagnostic = ValidateTarget(spec, host, unit);
        if (targetDiagnostic is not null)
        {
            return Store(metadata, Failed(metadata, spec, targetDiagnostic), spec).Status;
        }

        ProjectionPlan plan = CreatePlan(metadata, spec);
        if (plan.Diagnostic is not null)
        {
            return Store(metadata, plan.Status!, spec).Status;
        }

        AppleVirtualizationHelperEnvelope configure = await SendProjectionAsync(
            AppleVirtualizationHelperOperation.ProjectionConfigure,
            metadata,
            request => request with
            {
                ProjectionConfigureRequest = new AppleVirtualizationProjectionConfigureRequest
                {
                    ProjectionId = metadata.Id.Value,
                    HostPath = plan.HostPath!,
                    Tag = plan.Tag!,
                    AccessMode = spec.AccessMode,
                    Realization = plan.Realization,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (configure.Error is not null)
        {
            return Store(metadata, Failed(metadata, spec, ToDiagnostic(configure.Error, AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProjectionConfigure)))).Status;
        }

        AppleVirtualizationHelperEnvelope mount = await SendProjectionAsync(
            AppleVirtualizationHelperOperation.ProjectionMount,
            metadata,
            request => request with
            {
                ProjectionMountRequest = new AppleVirtualizationProjectionMountRequest
                {
                    ProjectionId = metadata.Id.Value,
                    HostId = TargetHostId(host, spec),
                    HostPath = plan.HostPath!,
                    Tag = plan.Tag!,
                    GuestPath = plan.GuestPath!.Value.Value,
                    AccessMode = spec.AccessMode,
                    Realization = plan.Realization,
                    RequestedWriteEffect = EffectiveWriteEffect(spec),
                    RequestedCoherence = spec.Realization.RequestedCoherence,
                    Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(
                        ProviderGeneration: _ledger.ProviderGeneration,
                        ProjectionGeneration: (ulong)Math.Max(0, metadata.Generation.Value)),
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (mount.Error is not null)
        {
            return Store(metadata, Failed(metadata, spec, ToDiagnostic(mount.Error, AppleVirtualizationHelperOperationNames.ToWireName(AppleVirtualizationHelperOperation.ProjectionMount)))).Status;
        }

        string? expectedGuestBootGeneration = ExpectedHostGuestBootGeneration(host, spec);
        ContentProjectionStatus status = FromHelperResponse(metadata, spec, plan, mount.ProjectionStatusResponse, expectedGuestBootGeneration);
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry = Store(metadata, status, spec);
        ContentProjectionStatus withLedgerHandle = status with
        {
            ProviderHandle = entry.ProviderHandle,
            Views = WithProviderHandle(status.Views, entry.ProviderHandle),
        };

        ContentProjectionStatus stored = Store(metadata, withLedgerHandle, spec).Status;
        if (stored.Phase == ResourcePhase.Ready &&
            stored.ProjectionPhase == ContentProjectionPhase.Projected &&
            TryGetTargetUnit(spec, unit, out ResourceRef<ExecutionUnit> targetUnit))
        {
            _ledger.AttachContentProjectionToExecutionUnit(
                targetUnit,
                new ResourceRef<ContentProjection>(metadata.Id, metadata.Scope, metadata.Generation));
        }

        return stored;
    }

    public async ValueTask EnumerateEntriesAsync(
        ResourceRef<ContentProjection> projection,
        IContentProjectionEntrySink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> lookup =
            _ledger.TryGetContentProjection(projection);
        if (!lookup.Succeeded)
        {
            return;
        }

        IReadOnlyList<RealizedProjectionView> views = lookup.Entry!.Status.Views;
        for (int i = 0; i < views.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GuestPath? guestPath = views[i].GuestPath;
            if (guestPath is not null)
            {
                await sink.OnEntryAsync(
                    new ContentProjectionEntry(ContentProjectionEntryKind.Directory, guestPath.Value, new ByteSize(0), Digest: null, LastModifiedAt: null),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask<SyncResult> SyncAsync(
        TargetHandle<ContentProjection> projection,
        SyncRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> lookup =
            _ledger.TryGetContentProjection(projection);
        if (!lookup.Succeeded)
        {
            return InvalidSyncResult(lookup.Diagnostic);
        }

        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry = lookup.Entry!;
        if (!IsVerifiedProjection(entry.Status))
        {
            return FailedSyncResult(
                GenerationOf(entry),
                "ProjectionNotVerified",
                "Content projection sync requires a guest-verified projected resource.",
                DiagnosticSeverity.Warning);
        }

        GuestPath? guestPath = FirstGuestPath(entry.Status);
        if (guestPath is null)
        {
            return FailedSyncResult(
                GenerationOf(entry),
                "ProjectionGuestPathMissing",
                "Content projection sync requires a realized guest path.",
                DiagnosticSeverity.Error);
        }

        SyncMode mode = request.OverrideMode ?? SyncMode.Manual;
        SyncDirection direction = request.OverrideDirection ?? SyncDirection.TargetToSource;
        ConflictPolicy conflictPolicy = request.OverrideConflictPolicy ?? ConflictPolicy.RecordConflict;
        if (SyncConflictPolicyPreflight(conflictPolicy, GenerationOf(entry)) is { } preflightFailure)
        {
            return preflightFailure;
        }

        AppleVirtualizationHelperEnvelope response = await SendProjectionAsync(
            AppleVirtualizationHelperOperation.ProjectionSync,
            MetadataFrom(entry),
            helperRequest => helperRequest with
            {
                PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionSyncRequestSchema,
                ProjectionSyncRequest = new AppleVirtualizationProjectionSyncRequest
                {
                    ProjectionId = entry.Resource.Id.Value,
                    HostId = HostIdForStoredProjection(entry),
                    GuestPath = guestPath.Value.Value,
                    Mode = mode,
                    Direction = direction,
                    ConflictPolicy = conflictPolicy,
                    DryRun = request.DryRun,
                    Generation = GenerationStampFor(entry),
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.Error is not null)
        {
            return FailedSyncResult(response.Error, GenerationOf(entry));
        }

        AppleVirtualizationGuestAgentProjectionSyncResult? sync = response.ProjectionSyncResult;
        if (sync is null)
        {
            return FailedSyncResult(
                GenerationOf(entry),
                "MissingSyncResult",
                "Projection sync helper response did not include a sync result.",
                DiagnosticSeverity.Error);
        }

        var checkpoint = new SyncCheckpoint(
            sync.CheckpointVersion,
            sync.CompletedAt,
            TargetManifestDigest: sync.ChangeSummary.ManifestDigest,
            Changes: sync.ChangeSummary);
        IReadOnlyList<Condition> conditions = BuildSyncConditions(sync, conflictPolicy, GenerationOf(entry));
        var result = new SyncResult(
            checkpoint,
            sync.Conflicts,
            conditions);

        if (IsSyncSuccess(sync) &&
            !sync.DryRun &&
            !HasFalseCondition(conditions, SyncFailedCondition) &&
            !HasFalseCondition(conditions, SyncConflictPolicyCondition))
        {
            ContentProjectionStatus status = entry.Status with
            {
                ChangeSummary = sync.ChangeSummary,
                LastSync = checkpoint,
                Conditions = Append(entry.Status.Conditions, conditions),
                Diagnostics = Append(entry.Status.Diagnostics, sync.Diagnostics),
                LastTransitionAt = sync.CompletedAt,
            };
            _ledger.UpsertContentProjection(MetadataFrom(entry), status);
        }

        return result;
    }

    public async ValueTask<FinalizationResult> FinalizeAsync(
        TargetHandle<ContentProjection> projection,
        FinalizationRequest request,
        IExecutionEventSink? events = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> lookup =
            _ledger.TryGetContentProjection(projection);
        if (!lookup.Succeeded)
        {
            return InvalidFinalizationResult(lookup.Diagnostic);
        }

        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry = lookup.Entry!;
        if (!IsVerifiedProjection(entry.Status))
        {
            return FailedFinalizationResult(
                GenerationOf(entry),
                "ProjectionNotVerified",
                "Content projection finalization requires a guest-verified projected resource.",
                DiagnosticSeverity.Warning);
        }

        GuestPath? guestPath = FirstGuestPath(entry.Status);
        if (guestPath is null)
        {
            return FailedFinalizationResult(
                GenerationOf(entry),
                "ProjectionGuestPathMissing",
                "Content projection finalization requires a realized guest path.",
                DiagnosticSeverity.Error);
        }

        if (!IsSupportedFinalizationKind(request.Kind))
        {
            return FailedFinalizationResult(
                GenerationOf(entry),
                "UnsupportedKind",
                $"Finalization kind '{request.Kind}' is not supported by the Apple Virtualization provider content projection slice.",
                DiagnosticSeverity.Warning);
        }

        AppleVirtualizationHelperEnvelope response = await SendProjectionAsync(
            AppleVirtualizationHelperOperation.ProjectionFinalize,
            MetadataFrom(entry),
            helperRequest => helperRequest with
            {
                PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionFinalizationRequestSchema,
                ProjectionFinalizationRequest = new AppleVirtualizationProjectionFinalizationRequest
                {
                    ProjectionId = entry.Resource.Id.Value,
                    HostId = HostIdForStoredProjection(entry),
                    GuestPath = guestPath.Value.Value,
                    Kind = request.Kind,
                    IncludeProvenance = request.IncludeProvenance,
                    IncludeDeletedEntries = request.IncludeDeletedEntries,
                    ProducerId = request.ProducerId,
                    Generation = GenerationStampFor(entry),
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.Error is not null)
        {
            return FailedFinalizationResult(response.Error, GenerationOf(entry));
        }

        AppleVirtualizationGuestAgentProjectionFinalizationResult? finalization = response.ProjectionFinalizationResult;
        if (finalization is null)
        {
            return FailedFinalizationResult(
                GenerationOf(entry),
                "MissingFinalizationResult",
                "Projection finalization helper response did not include a finalization result.",
                DiagnosticSeverity.Error);
        }

        var result = new FinalizationResult
        {
            CompletedAt = finalization.CompletedAt,
            ManifestDigest = finalization.ManifestDigest,
            Content = finalization.Content,
            Conflicts = finalization.Conflicts,
            Conditions = BuildFinalizationConditions(finalization, GenerationOf(entry)),
        };

        if (IsFinalizationSuccess(finalization) &&
            !HasFalseCondition(result.Conditions, FinalizationFailedCondition))
        {
            ContentProjectionStatus status = entry.Status with
            {
                LastFinalization = result,
                Conditions = Append(entry.Status.Conditions, result.Conditions),
                Diagnostics = Append(entry.Status.Diagnostics, finalization.Diagnostics),
                LastTransitionAt = finalization.CompletedAt,
            };
            _ledger.UpsertContentProjection(MetadataFrom(entry), status);
        }

        return result;
    }

    public async ValueTask ReleaseAsync(TargetHandle<ContentProjection> projection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> lookup =
            _ledger.TryGetContentProjection(projection);
        if (!lookup.Succeeded)
        {
            return;
        }

        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry = lookup.Entry!;
        await SendProjectionAsync(
            AppleVirtualizationHelperOperation.ProjectionRelease,
            MetadataFrom(entry),
            request => request with
            {
                ProjectionLifecycleRequest = new AppleVirtualizationProjectionLifecycleRequest
                {
                    ProjectionId = entry.Resource.Id.Value,
                    FinalizeBeforeRelease = false,
                    Reason = "release",
                },
            },
            cancellationToken).ConfigureAwait(false);

        _ledger.RemoveContentProjection(entry.Resource);
    }

    public async ValueTask<RuntimeFinalizationResult> FinalizeRuntimeAsync(
        RuntimeFinalizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>[] projections =
            _ledger.GetContentProjections(request.RuntimeScope);

        if (projections.Length == 0)
        {
            return new RuntimeFinalizationResult { RuntimeScope = request.RuntimeScope };
        }

        List<FinalizationResult>? finalizations = null;
        List<WorkspaceConflict>? conflicts = null;
        List<UntypedResourceRef>? retained = null;
        List<Diagnostic>? diagnostics = null;

        for (int i = 0; i < projections.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> projection = projections[i];
            ContentProjectionSpec? spec = _ledger.TryGetContentProjectionSpec(projection.Resource);
            if (!RequiresRuntimeFinalization(spec))
            {
                continue;
            }

            FinalizationResult result = await FinalizeAsync(
                projection.TargetHandle,
                RuntimeFinalizationRequestFor(request, spec),
                events: null,
                cancellationToken).ConfigureAwait(false);

            finalizations ??= [];
            finalizations.Add(result);
            AppendConflicts(ref conflicts, result.Conflicts);

            if (FinalizationSucceeded(result))
            {
                continue;
            }

            Diagnostic diagnostic = RuntimeFinalizationDiagnostic(projection, result);
            diagnostics ??= [];
            diagnostics.Add(diagnostic);

            switch (request.CleanupPolicy.FailureMode)
            {
                case CleanupFailureMode.BestEffortRelease:
                    await ReleaseAsync(projection.TargetHandle, cancellationToken).ConfigureAwait(false);
                    break;
                case CleanupFailureMode.MarkDegradedAndRetain:
                    MarkProjectionDegradedAndRetained(projection, diagnostic);
                    retained ??= [];
                    retained.Add(ToUntyped(projection.Resource));
                    break;
                default:
                    retained ??= [];
                    retained.Add(ToUntyped(projection.Resource));
                    break;
            }
        }

        return new RuntimeFinalizationResult
        {
            RuntimeScope = request.RuntimeScope,
            ContentProjections = finalizations is null ? Array.Empty<FinalizationResult>() : finalizations.ToArray(),
            RetainedResources = retained is null ? Array.Empty<UntypedResourceRef>() : retained.ToArray(),
            Conflicts = conflicts is null ? Array.Empty<WorkspaceConflict>() : conflicts.ToArray(),
            Diagnostics = diagnostics is null ? Array.Empty<Diagnostic>() : diagnostics.ToArray(),
        };
    }

    private Diagnostic? ValidateTarget(ContentProjectionSpec spec, TargetHandle<RuntimeHost>? host, TargetHandle<ExecutionUnit>? unit)
    {
        if (host is not null)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> hostLookup = _ledger.TryGetRuntimeHost(host.Value);
            if (!hostLookup.Succeeded)
            {
                return hostLookup.Diagnostic;
            }
        }
        else if (spec.Target.Host is { } targetHost)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> hostLookup = _ledger.TryGetRuntimeHost(targetHost);
            if (!hostLookup.Succeeded)
            {
                return hostLookup.Diagnostic;
            }
        }

        if (unit is not null)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> unitLookup = _ledger.TryGetExecutionUnit(unit.Value);
            if (!unitLookup.Succeeded)
            {
                return unitLookup.Diagnostic;
            }
        }
        else if (spec.Target.Unit is { } targetUnit)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> unitLookup = _ledger.TryGetExecutionUnit(targetUnit);
            if (!unitLookup.Succeeded)
            {
                return unitLookup.Diagnostic;
            }
        }

        return null;
    }

    private bool TryGetTargetUnit(
        ContentProjectionSpec spec,
        TargetHandle<ExecutionUnit>? unit,
        out ResourceRef<ExecutionUnit> targetUnit)
    {
        if (unit is not null)
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> lookup =
                _ledger.TryGetExecutionUnit(unit.Value);
            if (lookup.Succeeded)
            {
                targetUnit = lookup.Entry!.Resource;
                return true;
            }
        }
        else if (spec.Target.Unit is { } unitRef)
        {
            targetUnit = unitRef;
            return true;
        }

        targetUnit = default;
        return false;
    }

    private ProjectionPlan CreatePlan(ResourceMetadata<ContentProjection> metadata, ContentProjectionSpec spec)
    {
        if (spec.Source.Kind != ContentSelectorKind.HostPath || spec.Source.HostPath is null)
        {
            return FallbackOrFailed(metadata, spec, ProjectionFallbackReason.Unsupported, ProjectionUnsupported, "Only host path directory sources are supported by the first Apple projection provider slice.");
        }

        HostPathSelection source = spec.Source.HostPath;
        if (!spec.SecurityPolicy.AllowHostPathSource)
        {
            return FailedPlan(metadata, spec, HostPathDenied, "Host path projection requires ContentProjectionSecurityPolicy.AllowHostPathSource.");
        }

        if (source.Kind != HostPathKind.Directory && source.Kind != HostPathKind.Any)
        {
            return FallbackOrFailed(metadata, spec, ProjectionFallbackReason.Unsupported, ProjectionUnsupported, "Apple virtiofs projection only supports directory tree sources in this slice.");
        }

        if (source.RequireExists && !Directory.Exists(source.Path.Value))
        {
            return FailedPlan(metadata, spec, HostPathUnavailable, $"Host path '{source.Path.Value}' does not exist or is not a directory.");
        }

        if (spec.AccessMode == AccessMode.ReadWrite && !spec.SecurityPolicy.AllowDirectSourceMutation)
        {
            return FailedPlan(metadata, spec, DirectHostMutationDenied, "Read-write host path projection requires ContentProjectionSecurityPolicy.AllowDirectSourceMutation.");
        }

        if (spec.AccessMode is AccessMode.CopyOnWrite or AccessMode.AppendOnly or AccessMode.WriteOnly)
        {
            return FallbackOrFailed(metadata, spec, ProjectionFallbackReason.Unsupported, ProjectionUnsupported, $"Access mode '{spec.AccessMode}' requires a staged projection fallback.");
        }

        GuestPath? guestPath = spec.View.GuestPath;
        if (guestPath is null)
        {
            return FailedPlan(metadata, spec, ProjectionUnsupported, "Filesystem projection requires a guest path.");
        }

        return new ProjectionPlan(source.Path.Value, CreateTag(metadata), guestPath.Value, ProjectionRealizationKind.LiveProjection);
    }

    private ProjectionPlan FallbackOrFailed(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionSpec spec,
        ProjectionFallbackReason reason,
        DiagnosticCode code,
        string message)
    {
        if (!spec.Realization.Fallback.AllowFallback)
        {
            return FailedPlan(metadata, spec, code, message);
        }

        ProjectionRealizationKind fallback = spec.Realization.Fallback.PreferredFallback ?? ProjectionRealizationKind.CopyIn;
        ProjectionWriteEffect writeEffect = fallback == ProjectionRealizationKind.CopyOut
            ? ProjectionWriteEffect.FinalizePromote
            : ProjectionWriteEffect.StagedTargetWrite;
        ContentProjectionStatus status = Status(
            metadata,
            spec,
            ResourcePhase.Degraded,
            ContentProjectionPhase.Degraded,
            View(
                spec,
                spec.AccessMode,
                fallback,
                writeEffect,
                CoherenceClass.ManualRefresh,
                CacheBehavior.ProviderDefined,
                ProjectionFallbackStatus: new ProjectionFallbackStatus(true, reason, fallback, message),
                Limitations:
                [
                    new ContentProjectionLimitation(ContentProjectionDegradedFeature.LiveProjection, CapabilityDegradationMode.PartiallyAvailable, "AppleVirtualization.FallbackSelected", message),
                ],
                Diagnostics:
                [
                    Diagnostic(code, metadata.Id.Value, message, DiagnosticSeverity.Warning),
                ]),
            Diagnostics:
            [
                Diagnostic(code, metadata.Id.Value, message, DiagnosticSeverity.Warning),
            ]);

        return new ProjectionPlan(null, null, spec.View.GuestPath, fallback, status);
    }

    private ProjectionPlan FailedPlan(ResourceMetadata<ContentProjection> metadata, ContentProjectionSpec spec, DiagnosticCode code, string message)
    {
        ContentProjectionStatus status = Failed(metadata, spec, Diagnostic(code, metadata.Id.Value, message, DiagnosticSeverity.Error));
        return new ProjectionPlan(null, null, spec.View.GuestPath, ProjectionRealizationKind.ProviderDefault, status);
    }

    private ContentProjectionStatus FromHelperResponse(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionSpec spec,
        ProjectionPlan plan,
        AppleVirtualizationProjectionStatusResponse? response,
        string? expectedGuestBootGeneration)
    {
        AppleVirtualizationGuestAgentProjectionStatus? guestProjection = response?.GuestProjectionStatus;
        bool guestBootMatches = GuestBootGenerationMatches(expectedGuestBootGeneration, guestProjection?.Generation ?? default);
        bool guestVerified = response?.ReadyForHpdUse == true && guestBootMatches;
        ContentProjectionPhase projectionPhase = guestVerified ? ContentProjectionPhase.Projected : ContentProjectionPhase.Projecting;
        ResourcePhase phase = guestVerified ? ResourcePhase.Ready : ResourcePhase.Reconciling;
        AccessMode effectiveAccess = guestProjection?.EffectiveAccessMode ?? spec.AccessMode;
        ProjectionRealizationKind realization = EffectiveRealization(plan, response, guestProjection);
        ProjectionWriteEffect writeEffect = EffectiveWriteEffect(spec, response, guestProjection);
        CoherenceClass coherence = guestProjection?.EffectiveCoherence ?? response?.EffectiveCoherence ?? CoherenceClass.ProviderDefined;
        CacheBehavior cache = guestProjection?.EffectiveCache ?? (spec.Realization.Cache == CacheBehavior.Unknown ? CacheBehavior.ProviderDefined : spec.Realization.Cache);
        List<Condition> conditions =
        [
            Condition(HostShareConfiguredCondition, ConditionStatus.True, "HelperConfigured", "Helper configured the host-side virtiofs share.", metadata.Generation, DiagnosticSeverity.Info),
        ];
        List<Diagnostic> diagnostics = [];
        if (response is not null)
        {
            conditions.AddRange(BoundConditions(response.Conditions));
            diagnostics.AddRange(BoundDiagnostics(response.Diagnostics));
        }

        if (guestProjection is not null)
        {
            conditions.AddRange(BoundConditions(guestProjection.Conditions));
            diagnostics.AddRange(BoundDiagnostics(guestProjection.Diagnostics));
        }

        List<ContentProjectionLimitation> classifiedLimitations = [];
        AddProjectionFailureDiagnostics(
            metadata,
            spec,
            response,
            guestProjection,
            expectedGuestBootGeneration,
            guestBootMatches,
            conditions,
            diagnostics,
            classifiedLimitations);

        if (guestVerified)
        {
            if (!HasTrueCondition(conditions, GuestMountVerifiedCondition))
            {
                conditions.Add(Condition(GuestMountVerifiedCondition, ConditionStatus.True, "GuestVerified", "Guest agent verified the projection mount, path, access, and readiness state.", metadata.Generation, DiagnosticSeverity.Info));
            }
        }
        else
        {
            conditions.Add(Condition(GuestMountVerifiedCondition, ConditionStatus.False, "GuestMountPending", "Projection is not reported as projected until guest mount verification is observed.", metadata.Generation, DiagnosticSeverity.Warning));
        }

        IReadOnlyList<ContentProjectionLimitation> limitations = CombineLimitations(
            CombineLimitations(Limitations(coherence, cache), guestProjection?.Limitations),
            classifiedLimitations);

        return Status(
            metadata,
            spec,
            phase,
            projectionPhase,
            View(
                spec,
                effectiveAccess,
                realization,
                writeEffect,
                coherence,
                cache,
                Conditions: conditions,
                Limitations: limitations),
            Conditions: conditions,
            Diagnostics: diagnostics);
    }

    private ContentProjectionStatus Failed(ResourceMetadata<ContentProjection> metadata, ContentProjectionSpec spec, Diagnostic diagnostic) =>
        Status(
            metadata,
            spec,
            ResourcePhase.Failed,
            ContentProjectionPhase.Failed,
            View(
                spec,
                spec.AccessMode,
                ProjectionRealizationKind.ProviderDefault,
                ProjectionWriteEffect.Unknown,
                CoherenceClass.Unknown,
                CacheBehavior.Unknown,
                Diagnostics: [diagnostic]),
            Diagnostics: [diagnostic]);

    private static ContentProjectionStatus Status(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionSpec spec,
        ResourcePhase phase,
        ContentProjectionPhase projectionPhase,
        RealizedProjectionView view,
        IReadOnlyList<Condition>? Conditions = null,
        IReadOnlyList<Diagnostic>? Diagnostics = null) =>
        new()
        {
            Phase = phase,
            ProjectionPhase = projectionPhase,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Views = [view],
            Conditions = Conditions ?? Array.Empty<Condition>(),
            Diagnostics = Diagnostics ?? Array.Empty<Diagnostic>(),
        };

    private static RealizedProjectionView View(
        ContentProjectionSpec spec,
        AccessMode effectiveAccess,
        ProjectionRealizationKind realization,
        ProjectionWriteEffect writeEffect,
        CoherenceClass coherence,
        CacheBehavior cache,
        ProjectionFallbackStatus? ProjectionFallbackStatus = null,
        IReadOnlyList<ContentProjectionLimitation>? Limitations = null,
        IReadOnlyList<Condition>? Conditions = null,
        IReadOnlyList<Diagnostic>? Diagnostics = null) =>
        new()
        {
            Kind = spec.View.Kind,
            GuestPath = spec.View.GuestPath,
            EffectiveAccess = effectiveAccess,
            EffectiveRealization = realization,
            EffectiveWriteEffect = writeEffect,
            EffectiveCoherence = coherence,
            EffectiveCache = cache,
            EffectiveSymlinkPolicy = new EffectiveSymlinkPolicy(SymlinkPolicy.ProviderDefault, "Apple virtiofs symlink behavior is provider-defined until real guest tests pin it down."),
            EffectiveIdentityMapping = new EffectiveIdentityMapping(IdentityMappingPolicy.CurrentUser, "current-host-user"),
            ReadOnlyEnforcement = effectiveAccess == AccessMode.ReadOnly
                ? new ReadOnlyEnforcementStatus(ReadOnlyEnforcementPolicy.HostEnforced, Enforced: true, "VZSharedDirectory readOnly=true enforces host-side read-only access.")
                : new ReadOnlyEnforcementStatus(ReadOnlyEnforcementPolicy.ProviderDefault, Enforced: false, "Read-write projection mutates the host source through the helper process effective user."),
            FileEvents = spec.Realization.FileEvents is null
                ? null
                : new FileEventBridgeStatus(FileEventBridgePhase.Unsupported, spec.Realization.FileEvents.Direction, spec.Realization.FileEvents.Mask, Detail: "Apple Virtualization does not provide a shared-directory file-event stream."),
            Fallback = ProjectionFallbackStatus ?? new ProjectionFallbackStatus(false),
            Limitations = Limitations ?? Array.Empty<ContentProjectionLimitation>(),
            Conditions = Conditions ?? Array.Empty<Condition>(),
        };

    private async ValueTask<AppleVirtualizationHelperEnvelope> SendProjectionAsync(
        AppleVirtualizationHelperOperation operation,
        ResourceMetadata<ContentProjection> metadata,
        Func<AppleVirtualizationHelperEnvelope, AppleVirtualizationHelperEnvelope> configure,
        CancellationToken cancellationToken)
    {
        long sequence = Interlocked.Increment(ref _requestSequence);
        AppleVirtualizationHelperEnvelope request = AppleVirtualizationHelperEnvelope.Request(
            operation,
            string.Create(CultureInfo.InvariantCulture, $"{metadata.Id.Value}:{sequence}"),
            sequence,
            AppleVirtualizationHelperProtocol.ProjectionRequestSchema) with
        {
            ResourceKind = metadata.Kind,
            ResourceId = metadata.Id.Value,
            ResourceScope = metadata.Scope,
            ResourceGeneration = metadata.Generation,
            ProviderGeneration = _ledger.ProviderGeneration,
        };

        return await _helper.SendAsync(configure(request), cancellationToken).ConfigureAwait(false);
    }

    private static bool IsVerifiedProjection(ContentProjectionStatus status) =>
        status.Phase == ResourcePhase.Ready &&
        status.ProjectionPhase == ContentProjectionPhase.Projected &&
        HasTrueCondition(status.Conditions, GuestMountVerifiedCondition);

    private static GuestPath? FirstGuestPath(ContentProjectionStatus status)
    {
        for (int i = 0; i < status.Views.Count; i++)
        {
            if (status.Views[i].GuestPath is { } guestPath)
            {
                return guestPath;
            }
        }

        return null;
    }

    private static SyncResult? SyncConflictPolicyPreflight(ConflictPolicy conflictPolicy, ResourceGeneration generation) =>
        conflictPolicy switch
        {
            ConflictPolicy.PreferSource or ConflictPolicy.PreferTarget => FailedSyncResult(
                generation,
                "ConflictPolicyUnsupported",
                $"Conflict policy '{conflictPolicy}' would require provider-mediated overwrite semantics that are not proven by this slice.",
                DiagnosticSeverity.Warning,
                SyncConflictPolicyCondition),
            ConflictPolicy.RequireExplicitPromotion => FailedSyncResult(
                generation,
                "ExplicitPromotionRequired",
                "Conflict policy 'RequireExplicitPromotion' requires an explicit finalization or promotion path, not implicit sync mutation.",
                DiagnosticSeverity.Warning,
                SyncConflictPolicyCondition),
            _ => null,
        };

    private static bool IsSyncSuccess(AppleVirtualizationGuestAgentProjectionSyncResult sync) =>
        sync.Succeeded &&
        sync.State is AppleVirtualizationGuestAgentProjectionSyncState.Succeeded or AppleVirtualizationGuestAgentProjectionSyncState.DryRun;

    private static IReadOnlyList<Condition> BuildSyncConditions(
        AppleVirtualizationGuestAgentProjectionSyncResult sync,
        ConflictPolicy conflictPolicy,
        ResourceGeneration generation)
    {
        IReadOnlyList<Condition> conditions = sync.Conditions.Count == 0 ? Array.Empty<Condition>() : sync.Conditions;

        if (!IsSyncSuccess(sync))
        {
            conditions = Append(conditions, Condition(
                SyncFailedCondition,
                ConditionStatus.False,
                sync.State == AppleVirtualizationGuestAgentProjectionSyncState.Unknown ? "SyncFailed" : sync.State.ToString(),
                string.IsNullOrWhiteSpace(sync.UnsupportedReason)
                    ? $"Projection sync did not complete successfully; helper state was '{sync.State}'."
                    : sync.UnsupportedReason!,
                generation,
                DiagnosticSeverity.Warning));
        }

        if (conflictPolicy == ConflictPolicy.Fail && sync.Conflicts.Count > 0)
        {
            conditions = Append(conditions, Condition(
                SyncConflictPolicyCondition,
                ConditionStatus.False,
                "ConflictsRejected",
                "ConflictPolicy.Fail rejected the sync result because conflicts were reported.",
                generation,
                DiagnosticSeverity.Warning));
        }

        if (conflictPolicy == ConflictPolicy.RecordConflict && sync.Conflicts.Count > 0)
        {
            conditions = Append(conditions, Condition(
                SyncConflictPolicyCondition,
                ConditionStatus.True,
                "ConflictsRecorded",
                "Sync reported conflicts and recorded them without selecting a source or target winner.",
                generation,
                DiagnosticSeverity.Warning));
        }

        if (sync.ChangesTruncated)
        {
            conditions = Append(conditions, Condition(
                SyncResultBoundedCondition,
                ConditionStatus.True,
                "ChangesTruncated",
                "Projection sync change list was truncated to the negotiated result bound.",
                generation,
                DiagnosticSeverity.Warning));
        }

        if (sync.ConflictsTruncated)
        {
            conditions = Append(conditions, Condition(
                SyncResultBoundedCondition,
                ConditionStatus.True,
                "ConflictsTruncated",
                "Projection sync conflict list was truncated to the negotiated result bound.",
                generation,
                DiagnosticSeverity.Warning));
        }

        return conditions;
    }

    private static bool IsSupportedFinalizationKind(FinalizationKind kind) =>
        kind is FinalizationKind.ManifestOnly or FinalizationKind.ChangedContent or FinalizationKind.ManifestAndChangedContent;

    private static bool IsFinalizationSuccess(AppleVirtualizationGuestAgentProjectionFinalizationResult finalization) =>
        finalization.Succeeded &&
        finalization.State == AppleVirtualizationGuestAgentProjectionFinalizationState.Succeeded;

    private static IReadOnlyList<Condition> BuildFinalizationConditions(
        AppleVirtualizationGuestAgentProjectionFinalizationResult finalization,
        ResourceGeneration generation)
    {
        IReadOnlyList<Condition> conditions = finalization.Conditions.Count == 0 ? Array.Empty<Condition>() : finalization.Conditions;

        if (!IsFinalizationSuccess(finalization))
        {
            conditions = Append(conditions, Condition(
                FinalizationFailedCondition,
                ConditionStatus.False,
                finalization.State == AppleVirtualizationGuestAgentProjectionFinalizationState.Unknown ? "FinalizationFailed" : finalization.State.ToString(),
                string.IsNullOrWhiteSpace(finalization.UnsupportedReason)
                    ? $"Projection finalization did not complete successfully; helper state was '{finalization.State}'."
                    : finalization.UnsupportedReason!,
                generation,
                DiagnosticSeverity.Warning));
        }

        if (finalization.ContentTruncated)
        {
            conditions = Append(conditions, Condition(
                FinalizationResultBoundedCondition,
                ConditionStatus.True,
                "ContentRefsTruncated",
                "Projection finalization content refs were truncated to the negotiated result bound.",
                generation,
                DiagnosticSeverity.Warning));
        }

        if (finalization.ConflictsTruncated)
        {
            conditions = Append(conditions, Condition(
                FinalizationResultBoundedCondition,
                ConditionStatus.True,
                "ConflictsTruncated",
                "Projection finalization conflict list was truncated to the negotiated result bound.",
                generation,
                DiagnosticSeverity.Warning));
        }

        return conditions;
    }

    private static bool HasFalseCondition(IReadOnlyList<Condition>? conditions, string type)
    {
        if (conditions is null)
        {
            return false;
        }

        for (int i = 0; i < conditions.Count; i++)
        {
            if (string.Equals(conditions[i].Type, type, StringComparison.Ordinal) &&
                conditions[i].Status == ConditionStatus.False)
            {
                return true;
            }
        }

        return false;
    }

    private static string HostIdForStoredProjection(AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry) =>
        entry.TargetHandle.Route.Segments.Count > 0
            ? entry.TargetHandle.Route.Segments[0].Value
            : "unknown-host";

    private AppleVirtualizationGuestAgentProjectionGenerationStamp GenerationStampFor(
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry) =>
        new(
            ProviderGeneration: _ledger.ProviderGeneration,
            ProjectionGeneration: (ulong)Math.Max(0, GenerationOf(entry).Value));

    private static string TargetHostId(TargetHandle<RuntimeHost>? host, ContentProjectionSpec spec) =>
        host?.Route.BackingResourceId ??
        spec.Target.Host?.Id.Value ??
        "unknown-host";

    private string? ExpectedHostGuestBootGeneration(TargetHandle<RuntimeHost>? host, ContentProjectionSpec spec)
    {
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> lookup;
        if (host is not null)
        {
            lookup = _ledger.TryGetRuntimeHost(host.Value);
        }
        else if (spec.Target.Host is { } targetHost)
        {
            lookup = _ledger.TryGetRuntimeHost(targetHost);
        }
        else
        {
            return null;
        }

        return lookup.Succeeded
            ? lookup.Entry!.Status.Generations.GuestBootGeneration?.Value
            : null;
    }

    private AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> Store(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionStatus status,
        ContentProjectionSpec? spec = null) =>
        _ledger.UpsertContentProjection(metadata, status, spec);

    private static bool RequiresRuntimeFinalization(ContentProjectionSpec? spec) =>
        spec?.FinalizationPolicy is FinalizationPolicy.OnRuntimeEnd or FinalizationPolicy.Required;

    private static FinalizationRequest RuntimeFinalizationRequestFor(
        RuntimeFinalizationRequest request,
        ContentProjectionSpec? spec) =>
        new()
        {
            Kind = FinalizationKind.ManifestAndChangedContent,
            IncludeDeletedEntries = true,
            IncludeProvenance = true,
            ProducerId = request.PromoteMemory
                ? "runtime-finalization-promote-memory"
                : "runtime-finalization",
        };

    internal static bool FinalizationSucceeded(FinalizationResult result)
    {
        if (result.CompletedAt == DateTimeOffset.UnixEpoch)
        {
            return false;
        }

        return !HasFalseCondition(result.Conditions, FinalizationFailedCondition) &&
            !HasFalseCondition(result.Conditions, InvalidHandleCondition);
    }

    private void MarkProjectionDegradedAndRetained(
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> projection,
        Diagnostic diagnostic)
    {
        _ledger.UpsertContentProjection(
            MetadataFrom(projection),
            projection.Status with
            {
                Phase = ResourcePhase.Degraded,
                ProjectionPhase = ContentProjectionPhase.Degraded,
                Diagnostics = Append(projection.Status.Diagnostics, [diagnostic]),
                LastTransitionAt = DateTimeOffset.UtcNow,
            });
    }

    private static Diagnostic RuntimeFinalizationDiagnostic(
        AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> projection,
        FinalizationResult result) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode("AppleVirtualization.RuntimeProjectionFinalizationFailed"),
            Message = FirstConditionMessage(result.Conditions) ??
                "Content projection finalization failed during runtime finalization.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "content-projection/" + projection.Resource.Id.Value,
        };

    private static string? FirstConditionMessage(IReadOnlyList<Condition> conditions) =>
        conditions.Count == 0 ? null : conditions[0].Message;

    private static UntypedResourceRef ToUntyped(ResourceRef<ContentProjection> projection) =>
        new(new ResourceKind("content-projection"), projection.Id.Value, projection.Scope, projection.Generation);

    private static void AppendConflicts(ref List<WorkspaceConflict>? target, IReadOnlyList<WorkspaceConflict> conflicts)
    {
        if (conflicts.Count == 0)
        {
            return;
        }

        target ??= [];
        target.AddRange(conflicts);
    }

    private static ResourceMetadata<ContentProjection> MetadataFrom(AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry) =>
        new()
        {
            Id = entry.Resource.Id,
            Kind = new ResourceKind("content-projection"),
            Scope = entry.Resource.Scope,
            Generation = entry.Resource.Generation ?? new ResourceGeneration(0),
            SchemaVersion = new SchemaVersion("v1"),
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
        };

    private static ResourceGeneration GenerationOf(AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus> entry) =>
        entry.Resource.Generation ?? entry.Status.ObservedGeneration;

    private static IReadOnlyList<RealizedProjectionView> WithProviderHandle(IReadOnlyList<RealizedProjectionView> views, ProviderOpaqueHandle providerHandle)
    {
        if (views.Count == 0)
        {
            return Array.Empty<RealizedProjectionView>();
        }

        var projected = new RealizedProjectionView[views.Count];
        for (int i = 0; i < views.Count; i++)
        {
            projected[i] = views[i] with { ProviderHandle = providerHandle };
        }

        return projected;
    }

    private static bool HasTrueCondition(IReadOnlyList<Condition>? conditions, string type)
    {
        if (conditions is null)
        {
            return false;
        }

        for (int i = 0; i < conditions.Count; i++)
        {
            if (string.Equals(conditions[i].Type, type, StringComparison.Ordinal) &&
                conditions[i].Status == ConditionStatus.True)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<Condition> Append(IReadOnlyList<Condition> existing, Condition condition)
    {
        var conditions = new Condition[existing.Count + 1];
        for (int i = 0; i < existing.Count; i++)
        {
            conditions[i] = existing[i];
        }

        conditions[^1] = condition;
        return conditions;
    }

    private static IReadOnlyList<Condition> Append(IReadOnlyList<Condition> existing, IReadOnlyList<Condition> additions)
    {
        if (additions.Count == 0)
        {
            return existing;
        }

        var conditions = new Condition[existing.Count + additions.Count];
        for (int i = 0; i < existing.Count; i++)
        {
            conditions[i] = existing[i];
        }

        for (int i = 0; i < additions.Count; i++)
        {
            conditions[existing.Count + i] = additions[i];
        }

        return conditions;
    }

    private static IReadOnlyList<Diagnostic> Append(IReadOnlyList<Diagnostic> existing, IReadOnlyList<Diagnostic> additions)
    {
        if (additions.Count == 0)
        {
            return existing;
        }

        var diagnostics = new Diagnostic[existing.Count + additions.Count];
        for (int i = 0; i < existing.Count; i++)
        {
            diagnostics[i] = existing[i];
        }

        for (int i = 0; i < additions.Count; i++)
        {
            diagnostics[existing.Count + i] = additions[i];
        }

        return diagnostics;
    }

    private static SyncResult FailedSyncResult(AppleVirtualizationHelperError error, ResourceGeneration generation) =>
        FailedSyncResult(
            generation,
            ToConditionReason(error.Code),
            $"{error.Code}: {BoundMessage(error.Message)}",
            error.Severity);

    private static SyncResult FailedSyncResult(
        ResourceGeneration generation,
        string reason,
        string message,
        DiagnosticSeverity severity,
        string conditionType = SyncFailedCondition)
    {
        Condition condition = Condition(conditionType, ConditionStatus.False, reason, message, generation, severity);
        return new SyncResult(
            new SyncCheckpoint(
                Version: 0,
                CompletedAt: DateTimeOffset.UtcNow,
                Changes: new ContentProjectionChangeSummary()),
            Conflicts: Array.Empty<WorkspaceConflict>(),
            Conditions: [condition]);
    }

    private static FinalizationResult FailedFinalizationResult(AppleVirtualizationHelperError error, ResourceGeneration generation) =>
        FailedFinalizationResult(
            generation,
            ToConditionReason(error.Code),
            $"{error.Code}: {BoundMessage(error.Message)}",
            error.Severity);

    private static FinalizationResult FailedFinalizationResult(
        ResourceGeneration generation,
        string reason,
        string message,
        DiagnosticSeverity severity)
    {
        Condition condition = Condition(FinalizationFailedCondition, ConditionStatus.False, reason, message, generation, severity);
        return new FinalizationResult
        {
            CompletedAt = DateTimeOffset.UnixEpoch,
            Conditions = [condition],
        };
    }

    private static SyncResult InvalidSyncResult(Diagnostic? diagnostic)
    {
        Condition condition = InvalidHandleConditionFor(
            diagnostic,
            "SyncSkippedInvalidHandle",
            "Content projection sync was not performed because the projection handle could not be resolved.");
        return new SyncResult(
            new SyncCheckpoint(
                Version: 0,
                CompletedAt: DateTimeOffset.UtcNow,
                Changes: new ContentProjectionChangeSummary()),
            Conflicts: Array.Empty<WorkspaceConflict>(),
            Conditions: [condition]);
    }

    private static FinalizationResult InvalidFinalizationResult(Diagnostic? diagnostic)
    {
        Condition condition = InvalidHandleConditionFor(
            diagnostic,
            "FinalizationSkippedInvalidHandle",
            "Content projection finalization was not performed because the projection handle could not be resolved.");
        return new FinalizationResult
        {
            CompletedAt = DateTimeOffset.UnixEpoch,
            Conditions = [condition],
        };
    }

    private static Condition InvalidHandleConditionFor(Diagnostic? diagnostic, string fallbackReason, string fallbackMessage)
    {
        string reason = diagnostic?.Code.Value is { Length: > 0 } code ? ToConditionReason(code) : fallbackReason;
        string message = diagnostic is null ? fallbackMessage : $"{diagnostic.Code.Value}: {diagnostic.Message}";
        return new Condition(
            InvalidHandleCondition,
            ConditionStatus.False,
            reason,
            message,
            DateTimeOffset.UtcNow,
            default,
            diagnostic?.Severity ?? DiagnosticSeverity.Error);
    }

    private static string ToConditionReason(string diagnosticCode)
    {
        int separator = diagnosticCode.LastIndexOf('.');
        return separator >= 0 && separator + 1 < diagnosticCode.Length
            ? diagnosticCode[(separator + 1)..]
            : diagnosticCode;
    }

    private static IReadOnlyList<ContentProjectionLimitation> Limitations(CoherenceClass coherence, CacheBehavior cache)
    {
        List<ContentProjectionLimitation> limitations = [];
        if (coherence is CoherenceClass.Unknown or CoherenceClass.ProviderDefined)
        {
            limitations.Add(new ContentProjectionLimitation(ContentProjectionDegradedFeature.Coherence, CapabilityDegradationMode.PartiallyAvailable, "AppleVirtualization.CoherenceProviderDefined", "Apple docs do not define HPD-level virtiofs coherence guarantees."));
        }

        if (cache is CacheBehavior.Unknown or CacheBehavior.ProviderDefined)
        {
            limitations.Add(new ContentProjectionLimitation(ContentProjectionDegradedFeature.Cache, CapabilityDegradationMode.PartiallyAvailable, "AppleVirtualization.CacheProviderDefined", "Apple docs do not define HPD-level virtiofs cache behavior."));
        }

        return limitations.Count == 0 ? Array.Empty<ContentProjectionLimitation>() : limitations;
    }

    private static IReadOnlyList<ContentProjectionLimitation> CombineLimitations(
        IReadOnlyList<ContentProjectionLimitation> providerLimitations,
        IReadOnlyList<ContentProjectionLimitation>? guestLimitations)
    {
        if (guestLimitations is null || guestLimitations.Count == 0)
        {
            return providerLimitations;
        }

        if (providerLimitations.Count == 0)
        {
            return guestLimitations;
        }

        var combined = new ContentProjectionLimitation[providerLimitations.Count + guestLimitations.Count];
        for (int i = 0; i < providerLimitations.Count; i++)
        {
            combined[i] = providerLimitations[i];
        }

        for (int i = 0; i < guestLimitations.Count; i++)
        {
            combined[providerLimitations.Count + i] = guestLimitations[i];
        }

        return combined;
    }

    private static void AddProjectionFailureDiagnostics(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionSpec spec,
        AppleVirtualizationProjectionStatusResponse? response,
        AppleVirtualizationGuestAgentProjectionStatus? guestProjection,
        string? expectedGuestBootGeneration,
        bool guestBootMatches,
        List<Condition> conditions,
        List<Diagnostic> diagnostics,
        List<ContentProjectionLimitation> limitations)
    {
        if (response is null)
        {
            AddDiagnosticAndCondition(
                metadata,
                diagnostics,
                conditions,
                ProjectionResponseMissing,
                "ProjectionResponseMissing",
                "Projection helper response did not include a projection status payload.",
                DiagnosticSeverity.Error);
            return;
        }

        if (!response.GuestAgentReady)
        {
            AddDiagnosticAndCondition(
                metadata,
                diagnostics,
                conditions,
                ProjectionGuestNotReady,
                "GuestAgentNotReady",
                "Guest-agent readiness is required before projection verification can succeed.",
                DiagnosticSeverity.Warning);
        }

        if (!guestBootMatches)
        {
            string observed = FormatGuestBootGeneration(guestProjection?.Generation) ?? "unknown";
            AddDiagnosticAndCondition(
                metadata,
                diagnostics,
                conditions,
                ProjectionGuestBootMismatch,
                "GuestBootMismatch",
                $"Projection was verified against guest boot generation '{observed}', but RuntimeHost currently observes '{expectedGuestBootGeneration}'. Projection requires re-verification.",
                DiagnosticSeverity.Warning);
        }

        if (guestProjection is null)
        {
            if (response.HostShareConfigured || response.FrameworkShareAccepted)
            {
                AddDiagnosticAndCondition(
                    metadata,
                    diagnostics,
                    conditions,
                    ProjectionGuestNotVisible,
                    "ConfiguredButNotVisible",
                    "Host/framework share configuration was accepted, but no guest-visible projection status was reported.",
                    DiagnosticSeverity.Warning);
            }

            return;
        }

        switch (guestProjection.VerificationState)
        {
            case AppleVirtualizationGuestAgentProjectionVerificationState.HostShareConfigured:
            case AppleVirtualizationGuestAgentProjectionVerificationState.FrameworkShareAccepted:
            case AppleVirtualizationGuestAgentProjectionVerificationState.NotVisible:
            case AppleVirtualizationGuestAgentProjectionVerificationState.GuestPathVisible when !guestProjection.Mounted:
                AddDiagnosticAndCondition(
                    metadata,
                    diagnostics,
                    conditions,
                    ProjectionGuestNotVisible,
                    "GuestPathNotMounted",
                    "Projection host/framework share is not yet verified as mounted and visible at the expected guest path.",
                    DiagnosticSeverity.Warning);
                break;
            case AppleVirtualizationGuestAgentProjectionVerificationState.MountPathMissing:
                AddDiagnosticAndCondition(
                    metadata,
                    diagnostics,
                    conditions,
                    ProjectionMountPathMissing,
                    "MountPathMissing",
                    "Guest agent reported that the expected projection mount path is missing.",
                    DiagnosticSeverity.Error);
                break;
            case AppleVirtualizationGuestAgentProjectionVerificationState.AccessMismatch:
                AddAccessMismatch(metadata, spec, guestProjection, diagnostics, conditions, limitations);
                break;
            case AppleVirtualizationGuestAgentProjectionVerificationState.CoherenceUnknown:
            case AppleVirtualizationGuestAgentProjectionVerificationState.CoherenceDegraded:
                AddCoherenceUnverified(metadata, guestProjection, diagnostics, conditions, limitations);
                break;
            case AppleVirtualizationGuestAgentProjectionVerificationState.Failed:
                AddDiagnosticAndCondition(
                    metadata,
                    diagnostics,
                    conditions,
                    HelperProjectionFailed,
                    "GuestProjectionFailed",
                    "Guest agent reported projection verification failed.",
                    DiagnosticSeverity.Error);
                break;
        }

        if (guestProjection.RequestedAccessMode != guestProjection.EffectiveAccessMode)
        {
            AddAccessMismatch(metadata, spec, guestProjection, diagnostics, conditions, limitations);
        }

        ProjectionWriteEffect requestedWriteEffect = EffectiveWriteEffect(spec);
        if (requestedWriteEffect != ProjectionWriteEffect.Unknown &&
            guestProjection.EffectiveWriteEffect != ProjectionWriteEffect.Unknown &&
            guestProjection.EffectiveWriteEffect != requestedWriteEffect)
        {
            AddDiagnosticAndCondition(
                metadata,
                diagnostics,
                conditions,
                ProjectionWriteModeDegraded,
                "WriteModeDegraded",
                $"Projection effective write mode '{guestProjection.EffectiveWriteEffect}' does not match requested write mode '{requestedWriteEffect}'.",
                DiagnosticSeverity.Warning);
            AddLimitationOnce(
                limitations,
                ContentProjectionDegradedFeature.ReadOnlyEnforcement,
                "AppleVirtualization.ProjectionWriteModeDegraded",
                "Projection effective write mode is degraded from the requested mode.");
        }

        if (guestProjection.EffectiveCoherence is CoherenceClass.Unknown or CoherenceClass.ProviderDefined)
        {
            AddCoherenceUnverified(metadata, guestProjection, diagnostics, conditions, limitations);
        }
    }

    private static void AddAccessMismatch(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionSpec spec,
        AppleVirtualizationGuestAgentProjectionStatus guestProjection,
        List<Diagnostic> diagnostics,
        List<Condition> conditions,
        List<ContentProjectionLimitation> limitations)
    {
        AddDiagnosticAndCondition(
            metadata,
            diagnostics,
            conditions,
            ProjectionAccessMismatch,
            "AccessMismatch",
            $"Projection effective access '{guestProjection.EffectiveAccessMode}' does not match requested access '{spec.AccessMode}'.",
            DiagnosticSeverity.Warning);
        AddLimitationOnce(
            limitations,
            ContentProjectionDegradedFeature.ReadOnlyEnforcement,
            "AppleVirtualization.ProjectionAccessMismatch",
            "Guest projection effective access does not match requested access.");
    }

    private static void AddCoherenceUnverified(
        ResourceMetadata<ContentProjection> metadata,
        AppleVirtualizationGuestAgentProjectionStatus guestProjection,
        List<Diagnostic> diagnostics,
        List<Condition> conditions,
        List<ContentProjectionLimitation> limitations)
    {
        AddDiagnosticAndCondition(
            metadata,
            diagnostics,
            conditions,
            ProjectionCoherenceUnverified,
            guestProjection.VerificationState == AppleVirtualizationGuestAgentProjectionVerificationState.CoherenceDegraded ? "CoherenceDegraded" : "CoherenceUnknown",
            $"Projection coherence is not verified as HPD-ready; effective coherence is '{guestProjection.EffectiveCoherence}'.",
            DiagnosticSeverity.Warning);
        AddLimitationOnce(
            limitations,
            ContentProjectionDegradedFeature.Coherence,
            "AppleVirtualization.ProjectionCoherenceUnverified",
            "Guest projection coherence is not verified as ready for HPD use.");
    }

    private static void AddDiagnosticAndCondition(
        ResourceMetadata<ContentProjection> metadata,
        List<Diagnostic> diagnostics,
        List<Condition> conditions,
        DiagnosticCode code,
        string reason,
        string message,
        DiagnosticSeverity severity)
    {
        string boundedMessage = BoundMessage(message);
        diagnostics.Add(Diagnostic(code, metadata.Id.Value, boundedMessage, severity));
        conditions.Add(Condition(code.Value, ConditionStatus.False, reason, boundedMessage, metadata.Generation, severity));
    }

    private static void AddLimitationOnce(
        List<ContentProjectionLimitation> limitations,
        ContentProjectionDegradedFeature feature,
        string reasonCode,
        string message)
    {
        for (int i = 0; i < limitations.Count; i++)
        {
            if (limitations[i].Feature == feature && string.Equals(limitations[i].ReasonCode, reasonCode, StringComparison.Ordinal))
            {
                return;
            }
        }

        limitations.Add(new ContentProjectionLimitation(feature, CapabilityDegradationMode.PartiallyAvailable, reasonCode, message));
    }

    private static bool GuestBootGenerationMatches(
        string? expectedGuestBootGeneration,
        AppleVirtualizationGuestAgentProjectionGenerationStamp observedGeneration)
    {
        if (string.IsNullOrWhiteSpace(expectedGuestBootGeneration))
        {
            return true;
        }

        string? observed = FormatGuestBootGeneration(observedGeneration);
        return observed is null || string.Equals(expectedGuestBootGeneration, observed, StringComparison.Ordinal);
    }

    private static string? FormatGuestBootGeneration(AppleVirtualizationGuestAgentProjectionGenerationStamp generation)
    {
        if (!string.IsNullOrWhiteSpace(generation.GuestBootId) && generation.GuestBootGeneration != 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{generation.GuestBootId}:{generation.GuestBootGeneration}");
        }

        if (!string.IsNullOrWhiteSpace(generation.GuestBootId))
        {
            return generation.GuestBootId;
        }

        return generation.GuestBootGeneration == 0
            ? null
            : generation.GuestBootGeneration.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<Condition> BoundConditions(IReadOnlyList<Condition> conditions)
    {
        if (conditions.Count == 0)
        {
            return conditions;
        }

        Condition[] bounded = new Condition[conditions.Count];
        for (int i = 0; i < conditions.Count; i++)
        {
            Condition condition = conditions[i];
            bounded[i] = condition.Message.Length <= MaxProjectionDiagnosticMessageLength
                ? condition
                : condition with { Message = BoundMessage(condition.Message) };
        }

        return bounded;
    }

    private static IReadOnlyList<Diagnostic> BoundDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return diagnostics;
        }

        Diagnostic[] bounded = new Diagnostic[diagnostics.Count];
        for (int i = 0; i < diagnostics.Count; i++)
        {
            Diagnostic diagnostic = diagnostics[i];
            bounded[i] = diagnostic.Message.Length <= MaxProjectionDiagnosticMessageLength
                ? diagnostic
                : diagnostic with { Message = BoundMessage(diagnostic.Message) };
        }

        return bounded;
    }

    private static string BoundMessage(string message) =>
        message.Length <= MaxProjectionDiagnosticMessageLength
            ? message
            : string.Concat(message.AsSpan(0, MaxProjectionDiagnosticMessageLength - 3), "...");

    private static ProjectionWriteEffect EffectiveWriteEffect(ContentProjectionSpec spec) =>
        spec.AccessMode switch
        {
            AccessMode.ReadOnly => ProjectionWriteEffect.NoWrites,
            AccessMode.ReadWrite => ProjectionWriteEffect.DirectSourceMutation,
            AccessMode.CopyOnWrite => ProjectionWriteEffect.CopyOnWrite,
            AccessMode.AppendOnly => ProjectionWriteEffect.AppendOnlyArtifact,
            AccessMode.WriteOnly => ProjectionWriteEffect.FinalizePromote,
            _ => ProjectionWriteEffect.Unknown,
        };

    private static ProjectionRealizationKind EffectiveRealization(
        ProjectionPlan plan,
        AppleVirtualizationProjectionStatusResponse? response,
        AppleVirtualizationGuestAgentProjectionStatus? guestProjection)
    {
        if (guestProjection is { EffectiveRealization: not ProjectionRealizationKind.ProviderDefault })
        {
            return guestProjection.EffectiveRealization;
        }

        if (response is { EffectiveRealization: not ProjectionRealizationKind.ProviderDefault })
        {
            return response.EffectiveRealization;
        }

        return plan.Realization;
    }

    private static ProjectionWriteEffect EffectiveWriteEffect(
        ContentProjectionSpec spec,
        AppleVirtualizationProjectionStatusResponse? response,
        AppleVirtualizationGuestAgentProjectionStatus? guestProjection)
    {
        if (guestProjection is { EffectiveWriteEffect: not ProjectionWriteEffect.Unknown })
        {
            return guestProjection.EffectiveWriteEffect;
        }

        if (response is { EffectiveWriteEffect: not ProjectionWriteEffect.Unknown })
        {
            return response.EffectiveWriteEffect;
        }

        return EffectiveWriteEffect(spec);
    }

    private static Diagnostic ToDiagnostic(AppleVirtualizationHelperError error, string targetPath) =>
        new()
        {
            Severity = error.Severity,
            Code = string.IsNullOrWhiteSpace(error.Code) ? HelperProjectionFailed : new DiagnosticCode(error.Code),
            Message = BoundMessage(error.Message),
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    private static Diagnostic Diagnostic(DiagnosticCode code, string targetPath, string message, DiagnosticSeverity severity) =>
        new()
        {
            Severity = severity,
            Code = code,
            Message = BoundMessage(message),
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    private static Condition Condition(string type, ConditionStatus status, string reason, string message, ResourceGeneration generation, DiagnosticSeverity severity) =>
        new(type, status, reason, message, DateTimeOffset.UtcNow, generation, severity);

    private static string CreateTag(ResourceMetadata<ContentProjection> metadata)
    {
        Span<char> buffer = stackalloc char[35];
        "hpd".AsSpan().CopyTo(buffer);
        int index = 3;
        string id = metadata.Id.Value;
        for (int i = 0; i < id.Length && index < buffer.Length; i++)
        {
            char c = id[i];
            if (char.IsAsciiLetterOrDigit(c))
            {
                buffer[index++] = char.ToLowerInvariant(c);
            }
        }

        if (index == 3)
        {
            buffer[index++] = 'p';
        }

        string tag = new(buffer[..index]);
        return Encoding.UTF8.GetByteCount(tag) < 36 ? tag : tag[..35];
    }

    private sealed record ProjectionPlan(
        string? HostPath,
        string? Tag,
        GuestPath? GuestPath,
        ProjectionRealizationKind Realization,
        ContentProjectionStatus? Status = null)
    {
        public Diagnostic? Diagnostic => Status?.Diagnostics.Count > 0 ? Status.Diagnostics[0] : null;
    }
}
