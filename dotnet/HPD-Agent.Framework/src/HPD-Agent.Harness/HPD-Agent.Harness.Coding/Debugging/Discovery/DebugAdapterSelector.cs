namespace HPD.Agent.ToolHarness.Coding.Debugging;

public enum DebugAdapterSelectionKind
{
    Available,
    Unavailable,
    NoMatch,
    Ambiguous
}

public enum DebugAdapterSelectionOperation
{
    Launch,
    Attach
}

public sealed record DebugAdapterSelectionPolicy
{
    public IReadOnlySet<string> EnabledAdapters { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> DisabledAdapters { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> EnabledExperimentalAdapters { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public sealed record DebugAdapterSelectionContext
{
    public DebugAdapterSelectionOperation Operation { get; init; }
    public string? ExplicitAdapterId { get; init; }
    public string? Language { get; init; }
    public string? RuntimeLanguageHint { get; init; }
    public string? FileExtension { get; init; }
    public required DebugTargetKind TargetKind { get; init; }
    public IReadOnlySet<string> MatchedRootMarkers { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public required string ProjectMarkerFingerprint { get; init; }
    public required DebugAdapterResolutionContext Resolution { get; init; }
    public DebugAdapterSelectionPolicy Policy { get; init; } = new();
    public int MaxReportedCandidates { get; init; } = 8;
}

public sealed record DebugAdapterSelectionCandidate(
    string AdapterId,
    DebugAdapterAvailability Availability,
    int Score);

public sealed record DebugAdapterSelectionResult
{
    public required DebugAdapterSelectionKind Kind { get; init; }
    public DebugAdapterCatalogEntry? Entry { get; init; }
    public IDebugAdapterFactory? Factory { get; init; }
    public IReadOnlyList<DebugAdapterSelectionCandidate> Candidates { get; init; } = [];
}

public sealed class DebugAdapterSelector(
    DebugAdapterCatalog catalog,
    IDebugAdapterAvailabilityCache availabilityCache,
    IDebugAdapterTrustPolicy trustPolicy,
    IDebugWorkspaceCanonicalizer workspaceCanonicalizer)
{
    public async ValueTask<DebugAdapterSelectionResult> SelectAsync(
        DebugAdapterSelectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var explicitSelection = !string.IsNullOrWhiteSpace(context.ExplicitAdapterId);
        var canonicalWorkspace = workspaceCanonicalizer.Canonicalize(
            context.Resolution.WorkspaceRoot,
            context.Resolution.TargetPlatform);
        DebugAdapterCatalogEntry[] entries = explicitSelection
            ? catalog.TryGet(context.ExplicitAdapterId!, out var explicitEntry) && IsMetadataMatch(explicitEntry.Descriptor, context) ? [explicitEntry] : []
            : catalog.Entries.Where(entry => IsMetadataMatch(entry.Descriptor, context)).ToArray();
        if (entries.Length == 0)
            return new() { Kind = DebugAdapterSelectionKind.NoMatch };

        var candidates = new List<(DebugAdapterCatalogEntry Entry, IDebugAdapterFactory Factory, DebugAdapterAvailability Availability, int Score)>();
        foreach (var entry in entries)
        {
            if (!IsEnabled(entry.Descriptor, context.Policy, explicitSelection))
                continue;
            var factory = catalog.GetFactory(entry.Descriptor.Id);
            var trustDecision = trustPolicy.Evaluate(entry.Descriptor);
            var effectiveResolution = context.Resolution with { TrustDecision = trustDecision };
            if (trustDecision.TrustLevel != DebugAdapterTrustLevel.Trusted)
            {
                candidates.Add((
                    entry,
                    factory,
                    new DebugAdapterAvailability(
                        DebugAdapterAvailabilityKind.Unavailable,
                        SafeReasonCode: trustDecision.ReasonCode,
                        InstallGuidanceId: entry.Descriptor.InstallGuidanceId),
                    Score(entry.Descriptor, context)));
                continue;
            }
            var key = new DebugAdapterAvailabilityCacheKey(
                entry.Descriptor.Id,
                entry.Descriptor.Provenance.PackageId,
                context.Resolution.EnvironmentId,
                context.Resolution.EnvironmentRevision,
                context.Resolution.TargetPlatform,
                canonicalWorkspace,
                context.ProjectMarkerFingerprint,
                effectiveResolution.PolicyRevision,
                trustDecision.PolicyRevision,
                context.Resolution.EndpointCatalogRevision);
            var availability = await availabilityCache.GetOrProbeAsync(
                key,
                token => factory.ProbeAsync(entry.Descriptor, effectiveResolution, token),
                cancellationToken).ConfigureAwait(false);
            candidates.Add((entry, factory, availability, Score(entry.Descriptor, context)));
        }
        if (candidates.Count == 0)
            return new() { Kind = DebugAdapterSelectionKind.NoMatch };

        var ordered = candidates.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Entry.Descriptor.Id, StringComparer.Ordinal).ToArray();
        var available = ordered.Where(candidate => candidate.Availability.Kind == DebugAdapterAvailabilityKind.Available).ToArray();
        if (available.Length == 0)
            return Result(DebugAdapterSelectionKind.Unavailable, ordered, context.MaxReportedCandidates);
        if (explicitSelection)
            return Result(DebugAdapterSelectionKind.Available, available, context.MaxReportedCandidates, available[0]);

        var topScore = available[0].Score;
        var top = available.Where(candidate => candidate.Score == topScore).ToArray();
        return top.Length == 1
            ? Result(DebugAdapterSelectionKind.Available, ordered, context.MaxReportedCandidates, top[0])
            : Result(DebugAdapterSelectionKind.Ambiguous, top, context.MaxReportedCandidates);
    }

    private static bool IsMetadataMatch(DebugAdapterDescriptor descriptor, DebugAdapterSelectionContext context)
    {
        const DebugTargetKind attachKinds = DebugTargetKind.Process | DebugTargetKind.RegisteredRemoteEndpoint;
        if (context.Operation == DebugAdapterSelectionOperation.Attach && (context.TargetKind & attachKinds) == 0)
            return false;
        if (context.Operation == DebugAdapterSelectionOperation.Launch && (context.TargetKind & ~attachKinds) == 0)
            return false;
        if ((descriptor.TargetKinds & context.TargetKind) == 0)
            return false;
        if (context.Operation == DebugAdapterSelectionOperation.Attach &&
            context.RuntimeLanguageHint is not null &&
            !descriptor.Languages.Contains(context.RuntimeLanguageHint, StringComparer.OrdinalIgnoreCase))
            return false;
        var hasClassifier = !string.IsNullOrWhiteSpace(context.Language) || !string.IsNullOrWhiteSpace(context.FileExtension);
        if (!hasClassifier)
            return true;
        return context.Language is not null && descriptor.Languages.Contains(context.Language, StringComparer.OrdinalIgnoreCase) ||
            context.FileExtension is not null && descriptor.FileExtensions.Contains(context.FileExtension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsEnabled(DebugAdapterDescriptor descriptor, DebugAdapterSelectionPolicy policy, bool explicitSelection)
    {
        if (policy.DisabledAdapters.Contains(descriptor.Id))
            return false;
        var enabled = descriptor.EnabledByDefault || policy.EnabledAdapters.Contains(descriptor.Id) || explicitSelection;
        return enabled && (!descriptor.Experimental || policy.EnabledExperimentalAdapters.Contains(descriptor.Id) || explicitSelection);
    }

    private static int Score(DebugAdapterDescriptor descriptor, DebugAdapterSelectionContext context)
    {
        var score = descriptor.Priority * 100;
        if (context.Language is not null && descriptor.Languages.Contains(context.Language, StringComparer.OrdinalIgnoreCase)) score += 20;
        if (context.RuntimeLanguageHint is not null && descriptor.Languages.Contains(context.RuntimeLanguageHint, StringComparer.OrdinalIgnoreCase)) score += 15;
        if (context.FileExtension is not null && descriptor.FileExtensions.Contains(context.FileExtension, StringComparer.OrdinalIgnoreCase)) score += 10;
        score += descriptor.RootMarkers.Count(context.MatchedRootMarkers.Contains);
        return score;
    }

    private static DebugAdapterSelectionResult Result(
        DebugAdapterSelectionKind kind,
        IReadOnlyList<(DebugAdapterCatalogEntry Entry, IDebugAdapterFactory Factory, DebugAdapterAvailability Availability, int Score)> candidates,
        int limit,
        (DebugAdapterCatalogEntry Entry, IDebugAdapterFactory Factory, DebugAdapterAvailability Availability, int Score)? selected = null) => new()
    {
        Kind = kind,
        Entry = selected?.Entry,
        Factory = selected?.Factory,
        Candidates = candidates.Take(Math.Clamp(limit, 1, 32))
            .Select(candidate => new DebugAdapterSelectionCandidate(candidate.Entry.Descriptor.Id, candidate.Availability, candidate.Score)).ToArray()
    };
}
