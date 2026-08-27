using System.Collections.ObjectModel;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivityDetailedUpdateDispositionV1 : byte
{
    Applied = 1,
    Staged = 2,
    RequiresReplacement = 3,
    PartiallyApplied = 4,
    Rejected = 5,
    Superseded = 6,
    RejectedByFence = 7,
    CompletedBeforeCommit = 8,
}

internal enum VoiceActivityUpdateFieldOwnerV1 : byte
{
    SessionPolicy = 1,
    PlanCompiler = 2,
    Source = 3,
    Provider = 4,
}

internal enum VoiceActivityUpdateFieldV1 : byte
{
    Profile = 1,
    Responsiveness = 2,
    NoiseEnvironment = 3,
    SpeechContinuity = 4,
    PrefixContext = 5,
    Sources = 6,
    Degradation = 7,
    Limits = 8,
    Calibration = 9,
    Authority = 10,
    ProviderConfiguration = 11,
}

internal enum VoiceActivityUpdateFieldDispositionV1 : byte
{
    Applied = 1,
    Staged = 2,
    RequiresReplacement = 3,
    Rejected = 4,
    RequestedUnconfirmed = 5,
    Superseded = 6,
}

internal enum VoiceActivityCurrentSpeechHandlingV1 : byte
{
    ContinueUnderOldUntilCut = 1,
    ResetAtCut = 2,
    MarkDiscontinuousAtCut = 3,
    TransferWithContinuityProof = 4,
}

internal sealed record VoiceActivityUpdateFieldResultV1
{
    internal VoiceActivityUpdateFieldResultV1(VoiceActivityUpdateFieldV1 field, VoiceActivityUpdateFieldOwnerV1 owner,
        string? sourceKey, string requestedValue, string effectiveValue, VoiceActivityUpdateFieldDispositionV1 disposition,
        string reason)
    {
        if (!Enum.IsDefined(field) || !Enum.IsDefined(owner) || !Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException();
        Field = field;
        if (owner is VoiceActivityUpdateFieldOwnerV1.Source or VoiceActivityUpdateFieldOwnerV1.Provider)
            SourceKey = ActivitySourceRequestV1.RequireAscii(sourceKey ?? throw new ArgumentNullException(nameof(sourceKey)),
                nameof(sourceKey));
        else if (sourceKey is not null)
            throw new ArgumentException("Only source/provider-owned fields may name a source.", nameof(sourceKey));
        RequestedValue = ActivitySourceRequestV1.RequireAscii(requestedValue, nameof(requestedValue));
        EffectiveValue = ActivitySourceRequestV1.RequireAscii(effectiveValue, nameof(effectiveValue));
        Reason = ActivitySourceRequestV1.RequireAscii(reason, nameof(reason));
        Owner = owner;
        Disposition = disposition;
    }

    internal VoiceActivityUpdateFieldV1 Field { get; }
    internal VoiceActivityUpdateFieldOwnerV1 Owner { get; }
    internal string? SourceKey { get; }
    internal string RequestedValue { get; }
    internal string EffectiveValue { get; }
    internal VoiceActivityUpdateFieldDispositionV1 Disposition { get; }
    internal string Reason { get; }
}

internal sealed record VoiceActivityUpdateTransactionResultV1
{
    private readonly VoiceActivityUpdateFieldResultV1[] _fields;
    private readonly string[] _warnings;

    internal VoiceActivityUpdateTransactionResultV1(OperationId operationId,
        VoiceActivityDetailedUpdateDispositionV1 disposition, ulong oldRevision, ulong newRevision,
        ulong oldPlanGeneration, ulong newPlanGeneration, MonotonicStampV1 exactCut,
        VoiceActivityCurrentSpeechHandlingV1 currentSpeechHandling, ProviderActivityVisibilityV1 providerVisibility,
        bool degraded, bool rolledBack, IReadOnlyList<VoiceActivityUpdateFieldResultV1> fields,
        IReadOnlyList<string> warnings)
    {
        if (!operationId.IsValid || !Enum.IsDefined(disposition) || oldRevision == 0 || newRevision == 0 ||
            oldPlanGeneration == 0 || newPlanGeneration == 0 || !exactCut.IsValid ||
            !Enum.IsDefined(currentSpeechHandling) || !Enum.IsDefined(providerVisibility))
            throw new ArgumentException("Update result authority is invalid.");
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(warnings);
        _fields = fields.ToArray();
        _warnings = warnings.Select(static value => ActivitySourceRequestV1.RequireAscii(value, nameof(warnings))).ToArray();
        if (_fields.Length == 0 || _fields.Select(static item => item.Field).Distinct().Count() != _fields.Length)
            throw new ArgumentException("Update fields must be nonempty and unique.", nameof(fields));
        OperationId = operationId;
        Disposition = disposition;
        OldRevision = oldRevision;
        NewRevision = newRevision;
        OldPlanGeneration = oldPlanGeneration;
        NewPlanGeneration = newPlanGeneration;
        ExactCut = exactCut;
        CurrentSpeechHandling = currentSpeechHandling;
        ProviderVisibility = providerVisibility;
        Degraded = degraded;
        RolledBack = rolledBack;
        Fields = new ReadOnlyCollection<VoiceActivityUpdateFieldResultV1>(_fields);
        Warnings = Array.AsReadOnly(_warnings);
    }

