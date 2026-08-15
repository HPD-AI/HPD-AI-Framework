namespace HPD.Payments.Tools.Conformance;

/// <summary>Names every ordered H0-H13 semantic-history stage.</summary>
internal enum HistoryStage
{
    Scope = 0, Identity, Time, Preconditions, Admission, Decision, Operation, Boundary,
    Evidence, Economics, Durability, Correction, Repair, End,
}

/// <summary>Names the 24 frozen Level 4 H7 boundary classes.</summary>
internal enum FaultBoundary
{
    CompareBind = 0, OwnerAppend, RelationAppend, ContinuationAppend, Claim, Renew, Takeover, Enqueue,
    Dequeue, FirstAwait, PluginInvocation, PluginReturn, IpcWrite, IpcRead, SendLedgerPrepare,
    FirstPossibleExternalByte, ProviderAcknowledgement, Observation, ResultCommit,
    PublicationMaterialization, PublicationSend, AudienceAcknowledgement, ProjectionUpdate, CustodyUpdate,
}

/// <summary>Places a fault immediately before or after an exact named boundary.</summary>
internal enum FaultSide { Before = 0, After = 1 }

/// <summary>Identifies one exact H7 injection coordinate.</summary>
internal readonly record struct FaultCoordinate(FaultBoundary Boundary, FaultSide Side)
{
    /// <summary>Returns the stable coordinate token.</summary>
    internal string ToCanonicalText() => $"H7:{Boundary}:{Side}";
}

/// <summary>Owns an ordered, duplicate-free exact H7 fault schedule.</summary>
internal sealed class FaultSchedule
{
    private readonly FaultCoordinate[] _coordinates;
    /// <summary>Gets a read-only view over owned coordinates.</summary>
    internal IReadOnlyList<FaultCoordinate> Coordinates => Array.AsReadOnly(_coordinates);

    /// <summary>Creates a bounded ordered schedule and rejects duplicate or unknown coordinates.</summary>
    internal FaultSchedule(IEnumerable<FaultCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        _coordinates = coordinates.ToArray();
        if (_coordinates.Length is < 1 or > 48 || _coordinates.Any(static x => !Enum.IsDefined(x.Boundary) || !Enum.IsDefined(x.Side)) ||
            _coordinates.Distinct().Count() != _coordinates.Length)
            throw new ArgumentException("Fault schedules must be non-empty, bounded, known, and duplicate-free.", nameof(coordinates));
    }

    /// <summary>Creates the exact 48-coordinate before/after schedule for all frozen boundaries.</summary>
    internal static FaultSchedule Complete() => new(Enum.GetValues<FaultBoundary>()
        .SelectMany(static boundary => Enum.GetValues<FaultSide>().Select(side => new FaultCoordinate(boundary, side))));

    /// <summary>Returns the stable ordered schedule text retained by a proof receipt.</summary>
    internal string ToCanonicalText() => string.Join('|', _coordinates.Select(static x => x.ToCanonicalText()));
}
