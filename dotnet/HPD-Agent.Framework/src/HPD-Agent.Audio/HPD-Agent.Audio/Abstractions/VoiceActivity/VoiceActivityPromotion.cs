using System.Collections.ObjectModel;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivityPromotionState : byte
{
    Unknown = 1,
    CandidateOpen = 2,
    Open = 3,
    CandidateClose = 4,
    Closed = 5,
    Discontinuous = 6,
    Unobservable = 7,
    Faulted = 8,
}

internal enum VoiceActivityPromotionEdge : byte
{
    Observation = 1,
    Correction = 2,
    ManualPress = 3,
    ManualContinue = 4,
    ManualRelease = 5,
    Discontinuity = 6,
}

internal enum VoiceActivityPromotionFactKind : byte
{
    Opened = 1,
    Continued = 2,
    Closed = 3,
    FalseStart = 4,
    Corrected = 5,
    Unobservable = 6,
    Discontinuous = 7,
    Faulted = 8,
}

internal sealed record VoiceActivityPromotionInput
{
    internal VoiceActivityPromotionInput(
        ulong planGeneration,
        ulong configRevision,
        SessionAuthorityStampV1 session,
        GraphDirection direction,
        string sourceKey,
        ulong sourceGeneration,
        ulong sourceSequence,
        VoiceActivityPromotionEdge edge,
        VoiceActivitySourceOutcome? outcome,
        ulong correctsPromotionSequence = 0)
    {
        if (planGeneration == 0 || configRevision == 0 || !session.IsValid ||
            sourceGeneration == 0 || sourceSequence == 0)
            throw new ArgumentOutOfRangeException();
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        SourceKey = ActivitySourceRequest.RequireAscii(sourceKey, nameof(sourceKey));
        if (!Enum.IsDefined(edge)) throw new ArgumentOutOfRangeException(nameof(edge));
        if (edge == VoiceActivityPromotionEdge.Discontinuity)
        {
            if (outcome is not null || correctsPromotionSequence != 0)
                throw new ArgumentException("Discontinuity carries no source outcome or correction target.");
        }
        else
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if ((edge == VoiceActivityPromotionEdge.Correction) != (correctsPromotionSequence != 0))
                throw new ArgumentException("Only correction input names one prior promotion fact.");
        }
        PlanGeneration = planGeneration;
        ConfigRevision = configRevision;
        Session = session;
        Direction = direction;
        SourceGeneration = sourceGeneration;
        SourceSequence = sourceSequence;
        Edge = edge;
        Outcome = outcome;
        CorrectsPromotionSequence = correctsPromotionSequence;
    }

    internal ulong PlanGeneration { get; }
    internal ulong ConfigRevision { get; }
    internal SessionAuthorityStampV1 Session { get; }
    internal GraphDirection Direction { get; }
    internal string SourceKey { get; }
    internal ulong SourceGeneration { get; }
    internal ulong SourceSequence { get; }
    internal VoiceActivityPromotionEdge Edge { get; }
    internal VoiceActivitySourceOutcome? Outcome { get; }
    internal ulong CorrectsPromotionSequence { get; }
}

internal sealed record VoiceActivityPromotionFact
{
    private readonly string[] _contributors;