    internal OperationId OperationId { get; }
    internal VoiceActivityDetailedUpdateDispositionV1 Disposition { get; }
    internal ulong OldRevision { get; }
    internal ulong NewRevision { get; }
    internal ulong OldPlanGeneration { get; }
    internal ulong NewPlanGeneration { get; }
    internal MonotonicStampV1 ExactCut { get; }
    internal VoiceActivityCurrentSpeechHandlingV1 CurrentSpeechHandling { get; }
    internal ProviderActivityVisibilityV1 ProviderVisibility { get; }
    internal bool Degraded { get; }
    internal bool RolledBack { get; }
    internal IReadOnlyList<VoiceActivityUpdateFieldResultV1> Fields { get; }
    internal IReadOnlyList<string> Warnings { get; }
}

internal sealed class VoiceActivityUpdateSequencerV1
{
    private readonly object _gate = new();
    private readonly VoiceActivityLifecycleV1 _lifecycle;
    private readonly int _maximumTerminalHistory;
    private readonly Dictionary<OperationId, VoiceActivityUpdateTransactionResultV1> _terminal = [];
    private readonly Dictionary<OperationId, Intent> _intents = [];
    private readonly Queue<OperationId> _terminalOrder = [];
    private Pending? _pending;

    internal VoiceActivityUpdateSequencerV1(VoiceActivityLifecycleV1 lifecycle, int maximumTerminalHistory = 64)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        if (maximumTerminalHistory is < 1 or > 4_096)
            throw new ArgumentOutOfRangeException(nameof(maximumTerminalHistory));
        _maximumTerminalHistory = maximumTerminalHistory;
    }

    internal int TerminalHistoryCount { get { lock (_gate) return _terminal.Count; } }

