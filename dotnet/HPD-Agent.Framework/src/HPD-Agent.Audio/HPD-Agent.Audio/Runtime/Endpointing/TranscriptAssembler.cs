using System.Collections.ObjectModel;
using System.Text;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Endpointing;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal sealed record TranscriptAssemblerBounds
{
    internal TranscriptAssemblerBounds(ushort maximumSources, ushort maximumHypotheses,
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

internal sealed record TranscriptTrackSnapshot
{
    private readonly byte[] _textUtf8;
    private readonly Hash256[] _provenance;
    private readonly TranscriptTextRange[] _gaps;

    internal TranscriptTrackSnapshot(TranscriptSourceId sourceId, TranscriptHypothesisId hypothesisId,
        ulong sourceSequence, TranscriptRevision revision, ReadOnlySpan<byte> textUtf8,
        ExpectedAuthorityVectorV1 authority, TranscriptFinality finality,
        IReadOnlyList<Hash256> provenance, IReadOnlyList<TranscriptTextRange> gaps,
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
    internal TranscriptSourceId SourceId { get; }
    internal TranscriptHypothesisId HypothesisId { get; }
    internal ulong SourceSequence { get; }
    internal TranscriptRevision Revision { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal string Text { get; }
    internal ReadOnlySpan<byte> TextUtf8 => _textUtf8;
    internal TranscriptFinality Finality { get; }
    internal IReadOnlyList<Hash256> Provenance { get; }
    internal IReadOnlyList<TranscriptTextRange> Gaps { get; }
    internal bool ContinuityLost { get; }
}

internal sealed class TranscriptAssemblerState
{
    private readonly ReadOnlyDictionary<(TranscriptSourceId, TranscriptHypothesisId), TranscriptTrackSnapshot> _tracks;
    private readonly ReadOnlyDictionary<TranscriptSourceId, (ulong Sequence, Hash256 Digest)> _sourceHeads;
    internal TranscriptAssemblerState(
        IDictionary<(TranscriptSourceId, TranscriptHypothesisId), TranscriptTrackSnapshot>? tracks = null,
        IDictionary<TranscriptSourceId, (ulong Sequence, Hash256 Digest)>? sourceHeads = null)
    {
        _tracks = new ReadOnlyDictionary<(TranscriptSourceId, TranscriptHypothesisId), TranscriptTrackSnapshot>(
            tracks is null ? new Dictionary<(TranscriptSourceId, TranscriptHypothesisId), TranscriptTrackSnapshot>() : new(tracks));
        _sourceHeads = new ReadOnlyDictionary<TranscriptSourceId, (ulong Sequence, Hash256 Digest)>(
            sourceHeads is null ? new Dictionary<TranscriptSourceId, (ulong Sequence, Hash256 Digest)>() : new(sourceHeads));
    }
    internal IReadOnlyDictionary<(TranscriptSourceId, TranscriptHypothesisId), TranscriptTrackSnapshot> Tracks => _tracks;
    internal IReadOnlyDictionary<TranscriptSourceId, (ulong Sequence, Hash256 Digest)> SourceHeads => _sourceHeads;
}

internal abstract record TranscriptAssemblerResult
{
    private TranscriptAssemblerResult() { }
    internal sealed record Applied(TranscriptAssemblerState State, TranscriptTrackSnapshot Track) : TranscriptAssemblerResult;
    internal sealed record Duplicate(TranscriptAssemblerState State, TranscriptTrackSnapshot? Track) : TranscriptAssemblerResult;
    internal sealed record Rejected(TranscriptAssemblerState State, BoundedAscii SafeCode) : TranscriptAssemblerResult;
    internal sealed record AuthorityContinuityLost(TranscriptAssemblerState State, TranscriptSourceId SourceId,
        ulong FromSequence, ulong ThroughSequence, BoundedAscii SafeCode) : TranscriptAssemblerResult;
    internal sealed record TerminalFault(TranscriptAssemblerState State, BoundedAscii SafeCode) : TranscriptAssemblerResult;
}

internal static class TranscriptAssembler
{
    private static readonly TranscriptFinality InitialFinality = new(
        TranscriptMutability.Unknown, null, TranscriptFinalizedScope.None,
        TranscriptBoundaryEvidence.None, TranscriptContinuity.Unknown,
        TranscriptObservability.NotObservable, TranscriptCorrectionState.Correctable,
        TranscriptAssemblyClosure.Open);

    internal static TranscriptAssemblerState Create() => new();

    internal static TranscriptAssemblerResult Apply(TranscriptAssemblerState state,
        TranscriptObservation observation, TranscriptAssemblerBounds bounds,
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
                    ? new TranscriptAssemblerResult.Duplicate(state, current)
                    : Lose(state, observation.SourceId, observation.SourceSequence, observation.SourceSequence,
                        "source-sequence-contradiction", continuityBarrierCapacityAvailable);
            if (observation.SourceSequence != checked(sourceHead.Sequence + 1))
                return Lose(state, observation.SourceId, checked(sourceHead.Sequence + 1), observation.SourceSequence,
                    "source-sequence-gap", continuityBarrierCapacityAvailable);
        }
        else if (state.SourceHeads.Count >= bounds.MaximumSources)
            return new TranscriptAssemblerResult.Rejected(state, new BoundedAscii("source-capacity-refused"));

        if (current is null)
        {
            if (observation is not TranscriptObservation.HypothesisOpened)
                return new TranscriptAssemblerResult.Rejected(state, new BoundedAscii("hypothesis-not-open"));
            if (state.Tracks.Count >= bounds.MaximumHypotheses)
                return new TranscriptAssemblerResult.Rejected(state, new BoundedAscii("hypothesis-capacity-refused"));
            return Commit(state, observation, key, [], InitialFinality, [], false, bounds);
        }
        if (observation is TranscriptObservation.HypothesisOpened)
            return new TranscriptAssemblerResult.Rejected(state, new BoundedAscii("hypothesis-already-open"));
        if (observation.Authority != current.Authority)
            return new TranscriptAssemblerResult.Rejected(state, new BoundedAscii("authority-stale"));
        if (observation.ExpectedBaseRevision != current.Revision)
            return new TranscriptAssemblerResult.Rejected(state, new BoundedAscii("revision-conflict"));
        if (current.Revision.Value >= bounds.MaximumRevisionsPerHypothesis)
            return Authoritative(observation)
                ? Lose(state, observation.SourceId, observation.SourceSequence, observation.SourceSequence,
                    "revision-capacity-refused", continuityBarrierCapacityAvailable)
                : new TranscriptAssemblerResult.Rejected(state, new BoundedAscii("revision-capacity-refused"));

        var text = current.TextUtf8.ToArray();
        var finality = current.Finality;
        var gaps = current.Gaps.ToList();
        var continuityLost = current.ContinuityLost;
        try
        {
            switch (observation)
            {
                case TranscriptObservation.TextAppended append:
                    text = [.. text, .. append.Utf8Bytes.ToArray()];
                    break;
                case TranscriptObservation.TextReplaced replace:
                    text = replace.Utf8Bytes.ToArray();
                    break;
                case TranscriptObservation.RangeCorrected correction:
                    text = Replace(text, correction.Range, correction.ReplacementUtf8Bytes);
                    break;
                case TranscriptObservation.RangeRetracted retraction:
                    text = Replace(text, retraction.Range, []);
                    finality = new TranscriptFinality(finality.Mutability, finality.StablePrefix,
                        TranscriptFinalizedScope.None, finality.BoundaryEvidence, finality.Continuity,
                        finality.Observability, TranscriptCorrectionState.Retracted,
                        TranscriptAssemblyClosure.ClosedForAssembly);
                    break;
                case TranscriptObservation.StablePrefixAdvanced stable:
                    if (stable.Range.StartUtf8Byte != 0 || stable.Range.EndUtf8ByteExclusive > text.Length)
                        throw new ArgumentOutOfRangeException(nameof(observation));
                    finality = new TranscriptFinality(TranscriptMutability.StablePrefix, stable.Range,
                        finality.FinalizedScope, finality.BoundaryEvidence, finality.Continuity,
                        finality.Observability, finality.CorrectionState, finality.AssemblyClosure);
                    break;
                case TranscriptObservation.FinalityAsserted asserted:
                    finality = asserted.Finality;
                    break;
                case TranscriptObservation.GapObserved gap:
                    if (gaps.Count >= bounds.MaximumGaps)
                        return Lose(state, observation.SourceId, observation.SourceSequence, observation.SourceSequence,
                            "gap-capacity-refused", continuityBarrierCapacityAvailable);
                    gaps.Add(gap.Range);
                    break;
                case TranscriptObservation.DiscontinuityObserved:
                    continuityLost = true;
                    break;
            }
        }
        catch (ArgumentException)
        {
            return new TranscriptAssemblerResult.Rejected(state, new BoundedAscii("range-invalid"));
        }
        if (text.Length > bounds.MaximumTextUtf8Bytes)
            return Authoritative(observation)
                ? Lose(state, observation.SourceId, observation.SourceSequence, observation.SourceSequence,
                    "text-capacity-refused", continuityBarrierCapacityAvailable)
                : new TranscriptAssemblerResult.Rejected(state, new BoundedAscii("text-capacity-refused"));
        return Commit(state, observation, key, text, finality, gaps, continuityLost, bounds);
    }

    private static TranscriptAssemblerResult Commit(TranscriptAssemblerState state,
        TranscriptObservation observation, (TranscriptSourceId, TranscriptHypothesisId) key,
        ReadOnlySpan<byte> text, TranscriptFinality finality, IReadOnlyList<TranscriptTextRange> gaps,
        bool continuityLost, TranscriptAssemblerBounds bounds)
    {
        var tracks = state.Tracks.ToDictionary(static entry => entry.Key, static entry => entry.Value);
        var revision = new TranscriptRevision(tracks.TryGetValue(key, out var prior) ? checked(prior.Revision.Value + 1) : 1);
        var provenance = prior is null ? new List<Hash256>() : prior.Provenance.ToList();
        provenance.Add(observation.ProvenanceDigest);
        if (provenance.Count > bounds.MaximumRevisionsPerHypothesis)
            provenance.RemoveAt(0);
        var next = new TranscriptTrackSnapshot(observation.SourceId, observation.HypothesisId,
            observation.SourceSequence, revision, text, observation.Authority, finality, provenance, gaps, continuityLost);
        tracks[key] = next;
        var heads = state.SourceHeads.ToDictionary(static entry => entry.Key, static entry => entry.Value);
        heads[observation.SourceId] = (observation.SourceSequence, observation.ProvenanceDigest);
        return new TranscriptAssemblerResult.Applied(new TranscriptAssemblerState(tracks, heads), next);
    }

    private static byte[] Replace(byte[] source, TranscriptTextRange range, ReadOnlySpan<byte> replacement)
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

    private static bool Authoritative(TranscriptObservation observation) => observation is
        TranscriptObservation.RangeCorrected or TranscriptObservation.RangeRetracted or
        TranscriptObservation.FinalityAsserted or TranscriptObservation.GapObserved;

    private static TranscriptAssemblerResult Lose(TranscriptAssemblerState state,
        TranscriptSourceId source, ulong from, ulong through, string code, bool capacity) => capacity
        ? new TranscriptAssemblerResult.AuthorityContinuityLost(state, source, from, through, new BoundedAscii(code))
        : new TranscriptAssemblerResult.TerminalFault(state, new BoundedAscii("continuity-barrier-capacity-refused"));
}