    internal VoiceActivityPromotionFact(
        ulong planGeneration,
        ulong configRevision,
        SessionAuthorityStampV1 session,
        GraphDirection direction,
        ulong promotionSequence,
        VoiceActivityPromotionFactKind kind,
        VoiceActivityPromotionState state,
        VoiceActivityMediaExtent? extent,
        IReadOnlyList<string> contributors,
        string reason,
        MonotonicStampV1? observedAt,
        MonotonicStampV1? processedAt,
        ulong correctsPromotionSequence)
    {
        if (planGeneration == 0 || configRevision == 0 || !session.IsValid || promotionSequence == 0)
            throw new ArgumentOutOfRangeException();
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(state)) throw new ArgumentOutOfRangeException();
        ArgumentNullException.ThrowIfNull(contributors);
        _contributors = contributors.Select(static value =>
            ActivitySourceRequest.RequireAscii(value, nameof(contributors))).ToArray();
        if (_contributors.Length == 0 || _contributors.Length > VoiceActivityRequest.MaximumSources ||
            _contributors.Distinct(StringComparer.Ordinal).Count() != _contributors.Length)
            throw new ArgumentException("Promotion contributors must be bounded and unique.", nameof(contributors));
        Reason = ActivitySourceRequest.RequireAscii(reason, nameof(reason));
        if (observedAt.HasValue != processedAt.HasValue ||
            (observedAt.HasValue && (!observedAt.Value.IsValid ||
                processedAt!.Value.CompareTo(observedAt.Value) is ClockComparison.Earlier or ClockComparison.Incomparable)))
            throw new ArgumentException("Promotion observation timing must be paired and nondecreasing.");
        if ((kind == VoiceActivityPromotionFactKind.Corrected) != (correctsPromotionSequence != 0))
            throw new ArgumentException("Only correction facts name a corrected promotion sequence.");
        PlanGeneration = planGeneration;
        ConfigRevision = configRevision;
        Session = session;
        Direction = direction;
        PromotionSequence = promotionSequence;
        Kind = kind;
        State = state;
        Extent = extent;
        Contributors = new ReadOnlyCollection<string>(_contributors);
        ObservedAt = observedAt;
        ProcessedAt = processedAt;
        CorrectsPromotionSequence = correctsPromotionSequence;
    }

    internal ulong PlanGeneration { get; }
    internal ulong ConfigRevision { get; }
    internal SessionAuthorityStampV1 Session { get; }
    internal GraphDirection Direction { get; }
    internal ulong PromotionSequence { get; }
    internal VoiceActivityPromotionFactKind Kind { get; }
    internal VoiceActivityPromotionState State { get; }
    internal VoiceActivityMediaExtent? Extent { get; }
    internal IReadOnlyList<string> Contributors { get; }
    internal string Reason { get; }
    internal MonotonicStampV1? ObservedAt { get; }
    internal MonotonicStampV1? ProcessedAt { get; }
    internal ulong CorrectsPromotionSequence { get; }
}

internal abstract record VoiceActivityPromotionResult
{
    private VoiceActivityPromotionResult() { }
    internal sealed record Applied(VoiceActivityPromotionState State, VoiceActivityPromotionFact? Fact) :
        VoiceActivityPromotionResult;
    internal sealed record Duplicate(VoiceActivityPromotionState State) : VoiceActivityPromotionResult;
    internal sealed record Stale(VoiceActivityPromotionState State) : VoiceActivityPromotionResult;
    internal sealed record Rejected(VoiceActivityPromotionState State, string SafeCode) :
        VoiceActivityPromotionResult;
}

internal sealed class VoiceActivityPromoter
{
    private readonly VoiceActivityEffectivePlan _plan;
    private readonly SessionAuthorityStampV1 _session;
    private readonly GraphDirection _direction;
    private readonly Dictionary<string, ulong> _sourceGenerations;
    private readonly Dictionary<string, VoiceActivityPromotionInput> _lastInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _active = new(StringComparer.Ordinal);
    private readonly List<VoiceActivityPromotionFact> _facts = [];
    private readonly int _openThreshold;
    private readonly int _closeThreshold;
    private readonly int _maximumFacts;
    private readonly int _maximumCorrections;
    private int _openCount;
    private int _closeCount;
    private int _correctionCount;
    private bool _stableOpen;
    private long? _candidateStart;
    private long? _extentStart;
    private long? _extentEnd;
    private GraphGenerationId? _graphGeneration;
    private bool _extentExact = true;
    private ulong _promotionSequence;