    internal VoiceActivityUpdateTransactionResultV1 Stage(OperationId operationId,
        ulong expectedLifecycleRevision, VoiceActivityEffectivePlanV1 candidatePlan,
        MonotonicStampV1 exactCut, IReadOnlyList<VoiceActivityUpdateFieldResultV1> fields,
        VoiceActivityCurrentSpeechHandlingV1 currentSpeechHandling, ProviderActivityVisibilityV1 providerVisibility)
    {
        if (!operationId.IsValid || expectedLifecycleRevision == 0 || !exactCut.IsValid)
            throw new ArgumentException("Update stage authority is invalid.");
        ArgumentNullException.ThrowIfNull(candidatePlan);
        ArgumentNullException.ThrowIfNull(fields);
        lock (_gate)
        {
            var intent = new Intent(candidatePlan, fields.ToArray(), currentSpeechHandling, providerVisibility);
            if (_terminal.TryGetValue(operationId, out var terminal))
                return _intents.TryGetValue(operationId, out var prior) && IntentMatches(prior, intent)
                    ? terminal
                    : Result(operationId, VoiceActivityDetailedUpdateDispositionV1.Rejected, _lifecycle.Current,
                        candidatePlan, exactCut,
                        fields.Select(static field => new VoiceActivityUpdateFieldResultV1(field.Field, field.Owner,
                            field.SourceKey,
                            field.RequestedValue, field.EffectiveValue,
                            VoiceActivityUpdateFieldDispositionV1.Rejected, "operation-intent-contradiction")).ToArray(),
                        currentSpeechHandling, providerVisibility, false, true,
                        ["update-operation-contradiction"]);
            _intents[operationId] = intent;
            var current = _lifecycle.Current;
            if (current.State == VoiceActivityLifecycleStateV1.Completed)
                return Terminal(operationId, VoiceActivityDetailedUpdateDispositionV1.CompletedBeforeCommit,
                    current, candidatePlan, exactCut, Supersede(fields), currentSpeechHandling,
                    providerVisibility, false, true, ["update-completed-before-commit"]);
            if (current.LifecycleRevision != expectedLifecycleRevision)
                return Terminal(operationId, VoiceActivityDetailedUpdateDispositionV1.RejectedByFence,
                    current, candidatePlan, exactCut, Supersede(fields), currentSpeechHandling,
                    providerVisibility, false, true, ["update-rejected-by-fence"]);
            if (_pending is not null)
            {
                if (_pending.OperationId == operationId && _pending.CandidatePlan == candidatePlan &&
                    _pending.Fields.SequenceEqual(fields)) return _pending.Staged;
                return Terminal(operationId, VoiceActivityDetailedUpdateDispositionV1.Superseded,
                    current, candidatePlan, exactCut, Supersede(fields), currentSpeechHandling,
                    providerVisibility, false, true, ["update-superseded"]);
            }

            var normalizedFields = NormalizeFieldClaims(current.Plan, fields);
            var requested = Classify(normalizedFields);
            if (requested == VoiceActivityDetailedUpdateDispositionV1.Rejected)
                return Terminal(operationId, requested, current, candidatePlan, exactCut, normalizedFields,
                    currentSpeechHandling, providerVisibility, false, true, ["update-field-rejected"]);
            var stagedFields = normalizedFields.Select(static field => field.Disposition == VoiceActivityUpdateFieldDispositionV1.Applied
                ? new VoiceActivityUpdateFieldResultV1(field.Field, field.Owner, field.SourceKey, field.RequestedValue,
                    field.EffectiveValue, VoiceActivityUpdateFieldDispositionV1.Staged, "awaiting-ordered-cut")
                : field).ToArray();
            var staged = Result(operationId, VoiceActivityDetailedUpdateDispositionV1.Staged, current,
                candidatePlan, exactCut, stagedFields, currentSpeechHandling, providerVisibility, false, false, []);
            _pending = new Pending(operationId, expectedLifecycleRevision, candidatePlan, normalizedFields, staged,
                exactCut, currentSpeechHandling, providerVisibility);
            return staged;
        }
    }

    internal VoiceActivityUpdateTransactionResultV1 Commit(OperationId operationId, MonotonicStampV1 exactCut)
    {
        if (!operationId.IsValid || !exactCut.IsValid) throw new ArgumentException("Update commit authority is invalid.");
        lock (_gate)
        {
            if (_terminal.TryGetValue(operationId, out var terminal)) return terminal;
            var current = _lifecycle.Current;
            if (_pending is null || _pending.OperationId != operationId)
                return Terminal(operationId, VoiceActivityDetailedUpdateDispositionV1.Superseded, current,
                    current.Plan, exactCut, [RejectedField(VoiceActivityUpdateFieldV1.Authority, "no-current-stage")],
                    VoiceActivityCurrentSpeechHandlingV1.ResetAtCut,
                    ProviderActivityVisibilityV1.Unknown, false, true, ["update-stage-superseded"]);
            var pending = _pending;
            _pending = null;
            if (exactCut.CompareTo(pending.StagedAt) is ClockComparison.Earlier or ClockComparison.Incomparable)
                return Terminal(operationId, VoiceActivityDetailedUpdateDispositionV1.Rejected,
                    current, pending.CandidatePlan, exactCut, Supersede(pending.Fields),
                    pending.CurrentSpeechHandling, pending.ProviderVisibility, false, true,
                    ["update-cut-invalid"]);
            if (current.State == VoiceActivityLifecycleStateV1.Completed)
                return Terminal(operationId, VoiceActivityDetailedUpdateDispositionV1.CompletedBeforeCommit,
                    current, pending.CandidatePlan, exactCut, Supersede(pending.Fields),
                    pending.CurrentSpeechHandling, pending.ProviderVisibility, false, true,
                    ["update-completed-before-commit"]);
            if (current.LifecycleRevision != pending.ExpectedLifecycleRevision)
                return Terminal(operationId, VoiceActivityDetailedUpdateDispositionV1.RejectedByFence,
                    current, pending.CandidatePlan, exactCut, Supersede(pending.Fields),
                    pending.CurrentSpeechHandling, pending.ProviderVisibility, false, true,
                    ["update-rejected-by-fence"]);
            var disposition = Classify(pending.Fields);
            if (disposition is VoiceActivityDetailedUpdateDispositionV1.Applied or
                VoiceActivityDetailedUpdateDispositionV1.PartiallyApplied)
            {
                var applied = _lifecycle.ApplyInPlaceUpdate(pending.ExpectedLifecycleRevision,
                    pending.CandidatePlan);
                if (applied is null)
                    return Terminal(operationId, VoiceActivityDetailedUpdateDispositionV1.RejectedByFence,
                        current, pending.CandidatePlan, exactCut, Supersede(pending.Fields),
                        pending.CurrentSpeechHandling, pending.ProviderVisibility, false, true,
                        ["update-rejected-by-fence"]);
                var result = new VoiceActivityUpdateTransactionResultV1(operationId, disposition,
                    current.LifecycleRevision, applied.LifecycleRevision, current.Plan.PlanGeneration,
                    applied.Plan.PlanGeneration, exactCut, pending.CurrentSpeechHandling,
                    pending.ProviderVisibility,
                    disposition == VoiceActivityDetailedUpdateDispositionV1.PartiallyApplied, false,
                    pending.Fields, []);
                StoreTerminal(operationId, result);
                return result;
            }
            return Terminal(operationId, disposition, current, pending.CandidatePlan, exactCut, pending.Fields,
                pending.CurrentSpeechHandling, pending.ProviderVisibility,
                disposition == VoiceActivityDetailedUpdateDispositionV1.PartiallyApplied, false,
                disposition == VoiceActivityDetailedUpdateDispositionV1.RequiresReplacement
                    ? ["update-requires-replacement"] : []);
        }
    }

