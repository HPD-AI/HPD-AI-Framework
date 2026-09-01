using HPD.Payments.Contracts.EntitlementGrantRemovalFact;
using HPD.Payments.Contracts.RestrictionFact;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Runtime.Entitlement;

/// <summary>Closed result of resolving entitlement and independently owned restriction histories.</summary>
public enum EnforcementDecisionKind
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>Current evidence permits the action.</summary>
    Allow,
    /// <summary>Current evidence denies the action.</summary>
    Deny,
    /// <summary>Available evidence cannot decide the action.</summary>
    Indeterminate,
}

/// <summary>Declares outage behavior for an action-specific enforcement query.</summary>
public enum EnforcementFailMode
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>Stale evidence never produces permission.</summary>
    Closed,
    /// <summary>Stale evidence permits this explicitly classified action.</summary>
    Open,
}

/// <summary>Immutable decision evidence; it is not proof that an external consumer enforced the result.</summary>
public sealed record EnforcementDecision(EnforcementDecisionKind Kind, OwnerGeneration Generation,
    DateTimeOffset EffectiveAt, DateTimeOffset ObservedAt, string Reason);

/// <summary>One append-only entitlement or restriction entry retained for temporal reconstruction.</summary>
public sealed record EntitlementRestrictionEntry(string Kind, SemanticId FactId, SemanticId SubjectId, string Dimension,
    SemanticId OwnerId, SemanticId? PredecessorFactId, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    DateTimeOffset RecordedAt, OwnerGeneration Generation);

/// <summary>Generation-fenced combined query projection which preserves separate entitlement and restriction authorities.</summary>
public sealed record EntitlementRestrictionState
{
    /// <summary>Gets the continuing subject.</summary>
    public SemanticId SubjectId { get; }
    /// <summary>Gets the exact append generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the complete immutable fact history.</summary>
    public IReadOnlyList<EntitlementRestrictionEntry> History { get; }

    private EntitlementRestrictionState(SemanticId subjectId, OwnerGeneration generation, IReadOnlyList<EntitlementRestrictionEntry> history)
    {
        if (!subjectId.IsValid || !generation.IsValid || history is null || history.Any(x => x.SubjectId != subjectId))
            throw new ArgumentException("Entitlement/restriction projection is invalid.");
        SubjectId = subjectId; Generation = generation; History = history.ToArray();
    }

    /// <summary>Creates an empty subject projection at a named valid generation.</summary>
    public static EntitlementRestrictionState Create(SemanticId subjectId, OwnerGeneration generation) => new(subjectId, generation, []);

    /// <summary>Rehydrates a previously admitted projection.</summary>
    public static EntitlementRestrictionState Restore(SemanticId subjectId, OwnerGeneration generation,
        IReadOnlyList<EntitlementRestrictionEntry> history) => new(subjectId, generation, history);

    /// <summary>Appends one guarded entitlement fact without collapsing requested, effective, record, and observation time.</summary>
    public EntitlementRestrictionState Apply(EntitlementCommand command, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireGuard(command.SubjectId, command.ExpectedGeneration, command.FactId, command.PredecessorFactId);
        var owner = command.ProvenanceId;
        return Append(new(command.Operation.ToString(), command.FactId, command.SubjectId, command.Feature, owner,
            command.PredecessorFactId, command.EffectiveFrom.Value, command.EffectiveTo?.Value, recordedAt, Next()));
    }

    /// <summary>Appends one guarded restriction fact; only its original owner can release or supersede it.</summary>
    public EntitlementRestrictionState Apply(RestrictionCommand command, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireGuard(command.SubjectId, command.ExpectedGeneration, command.FactId, command.PredecessorFactId);
        if (command.PredecessorFactId is { } predecessor)
        {
            var prior = History.Single(x => x.FactId == predecessor);
            if (prior.OwnerId != command.RestrictionOwnerId) throw new InvalidOperationException("Restriction release owner does not own predecessor.");
        }
        return Append(new(command.Operation.ToString(), command.FactId, command.SubjectId, command.Dimension,
            command.RestrictionOwnerId, command.PredecessorFactId, command.EffectiveFrom.Value,
            command.EffectiveTo?.Value, recordedAt, Next()));
    }

    /// <summary>Resolves at explicit effective and observation coordinates with bounded freshness and fail behavior.</summary>
    public EnforcementDecision Resolve(string feature, string restrictionDimension, DateTimeOffset effectiveAt,
        DateTimeOffset observedAt, TimeSpan maximumStaleness, EnforcementFailMode failMode)
    {
        if (string.IsNullOrWhiteSpace(feature) || string.IsNullOrWhiteSpace(restrictionDimension) || observedAt < effectiveAt ||
            maximumStaleness < TimeSpan.Zero || failMode is EnforcementFailMode.None || !Enum.IsDefined(failMode))
            throw new ArgumentException("Invalid enforcement query.");
        var visible = History.Where(x => x.RecordedAt <= observedAt).ToArray();
        if (visible.Length == 0 || observedAt - visible.Max(x => x.RecordedAt) > maximumStaleness)
            return new(failMode == EnforcementFailMode.Open ? EnforcementDecisionKind.Allow : EnforcementDecisionKind.Indeterminate,
                Generation, effectiveAt, observedAt, "evidence-stale");
        var restricted = Active(visible, restrictionDimension, effectiveAt, "Restrict", "Release", "Supersede");
        if (restricted) return new(EnforcementDecisionKind.Deny, Generation, effectiveAt, observedAt, "restriction-active");
        var granted = Active(visible, feature, effectiveAt, "Grant", "Remove", "Correct", "Override");
        return new(granted ? EnforcementDecisionKind.Allow : EnforcementDecisionKind.Deny, Generation, effectiveAt, observedAt,
            granted ? "entitlement-active" : "entitlement-absent");
    }

    private static bool Active(EntitlementRestrictionEntry[] visible, string dimension, DateTimeOffset at,
        string activatingKind, params string[] replacingKinds)
    {
        var applicable = visible.Where(x => x.Dimension == dimension && x.EffectiveFrom <= at && (x.EffectiveTo is null || at < x.EffectiveTo)).ToArray();
        var replaced = applicable.Where(x => replacingKinds.Contains(x.Kind, StringComparer.Ordinal) && x.PredecessorFactId is not null)
            .Select(x => x.PredecessorFactId!.Value).ToHashSet();
        return applicable.Any(x => x.Kind == activatingKind && !replaced.Contains(x.FactId)) ||
            applicable.Any(x => (x.Kind == "Override" || x.Kind == "Correct" || x.Kind == "Supersede") && !replaced.Contains(x.FactId));
    }

    private void RequireGuard(SemanticId subject, OwnerGeneration expected, SemanticId factId, SemanticId? predecessor)
    {
        if (subject != SubjectId || expected != Generation || History.Any(x => x.FactId == factId) ||
            (predecessor is { } prior && History.All(x => x.FactId != prior)))
            throw new InvalidOperationException("Stale generation, duplicate identity, or missing predecessor.");
    }

    private OwnerGeneration Next() => Generation.TryNext(out var next) ? next : throw new InvalidOperationException("Generation exhausted.");
    private EntitlementRestrictionState Append(EntitlementRestrictionEntry entry) => new(SubjectId, entry.Generation, [.. History, entry]);
}