    internal VoiceActivityPromoter(
        VoiceActivityEffectivePlan plan,
        SessionAuthorityStampV1 session,
        GraphDirection direction,
        IReadOnlyDictionary<string, ulong> sourceGenerations)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        if (!session.IsValid) throw new ArgumentException("A live session authority is required.", nameof(session));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        _session = session;
        _direction = direction;
        ArgumentNullException.ThrowIfNull(sourceGenerations);
        _sourceGenerations = sourceGenerations.ToDictionary(static row =>
            ActivitySourceRequest.RequireAscii(row.Key, nameof(sourceGenerations)), static row => row.Value,
            StringComparer.Ordinal);
        var authority = plan.PromotionAuthority.SourceKeys;
        if (_sourceGenerations.Count != authority.Count || authority.Any(key =>
                !_sourceGenerations.TryGetValue(key, out var generation) || generation == 0))
            throw new ArgumentException("Every promotion source requires one positive source generation.", nameof(sourceGenerations));
        (_openThreshold, _closeThreshold) = plan.Request.Responsiveness switch
        {
            ActivityResponsiveness.Responsive => (1, 1),
            ActivityResponsiveness.Balanced => (2, 2),
            ActivityResponsiveness.Conservative => (3, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(plan)),
        };
        _maximumFacts = plan.Request.Limits?.MaximumObservationHistory ?? 1_024;
        _maximumCorrections = plan.Request.Limits?.MaximumCorrectionHistory ?? 64;
        State = VoiceActivityPromotionState.Unknown;
    }

    internal VoiceActivityPromotionState State { get; private set; }
    internal IReadOnlyList<VoiceActivityPromotionFact> Facts => _facts.AsReadOnly();

    internal VoiceActivityPromotionResult Apply(VoiceActivityPromotionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PlanGeneration != _plan.PlanGeneration || input.ConfigRevision != _plan.ConfigRevision ||
            input.Session != _session || input.Direction != _direction ||
            !_sourceGenerations.TryGetValue(input.SourceKey, out var sourceGeneration) ||
            input.SourceGeneration != sourceGeneration)
            return new VoiceActivityPromotionResult.Stale(State);
        if (_lastInputs.TryGetValue(input.SourceKey, out var prior))
        {
            if (input.SourceSequence < prior.SourceSequence) return new VoiceActivityPromotionResult.Stale(State);
            if (input.SourceSequence == prior.SourceSequence)
                return input == prior
                    ? new VoiceActivityPromotionResult.Duplicate(State)
                    : Reject("source-sequence-contradiction");
        }
        if (_facts.Count >= _maximumFacts) return Reject("promotion-history-exhausted");
        _lastInputs[input.SourceKey] = input;

        if (input.Edge == VoiceActivityPromotionEdge.Discontinuity)
            return Discontinue(input.SourceKey, "explicit-discontinuity");
        if (!EdgeAllowed(input)) return Reject("promotion-edge-invalid");
        if (input.Edge == VoiceActivityPromotionEdge.Correction)
            return Correct(input);
        if (input.Outcome is not VoiceActivitySourceOutcome.Observed observed)
            return ApplyNonObservation(input.SourceKey, input.Outcome!);
        if (!TryActivity(input, observed, out var active)) return Reject("measurement-promotion-invalid");
        if (!AcceptExtent(observed.Extent)) return Discontinue(input.SourceKey, "mixed-graph-generation");
        _active[input.SourceKey] = active;
        return Advance(input.SourceKey, observed);
    }

    private VoiceActivityPromotionResult Advance(string sourceKey, VoiceActivitySourceOutcome.Observed observed)
    {
        var extent = observed.Extent;
        var aggregate = _active.Any(row => row.Value);
        var contributors = _active.Where(static row => row.Value).Select(static row => row.Key)
            .OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        if (aggregate)
        {
            _closeCount = 0;
            if (_stableOpen)
            {
                State = VoiceActivityPromotionState.Open;
                Extend(extent);
                return Emit(VoiceActivityPromotionFactKind.Continued, contributors, "activity-continued",
                    observedAt: observed.ObservedAt, processedAt: observed.ProcessedAt);
            }
            _candidateStart ??= extent.StartInclusive;
            _extentEnd = Math.Max(_extentEnd ?? extent.EndExclusive, extent.EndExclusive);
            _extentExact &= extent.Exact;
            _openCount++;
            if (_openCount < _openThreshold)
            {
                State = VoiceActivityPromotionState.CandidateOpen;
                return Applied();
            }
            _stableOpen = true;
            _extentStart = _candidateStart;
            _candidateStart = null;
            _openCount = 0;
            State = VoiceActivityPromotionState.Open;
            return Emit(VoiceActivityPromotionFactKind.Opened, contributors, "activity-opened",
                observedAt: observed.ObservedAt, processedAt: observed.ProcessedAt);
        }

        _openCount = 0;
        if (!_stableOpen)
        {
            if (State == VoiceActivityPromotionState.CandidateOpen)
            {
                State = VoiceActivityPromotionState.Closed;
                _candidateStart = null;
                _extentEnd = null;
                _extentExact = true;
                return Emit(VoiceActivityPromotionFactKind.FalseStart, [sourceKey], "activity-false-start",
                    observedAt: observed.ObservedAt, processedAt: observed.ProcessedAt);
            }
            State = VoiceActivityPromotionState.Closed;
            return Applied();
        }
        Extend(extent);
        _closeCount++;
        if (_closeCount < _closeThreshold)
        {
            State = VoiceActivityPromotionState.CandidateClose;
            return Applied();
        }
        _stableOpen = false;
        _closeCount = 0;
        State = VoiceActivityPromotionState.Closed;
        var result = Emit(VoiceActivityPromotionFactKind.Closed, [sourceKey], "activity-closed",
            observedAt: observed.ObservedAt, processedAt: observed.ProcessedAt);
        _extentStart = null;
        _extentEnd = null;
        return result;
    }

    private VoiceActivityPromotionResult Correct(VoiceActivityPromotionInput input)
    {
        if (_maximumCorrections == 0 || _correctionCount >= _maximumCorrections)
            return Reject("correction-history-exhausted");
        var target = _facts.LastOrDefault(row => row.PromotionSequence == input.CorrectsPromotionSequence);
        if (target is null || target != _facts.Last()) return Reject("correction-target-stale");
        if (input.Outcome is not VoiceActivitySourceOutcome.Observed observed ||
            !TryActivity(input, observed, out var active) || !AcceptExtent(observed.Extent))
            return Reject("correction-evidence-invalid");
        _correctionCount++;
        _active[input.SourceKey] = active;
        _stableOpen = active;
        State = active ? VoiceActivityPromotionState.Open : VoiceActivityPromotionState.Closed;
        _extentStart = active ? observed.Extent.StartInclusive : null;
        _extentEnd = active ? observed.Extent.EndExclusive : null;
        _extentExact = observed.Extent.Exact;
        return Emit(VoiceActivityPromotionFactKind.Corrected, [input.SourceKey],
            active ? "activity-corrected-open" : "activity-corrected-closed", target.PromotionSequence,
            observed.ObservedAt, observed.ProcessedAt);
    }

    private VoiceActivityPromotionResult ApplyNonObservation(string sourceKey, VoiceActivitySourceOutcome outcome)
    {
        _active.Remove(sourceKey);
        return outcome switch
        {
            VoiceActivitySourceOutcome.NoObservation no when no.Reason is
                VoiceActivityNoObservationReason.Reset or VoiceActivityNoObservationReason.Teardown or
                VoiceActivityNoObservationReason.SourceRevoked => Discontinue(sourceKey, "source-discontinuous"),
            VoiceActivitySourceOutcome.InvalidInput => Discontinue(sourceKey, "input-discontinuous"),
            VoiceActivitySourceOutcome.Fault => SetExceptional(sourceKey,
                VoiceActivityPromotionState.Faulted, VoiceActivityPromotionFactKind.Faulted, "source-faulted"),
            _ => SetExceptional(sourceKey, VoiceActivityPromotionState.Unobservable,
                VoiceActivityPromotionFactKind.Unobservable, "source-unobservable"),
        };
    }

    private VoiceActivityPromotionResult Discontinue(string sourceKey, string reason)
    {
        var hadOpen = _stableOpen;
        _stableOpen = false;
        _openCount = _closeCount = 0;
        _candidateStart = _extentStart = _extentEnd = null;
        _active.Clear();
        _graphGeneration = null;
        _extentExact = true;
        State = VoiceActivityPromotionState.Discontinuous;
        return Emit(VoiceActivityPromotionFactKind.Discontinuous, [sourceKey],
            hadOpen ? $"{reason}-open-abandoned" : reason);
    }

    private VoiceActivityPromotionResult SetExceptional(string sourceKey, VoiceActivityPromotionState state,
        VoiceActivityPromotionFactKind kind, string reason)
    {
        State = state;
        return Emit(kind, [sourceKey], reason);
    }

    private bool EdgeAllowed(VoiceActivityPromotionInput input)
    {
        var kind = _plan.Sources.Single(row => row.Request.SourceKey == input.SourceKey).Request.Kind;
        return input.Edge switch
        {
            VoiceActivityPromotionEdge.Observation or VoiceActivityPromotionEdge.Correction => true,
            VoiceActivityPromotionEdge.ManualPress or VoiceActivityPromotionEdge.ManualContinue or
                VoiceActivityPromotionEdge.ManualRelease => kind == ActivitySourceKind.Manual &&
                    _plan.PromotionAuthority.Mode == VoiceActivityPromotionMode.Manual,
            _ => false,
        };
    }

    private static bool TryActivity(VoiceActivityPromotionInput input,
        VoiceActivitySourceOutcome.Observed observed, out bool active)
    {
        if (input.Edge == VoiceActivityPromotionEdge.ManualPress ||
            input.Edge == VoiceActivityPromotionEdge.ManualContinue) { active = true; return true; }
        if (input.Edge == VoiceActivityPromotionEdge.ManualRelease) { active = false; return true; }
        switch (observed.Measurement)
        {
            case VoiceActivityMeasurement.Numeric numeric:
                active = numeric.Value >= observed.Descriptor.Minimum +
                    (observed.Descriptor.Maximum - observed.Descriptor.Minimum) / 2d;
                return true;
            case VoiceActivityMeasurement.Binary binary:
                active = binary.Value;
                return true;
            case VoiceActivityMeasurement.Category category:
                var value = category.Value.ToString();
                if (value is "speech" or "active" or "open" or "pressed") { active = true; return true; }
                if (value is "silence" or "inactive" or "closed" or "released") { active = false; return true; }
                break;
        }
        active = false;
        return false;
    }

    private bool AcceptExtent(VoiceActivityMediaExtent extent)
    {
        if (_graphGeneration is { } graph && graph != extent.GraphGeneration) return false;
        _graphGeneration ??= extent.GraphGeneration;
        return true;
    }

    private void Extend(VoiceActivityMediaExtent extent)
    {
        _extentStart = Math.Min(_extentStart ?? extent.StartInclusive, extent.StartInclusive);
        _extentEnd = Math.Max(_extentEnd ?? extent.EndExclusive, extent.EndExclusive);
        _extentExact &= extent.Exact;
    }

    private VoiceActivityPromotionResult Emit(VoiceActivityPromotionFactKind kind,
        IReadOnlyList<string> contributors, string reason, ulong corrects = 0,
        MonotonicStampV1? observedAt = null, MonotonicStampV1? processedAt = null)
    {
        VoiceActivityMediaExtent? extent = _graphGeneration.HasValue && _extentStart.HasValue && _extentEnd.HasValue
            ? new VoiceActivityMediaExtent(_graphGeneration.Value, _extentStart.Value, _extentEnd.Value, _extentExact)
            : null;
        var fact = new VoiceActivityPromotionFact(_plan.PlanGeneration, _plan.ConfigRevision, _session, _direction,
            ++_promotionSequence, kind, State, extent, contributors, reason, observedAt, processedAt, corrects);
        _facts.Add(fact);
        return new VoiceActivityPromotionResult.Applied(State, fact);
    }

    private VoiceActivityPromotionResult Applied() => new VoiceActivityPromotionResult.Applied(State, null);
    private VoiceActivityPromotionResult Rejected(string code) =>
        new VoiceActivityPromotionResult.Rejected(State, code);
    private VoiceActivityPromotionResult Reject(string code) => Rejected(code);
}

