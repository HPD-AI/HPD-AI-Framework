using System.Collections.ObjectModel;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivityPromotionStateV1 : byte
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

internal enum VoiceActivityPromotionEdgeV1 : byte
{
    Observation = 1,
    Correction = 2,
    ManualPress = 3,
    ManualContinue = 4,
    ManualRelease = 5,
    Discontinuity = 6,
}

internal enum VoiceActivityPromotionFactKindV1 : byte
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

internal sealed record VoiceActivityPromotionInputV1
{
    internal VoiceActivityPromotionInputV1(
        ulong planGeneration,
        ulong configRevision,
        SessionAuthorityStampV1 session,
        GraphDirectionV1 direction,
        string sourceKey,
        ulong sourceGeneration,
        ulong sourceSequence,
        VoiceActivityPromotionEdgeV1 edge,
        VoiceActivitySourceOutcomeV1? outcome,
        ulong correctsPromotionSequence = 0)
    {
        if (planGeneration == 0 || configRevision == 0 || !session.IsValid ||
            sourceGeneration == 0 || sourceSequence == 0)
            throw new ArgumentOutOfRangeException();
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        SourceKey = ActivitySourceRequestV1.RequireAscii(sourceKey, nameof(sourceKey));
        if (!Enum.IsDefined(edge)) throw new ArgumentOutOfRangeException(nameof(edge));
        if (edge == VoiceActivityPromotionEdgeV1.Discontinuity)
        {
            if (outcome is not null || correctsPromotionSequence != 0)
                throw new ArgumentException("Discontinuity carries no source outcome or correction target.");
        }
        else
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if ((edge == VoiceActivityPromotionEdgeV1.Correction) != (correctsPromotionSequence != 0))
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
    internal GraphDirectionV1 Direction { get; }
    internal string SourceKey { get; }
    internal ulong SourceGeneration { get; }
    internal ulong SourceSequence { get; }
    internal VoiceActivityPromotionEdgeV1 Edge { get; }
    internal VoiceActivitySourceOutcomeV1? Outcome { get; }
    internal ulong CorrectsPromotionSequence { get; }
}

internal sealed record VoiceActivityPromotionFactV1
{
    private readonly string[] _contributors;

    internal VoiceActivityPromotionFactV1(
        ulong planGeneration,
        ulong configRevision,
        SessionAuthorityStampV1 session,
        GraphDirectionV1 direction,
        ulong promotionSequence,
        VoiceActivityPromotionFactKindV1 kind,
        VoiceActivityPromotionStateV1 state,
        VoiceActivityMediaExtentV1? extent,
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
            ActivitySourceRequestV1.RequireAscii(value, nameof(contributors))).ToArray();
        if (_contributors.Length == 0 || _contributors.Length > VoiceActivityRequestV1.MaximumSources ||
            _contributors.Distinct(StringComparer.Ordinal).Count() != _contributors.Length)
            throw new ArgumentException("Promotion contributors must be bounded and unique.", nameof(contributors));
        Reason = ActivitySourceRequestV1.RequireAscii(reason, nameof(reason));
        if (observedAt.HasValue != processedAt.HasValue ||
            (observedAt.HasValue && (!observedAt.Value.IsValid ||
                processedAt!.Value.CompareTo(observedAt.Value) is ClockComparison.Earlier or ClockComparison.Incomparable)))
            throw new ArgumentException("Promotion observation timing must be paired and nondecreasing.");
        if ((kind == VoiceActivityPromotionFactKindV1.Corrected) != (correctsPromotionSequence != 0))
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
    internal GraphDirectionV1 Direction { get; }
    internal ulong PromotionSequence { get; }
    internal VoiceActivityPromotionFactKindV1 Kind { get; }
    internal VoiceActivityPromotionStateV1 State { get; }
    internal VoiceActivityMediaExtentV1? Extent { get; }
    internal IReadOnlyList<string> Contributors { get; }
    internal string Reason { get; }
    internal MonotonicStampV1? ObservedAt { get; }
    internal MonotonicStampV1? ProcessedAt { get; }
    internal ulong CorrectsPromotionSequence { get; }
}

internal abstract record VoiceActivityPromotionResultV1
{
    private VoiceActivityPromotionResultV1() { }
    internal sealed record Applied(VoiceActivityPromotionStateV1 State, VoiceActivityPromotionFactV1? Fact) :
        VoiceActivityPromotionResultV1;
    internal sealed record Duplicate(VoiceActivityPromotionStateV1 State) : VoiceActivityPromotionResultV1;
    internal sealed record Stale(VoiceActivityPromotionStateV1 State) : VoiceActivityPromotionResultV1;
    internal sealed record Rejected(VoiceActivityPromotionStateV1 State, string SafeCode) :
        VoiceActivityPromotionResultV1;
}

internal sealed class VoiceActivityPromoterV1
{
    private readonly VoiceActivityEffectivePlanV1 _plan;
    private readonly SessionAuthorityStampV1 _session;
    private readonly GraphDirectionV1 _direction;
    private readonly Dictionary<string, ulong> _sourceGenerations;
    private readonly Dictionary<string, VoiceActivityPromotionInputV1> _lastInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _active = new(StringComparer.Ordinal);
    private readonly List<VoiceActivityPromotionFactV1> _facts = [];
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

