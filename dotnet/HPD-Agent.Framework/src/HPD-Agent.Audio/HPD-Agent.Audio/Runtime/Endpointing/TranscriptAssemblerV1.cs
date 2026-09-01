using System.Collections.ObjectModel;
using System.Text;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Endpointing;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal sealed record TranscriptAssemblerBoundsV1
{
    internal TranscriptAssemblerBoundsV1(ushort maximumSources, ushort maximumHypotheses,
        uint maximumTextUtf8Bytes, uint maximumRevisionsPerHypothesis, ushort maximumGaps)
    {
        if (maximumSources == 0 || maximumHypotheses == 0 || maximumTextUtf8Bytes == 0 ||
            maximumRevisionsPerHypothesis == 0 || maximumGaps == 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSources));
        MaximumSources = maximumSources;
        MaximumHypotheses = maximumHypotheses;
        MaximumTextUtf8Bytes = maximumTextUtf8Bytes;
        MaximumRevisionsPerHypothesis = maximumRevisionsPerHypothesis;
        MaximumGaps = maximumGaps;
    }
    internal ushort MaximumSources { get; }
    internal ushort MaximumHypotheses { get; }
    internal uint MaximumTextUtf8Bytes { get; }
    internal uint MaximumRevisionsPerHypothesis { get; }
    internal ushort MaximumGaps { get; }
}

internal sealed record TranscriptTrackSnapshotV1
{
    private readonly byte[] _textUtf8;
    private readonly Hash256[] _provenance;
    private readonly TranscriptTextRangeV1[] _gaps;

    internal TranscriptTrackSnapshotV1(TranscriptSourceIdV1 sourceId, TranscriptHypothesisIdV1 hypothesisId,
        ulong sourceSequence, TranscriptRevisionV1 revision, ReadOnlySpan<byte> textUtf8,
        ExpectedAuthorityVectorV1 authority, TranscriptFinalityV1 finality,
        IReadOnlyList<Hash256> provenance, IReadOnlyList<TranscriptTextRangeV1> gaps,
        bool continuityLost)
    {
        if (!sourceId.IsValid || !hypothesisId.IsValid || sourceSequence == 0)
            throw new ArgumentException("A track requires valid identities and sequence.");
        SourceId = sourceId;
        HypothesisId = hypothesisId;
        SourceSequence = sourceSequence;
        Revision = revision;
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _textUtf8 = textUtf8.ToArray();
        Text = new UTF8Encoding(false, true).GetString(_textUtf8);
        Finality = finality ?? throw new ArgumentNullException(nameof(finality));
        _provenance = provenance?.ToArray() ?? throw new ArgumentNullException(nameof(provenance));
        _gaps = gaps?.ToArray() ?? throw new ArgumentNullException(nameof(gaps));
        Provenance = Array.AsReadOnly(_provenance);
        Gaps = Array.AsReadOnly(_gaps);
        ContinuityLost = continuityLost;
    }
    internal TranscriptSourceIdV1 SourceId { get; }
    internal TranscriptHypothesisIdV1 HypothesisId { get; }
    internal ulong SourceSequence { get; }
    internal TranscriptRevisionV1 Revision { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal string Text { get; }
    internal ReadOnlySpan<byte> TextUtf8 => _textUtf8;
    internal TranscriptFinalityV1 Finality { get; }
    internal IReadOnlyList<Hash256> Provenance { get; }
    internal IReadOnlyList<TranscriptTextRangeV1> Gaps { get; }
    internal bool ContinuityLost { get; }
}

internal sealed class TranscriptAssemblerStateV1
{
    private readonly ReadOnlyDictionary<(TranscriptSourceIdV1, TranscriptHypothesisIdV1), TranscriptTrackSnapshotV1> _tracks;
    private readonly ReadOnlyDictionary<TranscriptSourceIdV1, (ulong Sequence, Hash256 Digest)> _sourceHeads;
    internal TranscriptAssemblerStateV1(
        IDictionary<(TranscriptSourceIdV1, TranscriptHypothesisIdV1), TranscriptTrackSnapshotV1>? tracks = null,
        IDictionary<TranscriptSourceIdV1, (ulong Sequence, Hash256 Digest)>? sourceHeads = null)
    {
        _tracks = new ReadOnlyDictionary<(TranscriptSourceIdV1, TranscriptHypothesisIdV1), TranscriptTrackSnapshotV1>(
            tracks is null ? new Dictionary<(TranscriptSourceIdV1, TranscriptHypothesisIdV1), TranscriptTrackSnapshotV1>() : new(tracks));
        _sourceHeads = new ReadOnlyDictionary<TranscriptSourceIdV1, (ulong Sequence, Hash256 Digest)>(
            sourceHeads is null ? new Dictionary<TranscriptSourceIdV1, (ulong Sequence, Hash256 Digest)>() : new(sourceHeads));
    }
    internal IReadOnlyDictionary<(TranscriptSourceIdV1, TranscriptHypothesisIdV1), TranscriptTrackSnapshotV1> Tracks => _tracks;
    internal IReadOnlyDictionary<TranscriptSourceIdV1, (ulong Sequence, Hash256 Digest)> SourceHeads => _sourceHeads;
}

internal abstract record TranscriptAssemblerResultV1
{
    private TranscriptAssemblerResultV1() { }
    internal sealed record Applied(TranscriptAssemblerStateV1 State, TranscriptTrackSnapshotV1 Track) : TranscriptAssemblerResultV1;
    internal sealed record Duplicate(TranscriptAssemblerStateV1 State, TranscriptTrackSnapshotV1? Track) : TranscriptAssemblerResultV1;
    internal sealed record Rejected(TranscriptAssemblerStateV1 State, BoundedAscii SafeCode) : TranscriptAssemblerResultV1;
    internal sealed record AuthorityContinuityLost(TranscriptAssemblerStateV1 State, TranscriptSourceIdV1 SourceId,
        ulong FromSequence, ulong ThroughSequence, BoundedAscii SafeCode) : TranscriptAssemblerResultV1;
    internal sealed record TerminalFault(TranscriptAssemblerStateV1 State, BoundedAscii SafeCode) : TranscriptAssemblerResultV1;
}

internal static class TranscriptAssemblerV1
{
    private static readonly TranscriptFinalityV1 InitialFinality = new(
        TranscriptMutabilityV1.Unknown, null, TranscriptFinalizedScopeV1.None,
        TranscriptBoundaryEvidenceV1.None, TranscriptContinuityV1.Unknown,
        TranscriptObservabilityV1.NotObservable, TranscriptCorrectionStateV1.Correctable,
        TranscriptAssemblyClosureV1.Open);