    private static VoiceActivityDetailedUpdateDispositionV1 Classify(IReadOnlyList<VoiceActivityUpdateFieldResultV1> fields)
    {
        if (fields.Any(static item => item.Disposition == VoiceActivityUpdateFieldDispositionV1.Rejected))
            return VoiceActivityDetailedUpdateDispositionV1.Rejected;
        var applied = fields.Any(static item => item.Disposition == VoiceActivityUpdateFieldDispositionV1.Applied);
        var uncertain = fields.Any(static item => item.Disposition is VoiceActivityUpdateFieldDispositionV1.Staged or
            VoiceActivityUpdateFieldDispositionV1.RequestedUnconfirmed);
        var replacement = fields.Any(static item => item.Disposition == VoiceActivityUpdateFieldDispositionV1.RequiresReplacement);
        if (applied && (uncertain || replacement)) return VoiceActivityDetailedUpdateDispositionV1.PartiallyApplied;
        if (replacement) return VoiceActivityDetailedUpdateDispositionV1.RequiresReplacement;
        if (uncertain) return VoiceActivityDetailedUpdateDispositionV1.Staged;
        return VoiceActivityDetailedUpdateDispositionV1.Applied;
    }

    private static VoiceActivityUpdateFieldResultV1[] NormalizeFieldClaims(VoiceActivityEffectivePlanV1 currentPlan,
        IReadOnlyList<VoiceActivityUpdateFieldResultV1> fields)
    {
        return fields.Select(field =>
        {
            var structural = field.Field is VoiceActivityUpdateFieldV1.Profile or
                VoiceActivityUpdateFieldV1.PrefixContext or VoiceActivityUpdateFieldV1.Sources or
                VoiceActivityUpdateFieldV1.Limits or VoiceActivityUpdateFieldV1.Calibration or
                VoiceActivityUpdateFieldV1.Authority;
            var claimsInPlace = field.Disposition is VoiceActivityUpdateFieldDispositionV1.Applied or
                VoiceActivityUpdateFieldDispositionV1.RequestedUnconfirmed;
            var target = field.SourceKey is null ? null : currentPlan.Sources.SingleOrDefault(source =>
                source.Request.SourceKey == field.SourceKey);
            var supportsSequencedUpdate = field.Owner is VoiceActivityUpdateFieldOwnerV1.SessionPolicy or
                VoiceActivityUpdateFieldOwnerV1.PlanCompiler || target is not null &&
                target.Capabilities.DynamicUpdate == VoiceActivitySourceControlV1.Sequenced &&
                (field.Owner != VoiceActivityUpdateFieldOwnerV1.Provider ||
                    target.Request.Kind == ActivitySourceKindV1.ProviderNative);
            if (claimsInPlace && (structural || !supportsSequencedUpdate))
                return new VoiceActivityUpdateFieldResultV1(field.Field, field.Owner, field.SourceKey,
                    field.RequestedValue,
                    field.EffectiveValue, VoiceActivityUpdateFieldDispositionV1.Rejected,
                    structural ? "field-requires-replacement" : "sequenced-update-unsupported");
            return field;
        }).ToArray();
    }

