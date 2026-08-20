namespace HPD.Agent.Audio.Endpointing;

internal enum TranscriptMutabilityV1 : ushort
{
    Volatile = 1,
    StablePrefix = 2,
    ImmutableUnderSourceGuarantee = 3,
    Unknown = 4,
}

internal enum TranscriptFinalizedScopeV1 : ushort
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
internal enum TranscriptBoundaryEvidenceV1 : ushort
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

internal enum TranscriptContinuityV1 : ushort
{
    Complete = 1,
    Gapped = 2,
    Overlapped = 3,
    Discontinuous = 4,
    Unknown = 5,
}

internal enum TranscriptObservabilityV1 : ushort
{
    Observed = 1,
    DerivedByDeclaredAdapterRule = 2,
    NotObservable = 3,
    Lost = 4,
    Opaque = 5,
}

internal enum TranscriptCorrectionStateV1 : ushort
{
    Correctable = 1,
    CorrectionWindowClosed = 2,
    Retracted = 3,
    Contradicted = 4,
}

internal enum TranscriptAssemblyClosureV1 : ushort
{
    Open = 1,
    ClosedForAssembly = 2,
    Retired = 3,
}

internal readonly record struct TranscriptTextRangeV1
{
    internal TranscriptTextRangeV1(uint startUtf8Byte, uint lengthUtf8Bytes)
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

internal sealed record TranscriptFinalityV1
{
    private const TranscriptBoundaryEvidenceV1 AllBoundaryEvidence =
        TranscriptBoundaryEvidenceV1.ActivityClose |
        TranscriptBoundaryEvidenceV1.ProviderPause |
        TranscriptBoundaryEvidenceV1.ProviderEndpoint |
        TranscriptBoundaryEvidenceV1.ProviderTurnEnd |
        TranscriptBoundaryEvidenceV1.ManualFlush |
        TranscriptBoundaryEvidenceV1.SourceEnd |
        TranscriptBoundaryEvidenceV1.Discontinuity;

    internal TranscriptFinalityV1(
        TranscriptMutabilityV1 mutability,
        TranscriptTextRangeV1? stablePrefix,
        TranscriptFinalizedScopeV1 finalizedScope,
        TranscriptBoundaryEvidenceV1 boundaryEvidence,
        TranscriptContinuityV1 continuity,
        TranscriptObservabilityV1 observability,
        TranscriptCorrectionStateV1 correctionState,
        TranscriptAssemblyClosureV1 assemblyClosure)
    {
        if (!Enum.IsDefined(mutability) || !Enum.IsDefined(finalizedScope) ||
            !Enum.IsDefined(continuity) || !Enum.IsDefined(observability) ||
            !Enum.IsDefined(correctionState) || !Enum.IsDefined(assemblyClosure) ||
            (boundaryEvidence & ~AllBoundaryEvidence) != 0)
            throw new ArgumentException("Transcript finality contains an unknown closed value.");
        if ((mutability == TranscriptMutabilityV1.StablePrefix) != stablePrefix.HasValue)
            throw new ArgumentException("Stable-prefix mutability requires exactly one non-empty UTF-8 byte range.", nameof(stablePrefix));
        if (correctionState == TranscriptCorrectionStateV1.Retracted &&
            assemblyClosure == TranscriptAssemblyClosureV1.Open)
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

    internal TranscriptMutabilityV1 Mutability { get; }
    internal TranscriptTextRangeV1? StablePrefix { get; }
    internal TranscriptFinalizedScopeV1 FinalizedScope { get; }
    internal TranscriptBoundaryEvidenceV1 BoundaryEvidence { get; }
    internal TranscriptContinuityV1 Continuity { get; }
    internal TranscriptObservabilityV1 Observability { get; }
    internal TranscriptCorrectionStateV1 CorrectionState { get; }
    internal TranscriptAssemblyClosureV1 AssemblyClosure { get; }

    internal bool IsProviderFinal =>
        Mutability == TranscriptMutabilityV1.ImmutableUnderSourceGuarantee &&
        FinalizedScope != TranscriptFinalizedScopeV1.None;
}