    internal static TranscriptAssemblerStateV1 Create() => new();

    internal static TranscriptAssemblerResultV1 Apply(TranscriptAssemblerStateV1 state,
        TranscriptObservationV1 observation, TranscriptAssemblerBoundsV1 bounds,
        bool continuityBarrierCapacityAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(bounds);
        var key = (observation.SourceId, observation.HypothesisId);
        state.Tracks.TryGetValue(key, out var current);
        if (state.SourceHeads.TryGetValue(observation.SourceId, out var sourceHead))
        {
            if (observation.SourceSequence == sourceHead.Sequence)
                return sourceHead.Digest == observation.ProvenanceDigest
                    ? new TranscriptAssemblerResultV1.Duplicate(state, current)
                    : Lose(state, observation.SourceId, observation.SourceSequence, observation.SourceSequence,
                        "source-sequence-contradiction", continuityBarrierCapacityAvailable);
            if (observation.SourceSequence != checked(sourceHead.Sequence + 1))
                return Lose(state, observation.SourceId, checked(sourceHead.Sequence + 1), observation.SourceSequence,
                    "source-sequence-gap", continuityBarrierCapacityAvailable);
        }
        else if (state.SourceHeads.Count >= bounds.MaximumSources)
            return new TranscriptAssemblerResultV1.Rejected(state, new BoundedAscii("source-capacity-refused"));

        if (current is null)
        {
            if (observation is not TranscriptObservationV1.HypothesisOpened)
                return new TranscriptAssemblerResultV1.Rejected(state, new BoundedAscii("hypothesis-not-open"));
            if (state.Tracks.Count >= bounds.MaximumHypotheses)
                return new TranscriptAssemblerResultV1.Rejected(state, new BoundedAscii("hypothesis-capacity-refused"));
            return Commit(state, observation, key, [], InitialFinality, [], false, bounds);
        }
        if (observation is TranscriptObservationV1.HypothesisOpened)
            return new TranscriptAssemblerResultV1.Rejected(state, new BoundedAscii("hypothesis-already-open"));
        if (observation.Authority != current.Authority)
            return new TranscriptAssemblerResultV1.Rejected(state, new BoundedAscii("authority-stale"));
        if (observation.ExpectedBaseRevision != current.Revision)
            return new TranscriptAssemblerResultV1.Rejected(state, new BoundedAscii("revision-conflict"));
        if (current.Revision.Value >= bounds.MaximumRevisionsPerHypothesis)
            return Authoritative(observation)
                ? Lose(state, observation.SourceId, observation.SourceSequence, observation.SourceSequence,
                    "revision-capacity-refused", continuityBarrierCapacityAvailable)
                : new TranscriptAssemblerResultV1.Rejected(state, new BoundedAscii("revision-capacity-refused"));

        var text = current.TextUtf8.ToArray();
        var finality = current.Finality;
        var gaps = current.Gaps.ToList();
        var continuityLost = current.ContinuityLost;
        try
        {
            switch (observation)
            {
                case TranscriptObservationV1.TextAppended append:
                    text = [.. text, .. append.Utf8Bytes.ToArray()];
                    break;
                case TranscriptObservationV1.TextReplaced replace:
                    text = replace.Utf8Bytes.ToArray();
                    break;
                case TranscriptObservationV1.RangeCorrected correction:
                    text = Replace(text, correction.Range, correction.ReplacementUtf8Bytes);
                    break;
                case TranscriptObservationV1.RangeRetracted retraction:
                    text = Replace(text, retraction.Range, []);
                    finality = new TranscriptFinalityV1(finality.Mutability, finality.StablePrefix,
                        TranscriptFinalizedScopeV1.None, finality.BoundaryEvidence, finality.Continuity,
                        finality.Observability, TranscriptCorrectionStateV1.Retracted,
                        TranscriptAssemblyClosureV1.ClosedForAssembly);
                    break;
                case TranscriptObservationV1.StablePrefixAdvanced stable:
                    if (stable.Range.StartUtf8Byte != 0 || stable.Range.EndUtf8ByteExclusive > text.Length)
                        throw new ArgumentOutOfRangeException(nameof(observation));
                    finality = new TranscriptFinalityV1(TranscriptMutabilityV1.StablePrefix, stable.Range,
                        finality.FinalizedScope, finality.BoundaryEvidence, finality.Continuity,
                        finality.Observability, finality.CorrectionState, finality.AssemblyClosure);
                    break;
                case TranscriptObservationV1.FinalityAsserted asserted:
                    finality = asserted.Finality;
                    break;
                case TranscriptObservationV1.GapObserved gap:
                    if (gaps.Count >= bounds.MaximumGaps)
                        return Lose(state, observation.SourceId, observation.SourceSequence, observation.SourceSequence,
                            "gap-capacity-refused", continuityBarrierCapacityAvailable);
                    gaps.Add(gap.Range);
                    break;
                case TranscriptObservationV1.DiscontinuityObserved:
                    continuityLost = true;
                    break;
            }
        }
        catch (ArgumentException)
        {
            return new TranscriptAssemblerResultV1.Rejected(state, new BoundedAscii("range-invalid"));
        }
        if (text.Length > bounds.MaximumTextUtf8Bytes)
            return Authoritative(observation)
                ? Lose(state, observation.SourceId, observation.SourceSequence, observation.SourceSequence,
                    "text-capacity-refused", continuityBarrierCapacityAvailable)
                : new TranscriptAssemblerResultV1.Rejected(state, new BoundedAscii("text-capacity-refused"));
        return Commit(state, observation, key, text, finality, gaps, continuityLost, bounds);
    }

