namespace HPD.Agent.Audio.Endpointing;

internal enum TranscriptMutability : ushort
{
    Volatile = 1,
    StablePrefix = 2,
    ImmutableUnderSourceGuarantee = 3,
    Unknown = 4,
}

internal enum TranscriptFinalizedScope : ushort
{
    None = 1,
    Token = 2,
    Range = 3,
    Segment = 4,
    ProviderItem = 5,
    ProviderTurn = 6,
    Utterance = 7,
    FiniteRequest = 8,
    Stream = 9,
}

[Flags]
internal enum TranscriptBoundaryEvidence : ushort
{
    None = 0,
    ActivityClose = 1 << 0,
    ProviderPause = 1 << 1,
    ProviderEndpoint = 1 << 2,
    ProviderTurnEnd = 1 << 3,
    ManualFlush = 1 << 4,
    SourceEnd = 1 << 5,
    Discontinuity = 1 << 6,
}

internal enum TranscriptContinuity : ushort
{
    Complete = 1,
    Gapped = 2,
    Overlapped = 3,
    Discontinuous = 4,
    Unknown = 5,
}

internal enum TranscriptObservability : ushort
{
    Observed = 1,
    DerivedByDeclaredAdapterRule = 2,
    NotObservable = 3,
    Lost = 4,
    Opaque = 5,
}

internal enum TranscriptCorrectionState : ushort
{
    Correctable = 1,
    CorrectionWindowClosed = 2,
    Retracted = 3,
    Contradicted = 4,
}

internal enum TranscriptAssemblyClosure : ushort
{
    Open = 1,
    ClosedForAssembly = 2,
    Retired = 3,
}

internal readonly record struct TranscriptTextRange
{
    internal TranscriptTextRange(uint startUtf8Byte, uint lengthUtf8Bytes)
    {
        if (lengthUtf8Bytes == 0)
            throw new ArgumentOutOfRangeException(nameof(lengthUtf8Bytes));
        _ = checked(startUtf8Byte + lengthUtf8Bytes);
        StartUtf8Byte = startUtf8Byte;
        LengthUtf8Bytes = lengthUtf8Bytes;
    }

    internal uint StartUtf8Byte { get; }
    internal uint LengthUtf8Bytes { get; }
    internal uint EndUtf8ByteExclusive => checked(StartUtf8Byte + LengthUtf8Bytes);
}

internal sealed record TranscriptFinality
{
    private const TranscriptBoundaryEvidence AllBoundaryEvidence =
        TranscriptBoundaryEvidence.ActivityClose |
        TranscriptBoundaryEvidence.ProviderPause |
        TranscriptBoundaryEvidence.ProviderEndpoint |
        TranscriptBoundaryEvidence.ProviderTurnEnd |
        TranscriptBoundaryEvidence.ManualFlush |
        TranscriptBoundaryEvidence.SourceEnd |
        TranscriptBoundaryEvidence.Discontinuity;

    internal TranscriptFinality(
        TranscriptMutability mutability,
        TranscriptTextRange? stablePrefix,
        TranscriptFinalizedScope finalizedScope,
        TranscriptBoundaryEvidence boundaryEvidence,
        TranscriptContinuity continuity,
        TranscriptObservability observability,
        TranscriptCorrectionState correctionState,
        TranscriptAssemblyClosure assemblyClosure)
    {
        if (!Enum.IsDefined(mutability) || !Enum.IsDefined(finalizedScope) ||
            !Enum.IsDefined(continuity) || !Enum.IsDefined(observability) ||
            !Enum.IsDefined(correctionState) || !Enum.IsDefined(assemblyClosure) ||
            (boundaryEvidence & ~AllBoundaryEvidence) != 0)
            throw new ArgumentException("Transcript finality contains an unknown closed value.");
        if ((mutability == TranscriptMutability.StablePrefix) != stablePrefix.HasValue)
            throw new ArgumentException("Stable-prefix mutability requires exactly one non-empty UTF-8 byte range.", nameof(stablePrefix));
        if (correctionState == TranscriptCorrectionState.Retracted &&
            assemblyClosure == TranscriptAssemblyClosure.Open)
            throw new ArgumentException("A retracted transcript cannot remain open for assembly.");

        Mutability = mutability;
        StablePrefix = stablePrefix;
        FinalizedScope = finalizedScope;
        BoundaryEvidence = boundaryEvidence;
        Continuity = continuity;
        Observability = observability;
        CorrectionState = correctionState;
        AssemblyClosure = assemblyClosure;
    }

    internal TranscriptMutability Mutability { get; }
    internal TranscriptTextRange? StablePrefix { get; }
    internal TranscriptFinalizedScope FinalizedScope { get; }
    internal TranscriptBoundaryEvidence BoundaryEvidence { get; }
    internal TranscriptContinuity Continuity { get; }
    internal TranscriptObservability Observability { get; }
    internal TranscriptCorrectionState CorrectionState { get; }
    internal TranscriptAssemblyClosure AssemblyClosure { get; }

    internal bool IsProviderFinal =>
        Mutability == TranscriptMutability.ImmutableUnderSourceGuarantee &&
        FinalizedScope != TranscriptFinalizedScope.None;
}
