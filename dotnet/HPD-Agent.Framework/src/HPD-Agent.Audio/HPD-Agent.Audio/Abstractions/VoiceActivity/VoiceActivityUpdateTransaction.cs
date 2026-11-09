using System.Collections.ObjectModel;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal enum VoiceActivityDetailedUpdateDisposition : byte
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

internal enum VoiceActivityUpdateFieldOwner : byte
{
    SessionPolicy = 1,
    PlanCompiler = 2,
    Source = 3,
    Provider = 4,
}

internal enum VoiceActivityUpdateField : byte
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

internal enum VoiceActivityUpdateFieldDisposition : byte
{
    Applied = 1,
    Staged = 2,
    RequiresReplacement = 3,
    Rejected = 4,
    RequestedUnconfirmed = 5,
    Superseded = 6,
}

internal enum VoiceActivityCurrentSpeechHandling : byte
{
    ContinueUnderOldUntilCut = 1,
    ResetAtCut = 2,
    MarkDiscontinuousAtCut = 3,
    TransferWithContinuityProof = 4,
}

internal sealed record VoiceActivityUpdateFieldResult
{
    internal VoiceActivityUpdateFieldResult(VoiceActivityUpdateField field, VoiceActivityUpdateFieldOwner owner,
        string? sourceKey, string requestedValue, string effectiveValue, VoiceActivityUpdateFieldDisposition disposition,
        string reason)
    {
        if (!Enum.IsDefined(field) || !Enum.IsDefined(owner) || !Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException();
        Field = field;
        if (owner is VoiceActivityUpdateFieldOwner.Source or VoiceActivityUpdateFieldOwner.Provider)
            SourceKey = ActivitySourceRequest.RequireAscii(sourceKey ?? throw new ArgumentNullException(nameof(sourceKey)),
                nameof(sourceKey));
        else if (sourceKey is not null)
            throw new ArgumentException("Only source/provider-owned fields may name a source.", nameof(sourceKey));
        RequestedValue = ActivitySourceRequest.RequireAscii(requestedValue, nameof(requestedValue));
        EffectiveValue = ActivitySourceRequest.RequireAscii(effectiveValue, nameof(effectiveValue));
        Reason = ActivitySourceRequest.RequireAscii(reason, nameof(reason));
        Owner = owner;
        Disposition = disposition;
    }

    internal VoiceActivityUpdateField Field { get; }
    internal VoiceActivityUpdateFieldOwner Owner { get; }
    internal string? SourceKey { get; }
    internal string RequestedValue { get; }
    internal string EffectiveValue { get; }
    internal VoiceActivityUpdateFieldDisposition Disposition { get; }
    internal string Reason { get; }
}

internal sealed record VoiceActivityUpdateTransactionResult
{
    private readonly VoiceActivityUpdateFieldResult[] _fields;
    private readonly string[] _warnings;

    internal VoiceActivityUpdateTransactionResult(OperationId operationId,
        VoiceActivityDetailedUpdateDisposition disposition, ulong oldRevision, ulong newRevision,
        ulong oldPlanGeneration, ulong newPlanGeneration, MonotonicStampV1 exactCut,
        VoiceActivityCurrentSpeechHandling currentSpeechHandling, ProviderActivityVisibility providerVisibility,
        bool degraded, bool rolledBack, IReadOnlyList<VoiceActivityUpdateFieldResult> fields,
        IReadOnlyList<string> warnings)
    {
        if (!operationId.IsValid || !Enum.IsDefined(disposition) || oldRevision == 0 || newRevision == 0 ||
            oldPlanGeneration == 0 || newPlanGeneration == 0 || !exactCut.IsValid ||
            !Enum.IsDefined(currentSpeechHandling) || !Enum.IsDefined(providerVisibility))
            throw new ArgumentException("Update result authority is invalid.");
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(warnings);
        _fields = fields.ToArray();
        _warnings = warnings.Select(static value => ActivitySourceRequest.RequireAscii(value, nameof(warnings))).ToArray();
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
        Fields = new ReadOnlyCollection<VoiceActivityUpdateFieldResult>(_fields);
        Warnings = Array.AsReadOnly(_warnings);
    }