    internal VoiceActivityPromoterV1(
        VoiceActivityEffectivePlanV1 plan,
        SessionAuthorityStampV1 session,
        GraphDirectionV1 direction,
        IReadOnlyDictionary<string, ulong> sourceGenerations)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        if (!session.IsValid) throw new ArgumentException("A live session authority is required.", nameof(session));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        _session = session;
        _direction = direction;
        ArgumentNullException.ThrowIfNull(sourceGenerations);
        _sourceGenerations = sourceGenerations.ToDictionary(static row =>
            ActivitySourceRequestV1.RequireAscii(row.Key, nameof(sourceGenerations)), static row => row.Value,
            StringComparer.Ordinal);
        var authority = plan.PromotionAuthority.SourceKeys;
        if (_sourceGenerations.Count != authority.Count || authority.Any(key =>
                !_sourceGenerations.TryGetValue(key, out var generation) || generation == 0))
            throw new ArgumentException("Every promotion source requires one positive source generation.", nameof(sourceGenerations));
        (_openThreshold, _closeThreshold) = plan.Request.Responsiveness switch
        {
            ActivityResponsivenessV1.Responsive => (1, 1),
            ActivityResponsivenessV1.Balanced => (2, 2),
            ActivityResponsivenessV1.Conservative => (3, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(plan)),
        };
        _maximumFacts = plan.Request.Limits?.MaximumObservationHistory ?? 1_024;
        _maximumCorrections = plan.Request.Limits?.MaximumCorrectionHistory ?? 64;
        State = VoiceActivityPromotionStateV1.Unknown;
    }

    internal VoiceActivityPromotionStateV1 State { get; private set; }
    internal IReadOnlyList<VoiceActivityPromotionFactV1> Facts => _facts.AsReadOnly();

    internal VoiceActivityPromotionResultV1 Apply(VoiceActivityPromotionInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PlanGeneration != _plan.PlanGeneration || input.ConfigRevision != _plan.ConfigRevision ||
            input.Session != _session || input.Direction != _direction ||
            !_sourceGenerations.TryGetValue(input.SourceKey, out var sourceGeneration) ||
            input.SourceGeneration != sourceGeneration)
            return new VoiceActivityPromotionResultV1.Stale(State);
        if (_lastInputs.TryGetValue(input.SourceKey, out var prior))
        {
            if (input.SourceSequence < prior.SourceSequence) return new VoiceActivityPromotionResultV1.Stale(State);
            if (input.SourceSequence == prior.SourceSequence)
                return input == prior
                    ? new VoiceActivityPromotionResultV1.Duplicate(State)
                    : Reject("source-sequence-contradiction");
        }
        if (_facts.Count >= _maximumFacts) return Reject("promotion-history-exhausted");
        _lastInputs[input.SourceKey] = input;

