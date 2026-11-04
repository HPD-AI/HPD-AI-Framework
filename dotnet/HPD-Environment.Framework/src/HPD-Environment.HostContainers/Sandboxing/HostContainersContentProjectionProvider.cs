namespace HPD.Environment.HostContainers;

using System.Security.Cryptography;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using HPD.Environment.Runtime.Workspaces;

/// <summary>
/// Verifies bind or provider-owned directory projections against the retained
/// sandbox container. Provider-owned content is enumerated through the
/// container process lane.
/// </summary>
internal sealed class HostContainersContentProjectionProvider(
    HostContainersProviderState state,
    IProcessProvider processProvider)
    : IContentProjectionProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("content-projection"),
        TargetRouteSegmentKind.ContentProjection,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control |
        TargetHandleAuthority.Read,
        new SchemaId(
            "hpd.execution.host-containers.content-projection.handle.v1"));

    public ProviderId ProviderId =>
        HostContainersProviderDescriptor.ProviderId;

    public async ValueTask<ContentProjectionStatus> ProjectAsync(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionSpec spec,
        TargetHandle<RuntimeHost>? host,
        TargetHandle<ExecutionUnit>? unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (unit is null ||
            !TryResolveUnit(unit.Value, out var unitEntry))
        {
            return Failed(
                metadata,
                "HostContainers.ProjectionUnitRequired",
                "A retained container-backed execution unit is required.");
        }
        if (unitEntry!.Status.UnitPhase is not (
                ExecutionUnitPhase.Ready or
                ExecutionUnitPhase.Running))
        {
            return Failed(
                metadata,
                "HostContainers.ProjectionUnitNotReady",
                "The retained container execution unit is not ready.");
        }
        if (host is not null &&
            !state.Ledger.TryGet<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>(host.Value).Succeeded)
        {
            return Failed(
                metadata,
                "HostContainers.ProjectionHostInvalid",
                "The projection host is not owned by this provider.");
        }
        HostContainersSandboxContainer? container =
            state.GetSandboxContainer(unitEntry.Resource.Id.Value);
        if (container is null)
        {
            return Failed(
                metadata,
                "HostContainers.ProjectionContainerMissing",
                "The execution unit does not own a retained sandbox container.");
        }
        if (spec.Source.Kind == ContentSelectorKind.ProviderSource)
        {
            if (spec.Source.ProviderSource is not
                {
                    Provider: var sourceProvider,
                } providerSource ||
                !sourceProvider.Equals(ProviderId) ||
                string.IsNullOrWhiteSpace(providerSource.SourceId))
            {
                return Failed(
                    metadata,
                    "HostContainers.ProjectionProviderSourceInvalid",
                    "Provider-owned projections require this provider and a non-empty source identity.");
            }
            if (spec.View.Kind != ProjectionViewKind.FilesystemTree ||
                spec.View.GuestPath is not { } providerDestination)
            {
                return Failed(
                    metadata,
                    "HostContainers.ProjectionViewUnsupported",
                    "Host Containers sandbox projections require a filesystem-tree destination.");
            }
            var rootId = new WorkspaceRootId(
                "projection-" + metadata.Id.Value);
            await using var store = new ProcessWorkspaceFileStore(
                rootId,
                providerDestination.Value,
                spec.AccessMode,
                new WorkspacePathSemantics(
                    true,
                    SymlinkEvaluationPolicy.DenySymlinks),
                processProvider,
                unit.Value,
                "HostContainers");
            WorkspaceFileInfo? sourceInfo =
                await store.GetInfoAsync(
                    new WorkspacePath(rootId, string.Empty),
                    cancellationToken).ConfigureAwait(false);
            if (sourceInfo is not
                { Kind: WorkspaceEntryKind.Directory })
            {
                return Failed(
                    metadata,
                    "HostContainers.ProjectionProviderSourceMissing",
                    "The provider-owned projection source is not a directory visible inside the retained container.");
            }
            ContentProjectionStatus providerStatus = new()
            {
                Phase = ResourcePhase.Ready,
                ObservedGeneration = metadata.Generation,
                ProjectionPhase = ContentProjectionPhase.Projected,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Views =
                [
                    new RealizedProjectionView
                    {
                        Kind = ProjectionViewKind.FilesystemTree,
                        GuestPath = providerDestination,
                        EffectiveAccess = spec.AccessMode,
                        EffectiveRealization =
                            ProjectionRealizationKind.LiveProjection,
                        EffectiveWriteEffect =
                            spec.AccessMode == AccessMode.ReadOnly
                                ? ProjectionWriteEffect.NoWrites
                                : ProjectionWriteEffect.DirectSourceMutation,
                        EffectiveCoherence = CoherenceClass.Strong,
                        EffectiveCache = CacheBehavior.None,
                        EffectiveSymlinkPolicy =
                            new EffectiveSymlinkPolicy(
                                SymlinkPolicy.Deny,
                                "Provider-owned workspace operations reject symlinks."),
                        ReadOnlyEnforcement =
                            new ReadOnlyEnforcementStatus(
                                ReadOnlyEnforcementPolicy.GuestEnforced,
                                Enforced:
                                    spec.AccessMode ==
                                    AccessMode.ReadOnly,
                                "The workspace file store rejects writes for read-only projections."),
                        Conditions = [],
                        Limitations = [],
                    },
                ],
            };
            ProviderResourceEntry<
                ContentProjection,
                ContentProjectionSpec,
                ContentProjectionStatus> providerEntry =
                state.Ledger.Upsert(
                    metadata,
                    spec,
                    providerStatus,
                    Shape);
            providerStatus = providerStatus with
            {
                ProviderHandle = providerEntry.ProviderHandle,
            };
            state.Ledger.Upsert(
                metadata,
                spec,
                providerStatus,
                Shape);
            state.RegisterProjectionUnit(
                metadata.Id.Value,
                unit.Value);
            return providerStatus;
        }
        if (spec.Source.Kind != ContentSelectorKind.HostPath ||
            spec.Source.HostPath is not
                { Kind: HostPathKind.Directory } source ||
            !spec.SecurityPolicy.AllowHostPathSource)
        {
            return Failed(
                metadata,
                "HostContainers.ProjectionSourceUnsupported",
                "Host Containers projections require an explicitly authorized HostPath directory source or a provider-owned source.");
        }
        if (spec.View.Kind != ProjectionViewKind.FilesystemTree ||
            spec.View.GuestPath is not { } destination)
        {
            return Failed(
                metadata,
                "HostContainers.ProjectionViewUnsupported",
                "Host Containers sandbox projections require a filesystem-tree destination.");
        }
        HostContainersSandboxMount? mount = container.FindMount(
            source.Path.Value,
            destination.Value);
        if (mount is null ||
            mount.Access != spec.AccessMode)
        {
            return Failed(
                metadata,
                "HostContainers.ProjectionMountMismatch",
                "The requested projection does not match a bind mount created with the execution unit.");
        }
        if (spec.AccessMode is not AccessMode.ReadOnly &&
            !spec.SecurityPolicy.AllowDirectSourceMutation)
        {
            return Failed(
                metadata,
                "HostContainers.ProjectionWriteNotAuthorized",
                "A writable bind projection requires explicit direct-source mutation authorization.");
        }
        ContentProjectionStatus status = new()
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ProjectionPhase = ContentProjectionPhase.Projected,
            LastTransitionAt = DateTimeOffset.UtcNow,
            Views =
            [
                new RealizedProjectionView
                {
                    Kind = ProjectionViewKind.FilesystemTree,
                    GuestPath = destination,
                    EffectiveAccess = spec.AccessMode,
                    EffectiveRealization =
                        ProjectionRealizationKind.LiveProjection,
                    EffectiveWriteEffect =
                        spec.AccessMode == AccessMode.ReadOnly
                            ? ProjectionWriteEffect.NoWrites
                            : ProjectionWriteEffect.DirectSourceMutation,
                    EffectiveCoherence = CoherenceClass.Strong,
                    EffectiveCache = CacheBehavior.None,
                    EffectiveSymlinkPolicy =
                        new EffectiveSymlinkPolicy(
                            SymlinkPolicy.Preserve,
                            "The bind mount preserves process-visible symlinks; model-facing file access denies them."),
                    ReadOnlyEnforcement =
                        new ReadOnlyEnforcementStatus(
                            ReadOnlyEnforcementPolicy.GuestEnforced,
                            Enforced:
                                spec.AccessMode ==
                                AccessMode.ReadOnly,
                            "The container engine bind mount applies the effective read-only flag."),
                    Conditions = [],
                    Limitations =
                    [
                        new ContentProjectionLimitation(
                            ContentProjectionDegradedFeature.SymlinkPolicy,
                            CapabilityDegradationMode.PartiallyAvailable,
                            "container-preserves-symlinks",
                            "The process view preserves symlinks while the workspace file store denies them."),
                    ],
                },
            ],
        };
        ProviderResourceEntry<
            ContentProjection,
            ContentProjectionSpec,
            ContentProjectionStatus> entry =
            state.Ledger.Upsert(metadata, spec, status, Shape);
        status = status with { ProviderHandle = entry.ProviderHandle };
        state.Ledger.Upsert(metadata, spec, status, Shape);
        return status;
    }

    public async ValueTask EnumerateEntriesAsync(
        ResourceRef<ContentProjection> projection,
        IContentProjectionEntrySink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ProviderResourceEntry<
            ContentProjection,
            ContentProjectionSpec,
            ContentProjectionStatus> entry = RequireResource(
                projection);
        if (entry.Spec.Source.Kind ==
            ContentSelectorKind.ProviderSource)
        {
            await EnumerateProviderEntriesAsync(
                entry,
                sink,
                cancellationToken).ConfigureAwait(false);
            return;
        }
        string source = Path.GetFullPath(
            entry.Spec.Source.HostPath!.Path.Value);
        string destination =
            entry.Spec.View.GuestPath!.Value.Value;
        foreach (string path in Enumerate(source, cancellationToken))
        {
            string relative = Path.GetRelativePath(source, path)
                .Replace(
                    Path.DirectorySeparatorChar,
                    '/');
            string guest = relative == "."
                ? destination
                : destination.TrimEnd('/') + "/" + relative;
            bool directory = Directory.Exists(path);
            Digest? digest = directory
                ? null
                : new Digest(
                    "sha256",
                    await HashAsync(path, cancellationToken)
                        .ConfigureAwait(false));
            await sink.OnEntryAsync(
                new ContentProjectionEntry(
                    directory
                        ? ContentProjectionEntryKind.Directory
                        : ContentProjectionEntryKind.File,
                    new GuestPath(guest),
                    directory
                        ? default
                        : new ByteSize(new FileInfo(path).Length),
                    digest,
                    File.GetLastWriteTimeUtc(path)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask<SyncResult> SyncAsync(
        TargetHandle<ContentProjection> projection,
        SyncRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderResourceEntry<
            ContentProjection,
            ContentProjectionSpec,
            ContentProjectionStatus> entry = Require(projection);
        if (entry.Spec.SyncPolicy.Mode != SyncMode.None ||
            request.OverrideMode is not null)
        {
            throw new NotSupportedException(
                "Host Containers live bind projections do not support synchronization.");
        }
        return ValueTask.FromResult(new SyncResult(
            new SyncCheckpoint(
                checked((long)projection.ProviderGeneration),
                DateTimeOffset.UtcNow)));
    }

    public ValueTask<FinalizationResult> FinalizeAsync(
        TargetHandle<ContentProjection> projection,
        FinalizationRequest request,
        IExecutionEventSink? events = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderResourceEntry<
            ContentProjection,
            ContentProjectionSpec,
            ContentProjectionStatus> entry = Require(projection);
        return ValueTask.FromResult(new FinalizationResult
        {
            CompletedAt = DateTimeOffset.UtcNow,
            ManifestDigest = new Digest(
                "sha256",
                entry.Spec.Source.Kind ==
                    ContentSelectorKind.ProviderSource
                    ? "host-containers-provider-workspace"
                    : "host-containers-live-bind"),
        });
    }

    public ValueTask ReleaseAsync(
        TargetHandle<ContentProjection> projection,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ContentProjection,
                ContentProjectionSpec,
                ContentProjectionStatus>> lookup = state.Ledger.TryGet<
                    ContentProjection,
                    ContentProjectionSpec,
                    ContentProjectionStatus>(projection);
        if (lookup.Succeeded)
        {
            state.ForgetProjectionUnit(
                lookup.Entry!.Resource.Id.Value);
            state.Ledger.Remove<
                ContentProjection,
                ContentProjectionSpec,
                ContentProjectionStatus>(
                    lookup.Entry!.Resource);
        }
        return ValueTask.CompletedTask;
    }

    private async ValueTask EnumerateProviderEntriesAsync(
        ProviderResourceEntry<
            ContentProjection,
            ContentProjectionSpec,
            ContentProjectionStatus> entry,
        IContentProjectionEntrySink sink,
        CancellationToken cancellationToken)
    {
        string projectionId = entry.Resource.Id.Value;
        TargetHandle<ExecutionUnit> unit =
            state.GetProjectionUnit(projectionId) ??
            throw new InvalidOperationException(
                "HostContainers.ProjectionUnitMissing: the projection lost its retained execution unit.");
        string destination =
            entry.Spec.View.GuestPath!.Value.Value;
        var rootId = new WorkspaceRootId(
            "projection-" + projectionId);
        await using var store = new ProcessWorkspaceFileStore(
            rootId,
            destination,
            entry.Spec.AccessMode,
            new WorkspacePathSemantics(
                true,
                SymlinkEvaluationPolicy.DenySymlinks),
            processProvider,
            unit,
            "HostContainers");
        var root = new WorkspacePath(rootId, string.Empty);
        WorkspaceListResultSet listing =
            await store.ListAsync(
                root,
                new WorkspaceListOptions
                {
                    Recursive = true,
                    MaxEntries = 100_000,
                    IncludeHidden = true,
                    RespectIgnoreFiles = false,
                },
                cancellationToken).ConfigureAwait(false);
        if (listing.Status is
            WorkspaceListStatus.Failed or
            WorkspaceListStatus.TimedOut or
            WorkspaceListStatus.Cancelled)
        {
            throw new IOException(
                listing.Message ??
                "Provider-owned projection enumeration did not complete.");
        }
        foreach (WorkspaceDirectoryEntry item in listing.Entries)
        {
            string guest = item.Path.RelativePath.Length == 0
                ? destination
                : destination.TrimEnd('/') + "/" +
                    item.Path.RelativePath;
            await sink.OnEntryAsync(
                new ContentProjectionEntry(
                    item.Kind == WorkspaceEntryKind.Directory
                        ? ContentProjectionEntryKind.Directory
                        : ContentProjectionEntryKind.File,
                    new GuestPath(guest),
                    item.Kind == WorkspaceEntryKind.Directory
                        ? default
                        : item.Version.Size,
                    item.Version.ContentDigest,
                    item.Version.ModifiedAt),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private bool TryResolveUnit(
        TargetHandle<ExecutionUnit> unit,
        out ProviderResourceEntry<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus>? entry)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> lookup = state.Ledger.TryGet<
                    ExecutionUnit,
                    ExecutionUnitSpec,
                    ExecutionUnitStatus>(unit);
        entry = lookup.Succeeded ? lookup.Entry : null;
        return entry is not null;
    }

    private ProviderResourceEntry<
        ContentProjection,
        ContentProjectionSpec,
        ContentProjectionStatus> RequireResource(
            ResourceRef<ContentProjection> projection)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ContentProjection,
                ContentProjectionSpec,
                ContentProjectionStatus>> lookup = state.Ledger.TryGet<
                    ContentProjection,
                    ContentProjectionSpec,
                    ContentProjectionStatus>(projection);
        return lookup.Succeeded
            ? lookup.Entry!
            : throw new InvalidOperationException(
                $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}");
    }

    private ProviderResourceEntry<
        ContentProjection,
        ContentProjectionSpec,
        ContentProjectionStatus> Require(
            TargetHandle<ContentProjection> projection)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ContentProjection,
                ContentProjectionSpec,
                ContentProjectionStatus>> lookup = state.Ledger.TryGet<
                    ContentProjection,
                    ContentProjectionSpec,
                    ContentProjectionStatus>(projection);
        return lookup.Succeeded
            ? lookup.Entry!
            : throw new InvalidOperationException(
                $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}");
    }

    private static IEnumerable<string> Enumerate(
        string root,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            foreach (string entry in
                     Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes =
                    File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UnauthorizedAccessException(
                        $"Projected symlink '{entry}' is denied by policy.");
                }
                yield return entry;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static async ValueTask<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            useAsync: true);
        byte[] digest = await SHA256.HashDataAsync(
            stream,
            cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private ContentProjectionStatus Failed(
        ResourceMetadata<ContentProjection> metadata,
        string code,
        string message) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            ObservedGeneration = metadata.Generation,
            ProjectionPhase = ContentProjectionPhase.Failed,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Rejected,
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
}