internal enum VoiceActivityCriticalEvidenceAppendResult : byte
{
    Appended = 1,
    Duplicate = 2,
    Stale = 3,
    CapacityExceeded = 4,
    Contradictory = 5,
}

internal sealed class VoiceActivityCriticalEvidenceBuffer
{
    private readonly VoiceActivityPromotionFact[] _facts;
    private int _count;

    internal VoiceActivityCriticalEvidenceBuffer(ulong planGeneration, ulong configRevision,
        SessionAuthorityStampV1 session, GraphDirection direction, int capacity)
    {
        if (planGeneration == 0 || configRevision == 0 || !session.IsValid) throw new ArgumentOutOfRangeException();
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (capacity is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(capacity));
        PlanGeneration = planGeneration;
        ConfigRevision = configRevision;
        Session = session;
        Direction = direction;
        _facts = new VoiceActivityPromotionFact[capacity];
    }

    internal ulong PlanGeneration { get; }
    internal ulong ConfigRevision { get; }
    internal SessionAuthorityStampV1 Session { get; }
    internal GraphDirection Direction { get; }
    internal IReadOnlyList<VoiceActivityPromotionFact> Snapshot =>
        Array.AsReadOnly(_facts.Take(_count).ToArray());

    internal VoiceActivityCriticalEvidenceAppendResult Append(VoiceActivityPromotionFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.PlanGeneration != PlanGeneration || fact.ConfigRevision != ConfigRevision ||
            fact.Session != Session || fact.Direction != Direction)
            return VoiceActivityCriticalEvidenceAppendResult.Stale;
        if (_count > 0 && fact.PromotionSequence <= _facts[_count - 1].PromotionSequence)
            return fact.PromotionSequence < _facts[_count - 1].PromotionSequence
                ? VoiceActivityCriticalEvidenceAppendResult.Stale
                : fact == _facts[_count - 1]
                    ? VoiceActivityCriticalEvidenceAppendResult.Duplicate
                    : VoiceActivityCriticalEvidenceAppendResult.Contradictory;
        if (_count == _facts.Length) return VoiceActivityCriticalEvidenceAppendResult.CapacityExceeded;
        _facts[_count++] = fact;
        return VoiceActivityCriticalEvidenceAppendResult.Appended;
    }
}