    internal OperationId OperationId { get; }
    internal VoiceActivityDetailedUpdateDisposition Disposition { get; }
    internal ulong OldRevision { get; }
    internal ulong NewRevision { get; }
    internal ulong OldPlanGeneration { get; }
    internal ulong NewPlanGeneration { get; }
    internal MonotonicStampV1 ExactCut { get; }
    internal VoiceActivityCurrentSpeechHandling CurrentSpeechHandling { get; }
    internal ProviderActivityVisibility ProviderVisibility { get; }
    internal bool Degraded { get; }
    internal bool RolledBack { get; }
    internal IReadOnlyList<VoiceActivityUpdateFieldResult> Fields { get; }
    internal IReadOnlyList<string> Warnings { get; }
}

internal sealed class VoiceActivityUpdateSequencer
{
    private readonly object _gate = new();
    private readonly VoiceActivityLifecycle _lifecycle;
    private readonly int _maximumTerminalHistory;
    private readonly Dictionary<OperationId, VoiceActivityUpdateTransactionResult> _terminal = [];
    private readonly Dictionary<OperationId, Intent> _intents = [];
    private readonly Queue<OperationId> _terminalOrder = [];
    private Pending? _pending;

    internal VoiceActivityUpdateSequencer(VoiceActivityLifecycle lifecycle, int maximumTerminalHistory = 64)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        if (maximumTerminalHistory is < 1 or > 4_096)
            throw new ArgumentOutOfRangeException(nameof(maximumTerminalHistory));
        _maximumTerminalHistory = maximumTerminalHistory;
    }

    internal int TerminalHistoryCount { get { lock (_gate) return _terminal.Count; } }

    internal VoiceActivityUpdateTransactionResult Stage(OperationId operationId,
        ulong expectedLifecycleRevision, VoiceActivityEffectivePlan candidatePlan,
        MonotonicStampV1 exactCut, IReadOnlyList<VoiceActivityUpdateFieldResult> fields,
        VoiceActivityCurrentSpeechHandling currentSpeechHandling, ProviderActivityVisibility providerVisibility)
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
                    : Result(operationId, VoiceActivityDetailedUpdateDisposition.Rejected, _lifecycle.Current,
                        candidatePlan, exactCut,
                        fields.Select(static field => new VoiceActivityUpdateFieldResult(field.Field, field.Owner,
                            field.SourceKey,
                            field.RequestedValue, field.EffectiveValue,
                            VoiceActivityUpdateFieldDisposition.Rejected, "operation-intent-contradiction")).ToArray(),
                        currentSpeechHandling, providerVisibility, false, true,
                        ["update-operation-contradiction"]);
            _intents[operationId] = intent;
            var current = _lifecycle.Current;
            if (current.State == VoiceActivityLifecycleState.Completed)
                return Terminal(operationId, VoiceActivityDetailedUpdateDisposition.CompletedBeforeCommit,
                    current, candidatePlan, exactCut, Supersede(fields), currentSpeechHandling,
                    providerVisibility, false, true, ["update-completed-before-commit"]);
            if (current.LifecycleRevision != expectedLifecycleRevision)
                return Terminal(operationId, VoiceActivityDetailedUpdateDisposition.RejectedByFence,
                    current, candidatePlan, exactCut, Supersede(fields), currentSpeechHandling,
                    providerVisibility, false, true, ["update-rejected-by-fence"]);
            if (_pending is not null)
            {
                if (_pending.OperationId == operationId && _pending.CandidatePlan == candidatePlan &&
                    _pending.Fields.SequenceEqual(fields)) return _pending.Staged;
                return Terminal(operationId, VoiceActivityDetailedUpdateDisposition.Superseded,
                    current, candidatePlan, exactCut, Supersede(fields), currentSpeechHandling,
                    providerVisibility, false, true, ["update-superseded"]);
            }

