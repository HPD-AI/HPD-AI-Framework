namespace HPD.Environment.Contracts;

/// <summary>
/// Owns the common lifecycle and routing surface for content projections.
/// Runtime implementations coordinate identity, generation checks, provider
/// selection, dependency ordering, and cleanup.
/// </summary>
public interface IContentProjectionRuntime
{
    /// <summary>Creates or reconciles a projection and returns its observed state.</summary>
    ValueTask<ResourceSnapshot<ContentProjection,
        ContentProjectionSpec, ContentProjectionStatus>> EnsureAsync(
            ContentProjectionSpec spec,
            CancellationToken cancellationToken = default);

    /// <summary>Gets a projection after verifying its scope and generation.</summary>
    ValueTask<ResourceSnapshot<ContentProjection,
        ContentProjectionSpec, ContentProjectionStatus>> GetAsync(
            ResourceRef<ContentProjection> projection,
            CancellationToken cancellationToken = default);

    /// <summary>Enumerates entries visible through the realized projection.</summary>
    ValueTask EnumerateAsync(
        ResourceRef<ContentProjection> projection,
        IContentProjectionEntrySink sink,
        CancellationToken cancellationToken = default);

    /// <summary>Requests a provider-supported synchronization operation.</summary>
    ValueTask<SyncResult> SyncAsync(
        ResourceRef<ContentProjection> projection,
        SyncRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Finalizes changes according to the projection's policy.</summary>
    ValueTask<FinalizationResult> FinalizeAsync(
        ResourceRef<ContentProjection> projection,
        FinalizationRequest request,
        IExecutionEventSink? events = null,
        CancellationToken cancellationToken = default);

    /// <summary>Releases the projection and its provider-owned dependencies.</summary>
    ValueTask ReleaseAsync(
        ResourceRef<ContentProjection> projection,
        CancellationToken cancellationToken = default);
}