    private VoiceActivityUpdateTransactionResultV1 Terminal(OperationId operationId,
        VoiceActivityDetailedUpdateDispositionV1 disposition, VoiceActivityLifecycleSnapshotV1 current,
        VoiceActivityEffectivePlanV1 candidate, MonotonicStampV1 cut,
        IReadOnlyList<VoiceActivityUpdateFieldResultV1> fields, VoiceActivityCurrentSpeechHandlingV1 currentSpeechHandling,
        ProviderActivityVisibilityV1 visibility, bool degraded, bool rolledBack, IReadOnlyList<string> warnings)
    {
        var result = Result(operationId, disposition, current, candidate, cut, fields, currentSpeechHandling,
            visibility, degraded, rolledBack, warnings);
        StoreTerminal(operationId, result);
        return result;
    }

    private void StoreTerminal(OperationId operationId, VoiceActivityUpdateTransactionResultV1 result)
    {
        if (!_terminal.ContainsKey(operationId)) _terminalOrder.Enqueue(operationId);
        _terminal[operationId] = result;
        while (_terminalOrder.Count > _maximumTerminalHistory)
        {
            var evicted = _terminalOrder.Dequeue();
            _terminal.Remove(evicted);
            _intents.Remove(evicted);
        }
    }

    private static VoiceActivityUpdateTransactionResultV1 Result(OperationId operationId,
        VoiceActivityDetailedUpdateDispositionV1 disposition, VoiceActivityLifecycleSnapshotV1 current,
        VoiceActivityEffectivePlanV1 candidate, MonotonicStampV1 cut,
        IReadOnlyList<VoiceActivityUpdateFieldResultV1> fields, VoiceActivityCurrentSpeechHandlingV1 currentSpeechHandling,
        ProviderActivityVisibilityV1 visibility, bool degraded, bool rolledBack, IReadOnlyList<string> warnings) =>
        new(operationId, disposition, current.LifecycleRevision,
            disposition is VoiceActivityDetailedUpdateDispositionV1.Applied or
                VoiceActivityDetailedUpdateDispositionV1.PartiallyApplied ? checked(current.LifecycleRevision + 1) : current.LifecycleRevision,
            current.Plan.PlanGeneration, candidate.PlanGeneration, cut, currentSpeechHandling, visibility,
            degraded, rolledBack, fields, warnings);

    private static VoiceActivityUpdateFieldResultV1[] Supersede(IReadOnlyList<VoiceActivityUpdateFieldResultV1> fields) =>
        fields.Select(static field => new VoiceActivityUpdateFieldResultV1(field.Field, field.Owner, field.SourceKey,
            field.RequestedValue, field.EffectiveValue, VoiceActivityUpdateFieldDispositionV1.Superseded,
            "transaction-not-current")).ToArray();

    private static VoiceActivityUpdateFieldResultV1 RejectedField(VoiceActivityUpdateFieldV1 field, string reason) =>
        new(field, VoiceActivityUpdateFieldOwnerV1.SessionPolicy, null, "unknown", "unchanged",
            VoiceActivityUpdateFieldDispositionV1.Rejected, reason);

    private static bool IntentMatches(Intent left, Intent right) =>
        left.CandidatePlan == right.CandidatePlan && left.CurrentSpeechHandling == right.CurrentSpeechHandling &&
        left.ProviderVisibility == right.ProviderVisibility && left.Fields.SequenceEqual(right.Fields);

    private sealed record Pending(OperationId OperationId, ulong ExpectedLifecycleRevision,
        VoiceActivityEffectivePlanV1 CandidatePlan, VoiceActivityUpdateFieldResultV1[] Fields,
        VoiceActivityUpdateTransactionResultV1 Staged, MonotonicStampV1 StagedAt,
        VoiceActivityCurrentSpeechHandlingV1 CurrentSpeechHandling,
        ProviderActivityVisibilityV1 ProviderVisibility);
    private sealed record Intent(VoiceActivityEffectivePlanV1 CandidatePlan,
        VoiceActivityUpdateFieldResultV1[] Fields, VoiceActivityCurrentSpeechHandlingV1 CurrentSpeechHandling,
        ProviderActivityVisibilityV1 ProviderVisibility);
}