        if (input.Edge == VoiceActivityPromotionEdgeV1.Discontinuity)
            return Discontinue(input.SourceKey, "explicit-discontinuity");
        if (!EdgeAllowed(input)) return Reject("promotion-edge-invalid");
        if (input.Edge == VoiceActivityPromotionEdgeV1.Correction)
            return Correct(input);
        if (input.Outcome is not VoiceActivitySourceOutcomeV1.Observed observed)
            return ApplyNonObservation(input.SourceKey, input.Outcome!);
        if (!TryActivity(input, observed, out var active)) return Reject("measurement-promotion-invalid");
        if (!AcceptExtent(observed.Extent)) return Discontinue(input.SourceKey, "mixed-graph-generation");
        _active[input.SourceKey] = active;
        return Advance(input.SourceKey, observed);
    }

    private VoiceActivityPromotionResultV1 Advance(string sourceKey, VoiceActivitySourceOutcomeV1.Observed observed)
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
                State = VoiceActivityPromotionStateV1.Open;
                Extend(extent);
                return Emit(VoiceActivityPromotionFactKindV1.Continued, contributors, "activity-continued",
                    observedAt: observed.ObservedAt, processedAt: observed.ProcessedAt);
            }
            _candidateStart ??= extent.StartInclusive;
            _extentEnd = Math.Max(_extentEnd ?? extent.EndExclusive, extent.EndExclusive);
            _extentExact &= extent.Exact;
            _openCount++;
            if (_openCount < _openThreshold)
            {
                State = VoiceActivityPromotionStateV1.CandidateOpen;
                return Applied();
            }
            _stableOpen = true;
            _extentStart = _candidateStart;
            _candidateStart = null;
            _openCount = 0;
            State = VoiceActivityPromotionStateV1.Open;
            return Emit(VoiceActivityPromotionFactKindV1.Opened, contributors, "activity-opened",
                observedAt: observed.ObservedAt, processedAt: observed.ProcessedAt);
        }

        _openCount = 0;
        if (!_stableOpen)
        {
            if (State == VoiceActivityPromotionStateV1.CandidateOpen)
            {
                State = VoiceActivityPromotionStateV1.Closed;
                _candidateStart = null;
                _extentEnd = null;
                _extentExact = true;
                return Emit(VoiceActivityPromotionFactKindV1.FalseStart, [sourceKey], "activity-false-start",
                    observedAt: observed.ObservedAt, processedAt: observed.ProcessedAt);
            }
            State = VoiceActivityPromotionStateV1.Closed;
            return Applied();
        }
        Extend(extent);
        _closeCount++;
        if (_closeCount < _closeThreshold)
        {
            State = VoiceActivityPromotionStateV1.CandidateClose;
            return Applied();
        }
        _stableOpen = false;
        _closeCount = 0;
        State = VoiceActivityPromotionStateV1.Closed;
        var result = Emit(VoiceActivityPromotionFactKindV1.Closed, [sourceKey], "activity-closed",
            observedAt: observed.ObservedAt, processedAt: observed.ProcessedAt);
        _extentStart = null;
        _extentEnd = null;
        return result;
    }

    private VoiceActivityPromotionResultV1 Correct(VoiceActivityPromotionInputV1 input)
    {
        if (_maximumCorrections == 0 || _correctionCount >= _maximumCorrections)
            return Reject("correction-history-exhausted");
        var target = _facts.LastOrDefault(row => row.PromotionSequence == input.CorrectsPromotionSequence);
        if (target is null || target != _facts.Last()) return Reject("correction-target-stale");
        if (input.Outcome is not VoiceActivitySourceOutcomeV1.Observed observed ||
            !TryActivity(input, observed, out var active) || !AcceptExtent(observed.Extent))
            return Reject("correction-evidence-invalid");
        _correctionCount++;
        _active[input.SourceKey] = active;
        _stableOpen = active;
        State = active ? VoiceActivityPromotionStateV1.Open : VoiceActivityPromotionStateV1.Closed;
        _extentStart = active ? observed.Extent.StartInclusive : null;
        _extentEnd = active ? observed.Extent.EndExclusive : null;
        _extentExact = observed.Extent.Exact;
        return Emit(VoiceActivityPromotionFactKindV1.Corrected, [input.SourceKey],
            active ? "activity-corrected-open" : "activity-corrected-closed", target.PromotionSequence,
            observed.ObservedAt, observed.ProcessedAt);
    }

    private VoiceActivityPromotionResultV1 ApplyNonObservation(string sourceKey, VoiceActivitySourceOutcomeV1 outcome)
    {
        _active.Remove(sourceKey);
        return outcome switch
        {
            VoiceActivitySourceOutcomeV1.NoObservation no when no.Reason is
                VoiceActivityNoObservationReasonV1.Reset or VoiceActivityNoObservationReasonV1.Teardown or
                VoiceActivityNoObservationReasonV1.SourceRevoked => Discontinue(sourceKey, "source-discontinuous"),
            VoiceActivitySourceOutcomeV1.InvalidInput => Discontinue(sourceKey, "input-discontinuous"),
            VoiceActivitySourceOutcomeV1.Fault => SetExceptional(sourceKey,
                VoiceActivityPromotionStateV1.Faulted, VoiceActivityPromotionFactKindV1.Faulted, "source-faulted"),
            _ => SetExceptional(sourceKey, VoiceActivityPromotionStateV1.Unobservable,
                VoiceActivityPromotionFactKindV1.Unobservable, "source-unobservable"),
        };
    }

    private VoiceActivityPromotionResultV1 Discontinue(string sourceKey, string reason)
    {
        var hadOpen = _stableOpen;
        _stableOpen = false;
        _openCount = _closeCount = 0;
        _candidateStart = _extentStart = _extentEnd = null;
        _active.Clear();
        _graphGeneration = null;
        _extentExact = true;
        State = VoiceActivityPromotionStateV1.Discontinuous;
        return Emit(VoiceActivityPromotionFactKindV1.Discontinuous, [sourceKey],
            hadOpen ? $"{reason}-open-abandoned" : reason);
    }

    private VoiceActivityPromotionResultV1 SetExceptional(string sourceKey, VoiceActivityPromotionStateV1 state,
        VoiceActivityPromotionFactKindV1 kind, string reason)
    {
        State = state;
        return Emit(kind, [sourceKey], reason);
    }

    private bool EdgeAllowed(VoiceActivityPromotionInputV1 input)
    {
        var kind = _plan.Sources.Single(row => row.Request.SourceKey == input.SourceKey).Request.Kind;
        return input.Edge switch
        {
            VoiceActivityPromotionEdgeV1.Observation or VoiceActivityPromotionEdgeV1.Correction => true,
            VoiceActivityPromotionEdgeV1.ManualPress or VoiceActivityPromotionEdgeV1.ManualContinue or
                VoiceActivityPromotionEdgeV1.ManualRelease => kind == ActivitySourceKindV1.Manual &&
                    _plan.PromotionAuthority.Mode == VoiceActivityPromotionModeV1.Manual,
            _ => false,
        };
    }

    private static bool TryActivity(VoiceActivityPromotionInputV1 input,
        VoiceActivitySourceOutcomeV1.Observed observed, out bool active)
    {
        if (input.Edge == VoiceActivityPromotionEdgeV1.ManualPress ||
            input.Edge == VoiceActivityPromotionEdgeV1.ManualContinue) { active = true; return true; }
        if (input.Edge == VoiceActivityPromotionEdgeV1.ManualRelease) { active = false; return true; }
        switch (observed.Measurement)
        {
            case VoiceActivityMeasurementV1.Numeric numeric:
                active = numeric.Value >= observed.Descriptor.Minimum +
                    (observed.Descriptor.Maximum - observed.Descriptor.Minimum) / 2d;
                return true;
            case VoiceActivityMeasurementV1.Binary binary:
                active = binary.Value;
                return true;
            case VoiceActivityMeasurementV1.Category category:
                var value = category.Value.ToString();
                if (value is "speech" or "active" or "open" or "pressed") { active = true; return true; }
                if (value is "silence" or "inactive" or "closed" or "released") { active = false; return true; }
                break;
        }
        active = false;
        return false;
    }

    private bool AcceptExtent(VoiceActivityMediaExtentV1 extent)
    {
        if (_graphGeneration is { } graph && graph != extent.GraphGeneration) return false;
        _graphGeneration ??= extent.GraphGeneration;
        return true;
    }

    private void Extend(VoiceActivityMediaExtentV1 extent)
    {
        _extentStart = Math.Min(_extentStart ?? extent.StartInclusive, extent.StartInclusive);
        _extentEnd = Math.Max(_extentEnd ?? extent.EndExclusive, extent.EndExclusive);
        _extentExact &= extent.Exact;
    }

    private VoiceActivityPromotionResultV1 Emit(VoiceActivityPromotionFactKindV1 kind,
        IReadOnlyList<string> contributors, string reason, ulong corrects = 0,
        MonotonicStampV1? observedAt = null, MonotonicStampV1? processedAt = null)
    {
        VoiceActivityMediaExtentV1? extent = _graphGeneration.HasValue && _extentStart.HasValue && _extentEnd.HasValue
            ? new VoiceActivityMediaExtentV1(_graphGeneration.Value, _extentStart.Value, _extentEnd.Value, _extentExact)
            : null;
        var fact = new VoiceActivityPromotionFactV1(_plan.PlanGeneration, _plan.ConfigRevision, _session, _direction,
            ++_promotionSequence, kind, State, extent, contributors, reason, observedAt, processedAt, corrects);
        _facts.Add(fact);
        return new VoiceActivityPromotionResultV1.Applied(State, fact);
    }

    private VoiceActivityPromotionResultV1 Applied() => new VoiceActivityPromotionResultV1.Applied(State, null);
    private VoiceActivityPromotionResultV1 Rejected(string code) =>
        new VoiceActivityPromotionResultV1.Rejected(State, code);
    private VoiceActivityPromotionResultV1 Reject(string code) => Rejected(code);
}

