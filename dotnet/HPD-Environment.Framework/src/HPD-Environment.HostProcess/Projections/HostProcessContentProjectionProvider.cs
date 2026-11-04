namespace HPD.Environment.HostProcess;

using System.Security.Cryptography;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

/// <summary>
/// Realizes content projections that are already visible at a host path.
/// Host Process cannot remap a destination path, so such requests fail rather
/// than reporting a projection the process cannot observe.
/// </summary>
internal sealed class HostProcessContentProjectionProvider(
    HostProcessProviderState state)
    : IContentProjectionProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("content-projection"),
        TargetRouteSegmentKind.ContentProjection,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control |
        TargetHandleAuthority.Read,
        new SchemaId("hpd.execution.host-process.content-projection.handle.v1"));

    public ProviderId ProviderId =>
        HostProcessProviderDescriptor.ProviderId;

    public ValueTask<ContentProjectionStatus> ProjectAsync(
        ResourceMetadata<ContentProjection> metadata,
        ContentProjectionSpec spec,
        TargetHandle<RuntimeHost>? host,
        TargetHandle<ExecutionUnit>? unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? failure = Validate(
            spec,
            host,
            unit,
            cancellationToken);
        if (failure is not null)
        {
            return ValueTask.FromResult(Failed(
                metadata,
                "hpd.environment.host-process.projection-rejected",
                failure));
        }

        HostPath source = spec.Source.HostPath!.Path;
        GuestPath effectivePath = new(source.Value);
        bool writable =
            spec.AccessMode is not AccessMode.ReadOnly;
        ProviderResourceEntry<
            ContentProjection,
            ContentProjectionSpec,
            ContentProjectionStatus> entry =
            state.Ledger.Upsert(
                metadata,
                spec,
                new ContentProjectionStatus
                {
                    Phase = ResourcePhase.Ready,
                    ObservedGeneration = metadata.Generation,
                    ProjectionPhase = ContentProjectionPhase.Projected,
                    LastTransitionAt = DateTimeOffset.UtcNow,
                    Views =
                    [
                        new RealizedProjectionView
                        {
                            Kind = spec.View.Kind,
                            GuestPath = effectivePath,
                            EffectiveAccess = spec.AccessMode,
                            EffectiveRealization =
                                ProjectionRealizationKind.LiveProjection,
                            EffectiveWriteEffect = writable
                                ? ProjectionWriteEffect.DirectSourceMutation
                                : ProjectionWriteEffect.NoWrites,
                            EffectiveCoherence = CoherenceClass.Strong,
                            EffectiveCache = CacheBehavior.None,
                            ReadOnlyEnforcement =
                                new ReadOnlyEnforcementStatus(
                                    ReadOnlyEnforcementPolicy.BestEffort,
                                    Enforced: false,
                                    "Read-only process visibility is enforced by the compiled Host Process policy; " +
                                    "the projection provider does not change host permissions."),
                            Limitations =
                            [
                                new ContentProjectionLimitation(
                                    ContentProjectionDegradedFeature.HostPath,
                                    CapabilityDegradationMode.PartiallyAvailable,
                                    "host-path-not-remapped",
                                    "Host Process content is visible at its source path."),
                            ],
                        },
                    ],
                },
                Shape);
        ContentProjectionStatus status = entry.Status with
        {
            ProviderHandle = entry.ProviderHandle,
        };
        state.Ledger.Upsert(metadata, spec, status, Shape);
        return ValueTask.FromResult(status);
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
            ContentProjectionStatus> entry =
            RequireResource(projection);
        string source = entry.Spec.Source.HostPath!.Path.Value;
        if (File.Exists(source))
        {
            await sink.OnEntryAsync(
                await EntryAsync(
                    new GuestPath(source),
                    source,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFileSystemEntries(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await sink.OnEntryAsync(
                await EntryAsync(
                    new GuestPath(path),
                    path,
                    cancellationToken),
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
                "Host Process live projections do not require or support provider synchronization.");
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
        _ = Require(projection);
        return ValueTask.FromResult(new FinalizationResult
        {
            CompletedAt = DateTimeOffset.UtcNow,
            ManifestDigest = new Digest(
                "sha256",
                "host-process-live-projection"),
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
            state.Ledger.Remove<
                ContentProjection,
                ContentProjectionSpec,
                ContentProjectionStatus>(
                    lookup.Entry!.Resource);
        }

        return ValueTask.CompletedTask;
    }

    private string? Validate(
        ContentProjectionSpec spec,
        TargetHandle<RuntimeHost>? host,
        TargetHandle<ExecutionUnit>? unit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!spec.SecurityPolicy.AllowHostPathSource ||
            spec.Source.Kind != ContentSelectorKind.HostPath ||
            spec.Source.HostPath is null)
        {
            return "Host Process projections require an explicitly authorized HostPath source.";
        }

        HostPathSelection source = spec.Source.HostPath;
        if (source.RequireExists && !Exists(source))
        {
            return $"Projection source '{source.Path.Value}' does not exist.";
        }

        if (spec.AccessMode is not AccessMode.ReadOnly &&
            !spec.SecurityPolicy.AllowDirectSourceMutation)
        {
            return "Writable Host Process projections require explicit direct-source mutation authorization.";
        }

        if (spec.AccessMode is AccessMode.ReadOnly &&
            spec.SecurityPolicy.ReadOnlyEnforcement is
                ReadOnlyEnforcementPolicy.Required)
        {
            return "Host Process cannot prove projection-level read-only enforcement; " +
                "the caller must explicitly accept best-effort enforcement together with the compiled process policy.";
        }

        if (spec.View.GuestPath is { } requestedPath &&
            !string.Equals(
                Path.GetFullPath(requestedPath.Value),
                Path.GetFullPath(source.Path.Value),
                StringComparison.Ordinal))
        {
            return "Host Process cannot remap projected content to a different destination path.";
        }

        if (host is null)
        {
            return "Host Process projections require a runtime host handle.";
        }

        ProviderLedgerLookup<
            ProviderResourceEntry<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>> hostLookup = state.Ledger.TryGet<
                    RuntimeHost,
                    RuntimeHostSpec,
                    RuntimeHostStatus>(host.Value);
        if (!hostLookup.Succeeded)
        {
            return hostLookup.Diagnostic?.Message ??
                "The projection host is not owned by this provider.";
        }

        if (unit is not null &&
            !HostProcessExecutionUnitProvider.Lookup(state, unit.Value).Succeeded)
        {
            return "The projection execution unit is not owned by this provider.";
        }

        return null;
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

    private static bool Exists(HostPathSelection source) =>
        source.Kind switch
        {
            HostPathKind.File => File.Exists(source.Path.Value),
            HostPathKind.Directory => Directory.Exists(source.Path.Value),
            HostPathKind.Socket => File.Exists(source.Path.Value),
            _ => File.Exists(source.Path.Value) ||
                 Directory.Exists(source.Path.Value),
        };

    private static async ValueTask<ContentProjectionEntry> EntryAsync(
        GuestPath projectedPath,
        string hostPath,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(hostPath))
        {
            return new ContentProjectionEntry(
                ContentProjectionEntryKind.Directory,
                projectedPath,
                default,
                null,
                Directory.GetLastWriteTimeUtc(hostPath));
        }

        await using FileStream stream = new(
            hostPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        byte[] digest = await SHA256.HashDataAsync(
            stream,
            cancellationToken).ConfigureAwait(false);
        return new ContentProjectionEntry(
            ContentProjectionEntryKind.File,
            projectedPath,
            new ByteSize(stream.Length),
            new Digest(
                "sha256",
                Convert.ToHexString(digest).ToLowerInvariant()),
            File.GetLastWriteTimeUtc(hostPath));
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