    private static TranscriptAssemblerResultV1 Commit(TranscriptAssemblerStateV1 state,
        TranscriptObservationV1 observation, (TranscriptSourceIdV1, TranscriptHypothesisIdV1) key,
        ReadOnlySpan<byte> text, TranscriptFinalityV1 finality, IReadOnlyList<TranscriptTextRangeV1> gaps,
        bool continuityLost, TranscriptAssemblerBoundsV1 bounds)
    {
        var tracks = state.Tracks.ToDictionary(static entry => entry.Key, static entry => entry.Value);
        var revision = new TranscriptRevisionV1(tracks.TryGetValue(key, out var prior) ? checked(prior.Revision.Value + 1) : 1);
        var provenance = prior is null ? new List<Hash256>() : prior.Provenance.ToList();
        provenance.Add(observation.ProvenanceDigest);
        if (provenance.Count > bounds.MaximumRevisionsPerHypothesis)
            provenance.RemoveAt(0);
        var next = new TranscriptTrackSnapshotV1(observation.SourceId, observation.HypothesisId,
            observation.SourceSequence, revision, text, observation.Authority, finality, provenance, gaps, continuityLost);
        tracks[key] = next;
        var heads = state.SourceHeads.ToDictionary(static entry => entry.Key, static entry => entry.Value);
        heads[observation.SourceId] = (observation.SourceSequence, observation.ProvenanceDigest);
        return new TranscriptAssemblerResultV1.Applied(new TranscriptAssemblerStateV1(tracks, heads), next);
    }

    private static byte[] Replace(byte[] source, TranscriptTextRangeV1 range, ReadOnlySpan<byte> replacement)
    {
        var start = checked((int)range.StartUtf8Byte);
        var end = checked((int)range.EndUtf8ByteExclusive);
        if (end > source.Length) throw new ArgumentOutOfRangeException(nameof(range));
        var encoding = new UTF8Encoding(false, true);
        _ = encoding.GetString(source.AsSpan(0, start));
        _ = encoding.GetString(source.AsSpan(end));
        var result = new byte[checked(source.Length - (end - start) + replacement.Length)];
        source.AsSpan(0, start).CopyTo(result);
        replacement.CopyTo(result.AsSpan(start));
        source.AsSpan(end).CopyTo(result.AsSpan(start + replacement.Length));
        _ = encoding.GetString(result);
        return result;
    }

    private static bool Authoritative(TranscriptObservationV1 observation) => observation is
        TranscriptObservationV1.RangeCorrected or TranscriptObservationV1.RangeRetracted or
        TranscriptObservationV1.FinalityAsserted or TranscriptObservationV1.GapObserved;

    private static TranscriptAssemblerResultV1 Lose(TranscriptAssemblerStateV1 state,
        TranscriptSourceIdV1 source, ulong from, ulong through, string code, bool capacity) => capacity
        ? new TranscriptAssemblerResultV1.AuthorityContinuityLost(state, source, from, through, new BoundedAscii(code))
        : new TranscriptAssemblerResultV1.TerminalFault(state, new BoundedAscii("continuity-barrier-capacity-refused"));
}