internal enum VoiceActivityCriticalEvidenceAppendResultV1 : byte
{
    Appended = 1,
    Duplicate = 2,
    Stale = 3,
    CapacityExceeded = 4,
    Contradictory = 5,
}

internal sealed class VoiceActivityCriticalEvidenceBufferV1
{
    private readonly VoiceActivityPromotionFactV1[] _facts;
    private int _count;

    internal VoiceActivityCriticalEvidenceBufferV1(ulong planGeneration, ulong configRevision,
        SessionAuthorityStampV1 session, GraphDirectionV1 direction, int capacity)
    {
        if (planGeneration == 0 || configRevision == 0 || !session.IsValid) throw new ArgumentOutOfRangeException();
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (capacity is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(capacity));
        PlanGeneration = planGeneration;
        ConfigRevision = configRevision;
        Session = session;
        Direction = direction;
        _facts = new VoiceActivityPromotionFactV1[capacity];
    }

    internal ulong PlanGeneration { get; }
    internal ulong ConfigRevision { get; }
    internal SessionAuthorityStampV1 Session { get; }
    internal GraphDirectionV1 Direction { get; }
    internal IReadOnlyList<VoiceActivityPromotionFactV1> Snapshot =>
        Array.AsReadOnly(_facts.Take(_count).ToArray());

    internal VoiceActivityCriticalEvidenceAppendResultV1 Append(VoiceActivityPromotionFactV1 fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.PlanGeneration != PlanGeneration || fact.ConfigRevision != ConfigRevision ||
            fact.Session != Session || fact.Direction != Direction)
            return VoiceActivityCriticalEvidenceAppendResultV1.Stale;
        if (_count > 0 && fact.PromotionSequence <= _facts[_count - 1].PromotionSequence)
            return fact.PromotionSequence < _facts[_count - 1].PromotionSequence
                ? VoiceActivityCriticalEvidenceAppendResultV1.Stale
                : fact == _facts[_count - 1]
                    ? VoiceActivityCriticalEvidenceAppendResultV1.Duplicate
                    : VoiceActivityCriticalEvidenceAppendResultV1.Contradictory;
        if (_count == _facts.Length) return VoiceActivityCriticalEvidenceAppendResultV1.CapacityExceeded;
        _facts[_count++] = fact;
        return VoiceActivityCriticalEvidenceAppendResultV1.Appended;
    }
}