            var normalizedFields = NormalizeFieldClaims(current.Plan, fields);
            var requested = Classify(normalizedFields);
            if (requested == VoiceActivityDetailedUpdateDisposition.Rejected)
                return Terminal(operationId, requested, current, candidatePlan, exactCut, normalizedFields,
                    currentSpeechHandling, providerVisibility, false, true, ["update-field-rejected"]);
            var stagedFields = normalizedFields.Select(static field => field.Disposition == VoiceActivityUpdateFieldDisposition.Applied
                ? new VoiceActivityUpdateFieldResult(field.Field, field.Owner, field.SourceKey, field.RequestedValue,
                    field.EffectiveValue, VoiceActivityUpdateFieldDisposition.Staged, "awaiting-ordered-cut")
                : field).ToArray();
            var staged = Result(operationId, VoiceActivityDetailedUpdateDisposition.Staged, current,
                candidatePlan, exactCut, stagedFields, currentSpeechHandling, providerVisibility, false, false, []);
            _pending = new Pending(operationId, expectedLifecycleRevision, candidatePlan, normalizedFields, staged,
                exactCut, currentSpeechHandling, providerVisibility);
            return staged;
        }
    }

    internal VoiceActivityUpdateTransactionResult Commit(OperationId operationId, MonotonicStampV1 exactCut)
    {
        if (!operationId.IsValid || !exactCut.IsValid) throw new ArgumentException("Update commit authority is invalid.");
        lock (_gate)
        {
            if (_terminal.TryGetValue(operationId, out var terminal)) return terminal;
            var current = _lifecycle.Current;
            if (_pending is null || _pending.OperationId != operationId)
                return Terminal(operationId, VoiceActivityDetailedUpdateDisposition.Superseded, current,
                    current.Plan, exactCut, [RejectedField(VoiceActivityUpdateField.Authority, "no-current-stage")],
                    VoiceActivityCurrentSpeechHandling.ResetAtCut,
                    ProviderActivityVisibility.Unknown, false, true, ["update-stage-superseded"]);
            var pending = _pending;
            _pending = null;
            if (exactCut.CompareTo(pending.StagedAt) is ClockComparison.Earlier or ClockComparison.Incomparable)
                return Terminal(operationId, VoiceActivityDetailedUpdateDisposition.Rejected,
                    current, pending.CandidatePlan, exactCut, Supersede(pending.Fields),
                    pending.CurrentSpeechHandling, pending.ProviderVisibility, false, true,
                    ["update-cut-invalid"]);
            if (current.State == VoiceActivityLifecycleState.Completed)
                return Terminal(operationId, VoiceActivityDetailedUpdateDisposition.CompletedBeforeCommit,
                    current, pending.CandidatePlan, exactCut, Supersede(pending.Fields),
                    pending.CurrentSpeechHandling, pending.ProviderVisibility, false, true,
                    ["update-completed-before-commit"]);
            if (current.LifecycleRevision != pending.ExpectedLifecycleRevision)
                return Terminal(operationId, VoiceActivityDetailedUpdateDisposition.RejectedByFence,
                    current, pending.CandidatePlan, exactCut, Supersede(pending.Fields),
                    pending.CurrentSpeechHandling, pending.ProviderVisibility, false, true,
                    ["update-rejected-by-fence"]);
            var disposition = Classify(pending.Fields);
            if (disposition is VoiceActivityDetailedUpdateDisposition.Applied or
                VoiceActivityDetailedUpdateDisposition.PartiallyApplied)
            {
                var applied = _lifecycle.ApplyInPlaceUpdate(pending.ExpectedLifecycleRevision,
                    pending.CandidatePlan);
                if (applied is null)
                    return Terminal(operationId, VoiceActivityDetailedUpdateDisposition.RejectedByFence,
                        current, pending.CandidatePlan, exactCut, Supersede(pending.Fields),
                        pending.CurrentSpeechHandling, pending.ProviderVisibility, false, true,
                        ["update-rejected-by-fence"]);
                var result = new VoiceActivityUpdateTransactionResult(operationId, disposition,
                    current.LifecycleRevision, applied.LifecycleRevision, current.Plan.PlanGeneration,
                    applied.Plan.PlanGeneration, exactCut, pending.CurrentSpeechHandling,
                    pending.ProviderVisibility,
                    disposition == VoiceActivityDetailedUpdateDisposition.PartiallyApplied, false,
                    pending.Fields, []);
                StoreTerminal(operationId, result);
                return result;
            }
            return Terminal(operationId, disposition, current, pending.CandidatePlan, exactCut, pending.Fields,
                pending.CurrentSpeechHandling, pending.ProviderVisibility,
                disposition == VoiceActivityDetailedUpdateDisposition.PartiallyApplied, false,
                disposition == VoiceActivityDetailedUpdateDisposition.RequiresReplacement
                    ? ["update-requires-replacement"] : []);
        }
    }

    private static VoiceActivityDetailedUpdateDisposition Classify(IReadOnlyList<VoiceActivityUpdateFieldResult> fields)
    {
        if (fields.Any(static item => item.Disposition == VoiceActivityUpdateFieldDisposition.Rejected))
            return VoiceActivityDetailedUpdateDisposition.Rejected;
        var applied = fields.Any(static item => item.Disposition == VoiceActivityUpdateFieldDisposition.Applied);
        var uncertain = fields.Any(static item => item.Disposition is VoiceActivityUpdateFieldDisposition.Staged or
            VoiceActivityUpdateFieldDisposition.RequestedUnconfirmed);
        var replacement = fields.Any(static item => item.Disposition == VoiceActivityUpdateFieldDisposition.RequiresReplacement);
        if (applied && (uncertain || replacement)) return VoiceActivityDetailedUpdateDisposition.PartiallyApplied;
        if (replacement) return VoiceActivityDetailedUpdateDisposition.RequiresReplacement;
        if (uncertain) return VoiceActivityDetailedUpdateDisposition.Staged;
        return VoiceActivityDetailedUpdateDisposition.Applied;
    }

    private static VoiceActivityUpdateFieldResult[] NormalizeFieldClaims(VoiceActivityEffectivePlan currentPlan,
        IReadOnlyList<VoiceActivityUpdateFieldResult> fields)
    {
        return fields.Select(field =>
        {
            var structural = field.Field is VoiceActivityUpdateField.Profile or
                VoiceActivityUpdateField.PrefixContext or VoiceActivityUpdateField.Sources or
                VoiceActivityUpdateField.Limits or VoiceActivityUpdateField.Calibration or
                VoiceActivityUpdateField.Authority;
            var claimsInPlace = field.Disposition is VoiceActivityUpdateFieldDisposition.Applied or
                VoiceActivityUpdateFieldDisposition.RequestedUnconfirmed;
            var target = field.SourceKey is null ? null : currentPlan.Sources.SingleOrDefault(source =>
                source.Request.SourceKey == field.SourceKey);
            var supportsSequencedUpdate = field.Owner is VoiceActivityUpdateFieldOwner.SessionPolicy or
                VoiceActivityUpdateFieldOwner.PlanCompiler || target is not null &&
                target.Capabilities.DynamicUpdate == VoiceActivitySourceControl.Sequenced &&
                (field.Owner != VoiceActivityUpdateFieldOwner.Provider ||
                    target.Request.Kind == ActivitySourceKind.ProviderNative);
            if (claimsInPlace && (structural || !supportsSequencedUpdate))
                return new VoiceActivityUpdateFieldResult(field.Field, field.Owner, field.SourceKey,
                    field.RequestedValue,
                    field.EffectiveValue, VoiceActivityUpdateFieldDisposition.Rejected,
                    structural ? "field-requires-replacement" : "sequenced-update-unsupported");
            return field;
        }).ToArray();
    }

    private VoiceActivityUpdateTransactionResult Terminal(OperationId operationId,
        VoiceActivityDetailedUpdateDisposition disposition, VoiceActivityLifecycleSnapshot current,
        VoiceActivityEffectivePlan candidate, MonotonicStampV1 cut,
        IReadOnlyList<VoiceActivityUpdateFieldResult> fields, VoiceActivityCurrentSpeechHandling currentSpeechHandling,
        ProviderActivityVisibility visibility, bool degraded, bool rolledBack, IReadOnlyList<string> warnings)
    {
        var result = Result(operationId, disposition, current, candidate, cut, fields, currentSpeechHandling,
            visibility, degraded, rolledBack, warnings);
        StoreTerminal(operationId, result);
        return result;
    }

    private void StoreTerminal(OperationId operationId, VoiceActivityUpdateTransactionResult result)
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

    private static VoiceActivityUpdateTransactionResult Result(OperationId operationId,
        VoiceActivityDetailedUpdateDisposition disposition, VoiceActivityLifecycleSnapshot current,
        VoiceActivityEffectivePlan candidate, MonotonicStampV1 cut,
        IReadOnlyList<VoiceActivityUpdateFieldResult> fields, VoiceActivityCurrentSpeechHandling currentSpeechHandling,
        ProviderActivityVisibility visibility, bool degraded, bool rolledBack, IReadOnlyList<string> warnings) =>
        new(operationId, disposition, current.LifecycleRevision,
            disposition is VoiceActivityDetailedUpdateDisposition.Applied or
                VoiceActivityDetailedUpdateDisposition.PartiallyApplied ? checked(current.LifecycleRevision + 1) : current.LifecycleRevision,
            current.Plan.PlanGeneration, candidate.PlanGeneration, cut, currentSpeechHandling, visibility,
            degraded, rolledBack, fields, warnings);

    private static VoiceActivityUpdateFieldResult[] Supersede(IReadOnlyList<VoiceActivityUpdateFieldResult> fields) =>
        fields.Select(static field => new VoiceActivityUpdateFieldResult(field.Field, field.Owner, field.SourceKey,
            field.RequestedValue, field.EffectiveValue, VoiceActivityUpdateFieldDisposition.Superseded,
            "transaction-not-current")).ToArray();

    private static VoiceActivityUpdateFieldResult RejectedField(VoiceActivityUpdateField field, string reason) =>
        new(field, VoiceActivityUpdateFieldOwner.SessionPolicy, null, "unknown", "unchanged",
            VoiceActivityUpdateFieldDisposition.Rejected, reason);

    private static bool IntentMatches(Intent left, Intent right) =>
        left.CandidatePlan == right.CandidatePlan && left.CurrentSpeechHandling == right.CurrentSpeechHandling &&
        left.ProviderVisibility == right.ProviderVisibility && left.Fields.SequenceEqual(right.Fields);

    private sealed record Pending(OperationId OperationId, ulong ExpectedLifecycleRevision,
        VoiceActivityEffectivePlan CandidatePlan, VoiceActivityUpdateFieldResult[] Fields,
        VoiceActivityUpdateTransactionResult Staged, MonotonicStampV1 StagedAt,
        VoiceActivityCurrentSpeechHandling CurrentSpeechHandling,
        ProviderActivityVisibility ProviderVisibility);
    private sealed record Intent(VoiceActivityEffectivePlan CandidatePlan,
        VoiceActivityUpdateFieldResult[] Fields, VoiceActivityCurrentSpeechHandling CurrentSpeechHandling,
        ProviderActivityVisibility ProviderVisibility);
}
