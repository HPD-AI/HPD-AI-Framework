using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed class GraphMediaWorkExecutionFold
{
    private readonly SessionAuthorityStampV1 _session;
    private readonly StableId128 _residenceId;
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly Dictionary<long, AuthorityFactEnvelopeV1> _positions = [];
    private readonly Dictionary<JournalFactId, AuthorityFactEnvelopeV1> _facts = [];
    private readonly Dictionary<OperationId, Entry> _operations = [];
    private GraphMediaWorkExecutionFoldApplyResult.InvalidHistory? _invalid;
    private JournalPositionV1? _predecessor;
    private bool _completed;
    private long _through;
    private ulong _records;
    private ulong _bytes;
    private int _targetRecords;

    private GraphMediaWorkExecutionFold(SessionAuthorityStampV1 session, StableId128 residenceId,
        AuthorityPayloadAdmissionRegistryV1 registry)
    { _session = session; _residenceId = residenceId; _registry = registry; }

    internal static GraphMediaWorkExecutionFold Create(SessionAuthorityStampV1 session,
        StableId128 residenceId, AuthorityPayloadAdmissionRegistryV1 registry)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
        if (residenceId.Equals(default(StableId128))) throw new ArgumentException("A valid residence is required.", nameof(residenceId));
        ArgumentNullException.ThrowIfNull(registry);
        return new(session, residenceId, registry);
    }

    internal GraphMediaWorkExecutionFoldApplyResult Apply(AuthorityFactEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_invalid is not null) return _invalid;
        if (_completed) return Fail("post-terminal-record");
        if (envelope.Position.Session != _session) return Fail("session-mismatch");
        if (_positions.TryGetValue(envelope.Position.Sequence, out var atPosition))
            return SameEnvelope(atPosition, envelope)
                ? IsTarget(envelope) ? new GraphMediaWorkExecutionFoldApplyResult.Applied(true) : new GraphMediaWorkExecutionFoldApplyResult.Ignored(true)
                : Fail("position-invalid");
        if (_facts.TryGetValue(envelope.FactId, out var withFactId))
            return SameEnvelope(withFactId, envelope)
                ? IsTarget(envelope) ? new GraphMediaWorkExecutionFoldApplyResult.Applied(true) : new GraphMediaWorkExecutionFoldApplyResult.Ignored(true)
                : Fail("fact-id-mismatch");
        if (envelope.Position.Sequence != _through + 1) return Fail("position-invalid");

        var encodedLength = AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
        if (_records == 65_536 || encodedLength > 1_048_576 || _bytes > 67_108_864UL - (ulong)encodedLength)
            return Fail("record-wire-invalid");
        _through++;
        _records++;
        _bytes += (ulong)encodedLength;
        _positions.Add(envelope.Position.Sequence, envelope);
        _facts.Add(envelope.FactId, envelope);

        var proposal = new ProposedAuthorityFactV1(envelope.FactId, envelope.ThreadScope?.ThreadId,
            envelope.Owner, envelope.PayloadSchema, envelope.PayloadBytes, envelope.PayloadHash,
            envelope.Correlation, envelope.ObservedAt);
        if (_registry.Validate(_session, proposal, out _) != AuthorityPayloadAdmissionV1.Exact)
            return Fail("record-wire-invalid");
        if (!IsTarget(envelope)) return new GraphMediaWorkExecutionFoldApplyResult.Ignored(false);
        if (++_targetRecords > GraphMediaWorkLedger.MaximumWorkPerRuntime * 2 ||
            envelope.Owner != OwnerSliceId.S1 || envelope.ThreadScope is not null)
            return Fail("record-wire-invalid");
        return envelope.PayloadSchema == GraphMediaWorkExecutionPayloadRegistrations.Command.Schema
            ? ApplyCommand(envelope)
            : ApplyFact(envelope);
    }

    internal GraphMediaWorkExecutionFoldResult Complete()
    {
        _completed = true;
        if (_invalid is not null) return new GraphMediaWorkExecutionFoldResult.InvalidHistory(_invalid.SafeCode);
        if (_operations.Count == 0) return new GraphMediaWorkExecutionFoldResult.NotFound(_records, _bytes);
        var entry = _operations.Values.OrderBy(x => x.Command.Position.Sequence).Last();
        return Result(entry);
    }

    internal GraphMediaWorkExecutionFoldResult Query(OperationId operationId)
    {
        if (!operationId.IsValid) throw new ArgumentException("A valid operation is required.", nameof(operationId));
        _completed = true;
        if (_invalid is not null) return new GraphMediaWorkExecutionFoldResult.InvalidHistory(_invalid.SafeCode);
        return _operations.TryGetValue(operationId, out var entry)
            ? Result(entry)
            : new GraphMediaWorkExecutionFoldResult.NotFound(_records, _bytes);
    }

    internal GraphMediaWorkExecutionFoldSnapshot Snapshot()
    {
        _completed = true;
        if (_invalid is not null) return new GraphMediaWorkExecutionFoldSnapshot.InvalidHistory(_invalid.SafeCode);
        var entries = _operations.Values.OrderBy(x => x.Command.Position.Sequence).Select(Result).ToArray();
        return new GraphMediaWorkExecutionFoldSnapshot.Current(entries, _records, _bytes);
    }

    private GraphMediaWorkExecutionFoldResult Result(Entry entry)
    {
        if (entry.Fact is null)
            return new GraphMediaWorkExecutionFoldResult.CommandOnly(entry.Command, entry.CommandBody, _records, _bytes);
        return entry.FactBody!.Outcome switch
        {
            GraphMediaWorkExecutionOutcome.Completed => new GraphMediaWorkExecutionFoldResult.Completed(
                entry.Command, entry.Fact, entry.CommandBody, entry.FactBody, entry.FactBody.EvidenceHash!.Value, _records, _bytes),
            GraphMediaWorkExecutionOutcome.Unknown => new GraphMediaWorkExecutionFoldResult.Unknown(
                entry.Command, entry.Fact, entry.CommandBody, entry.FactBody, _records, _bytes),
            GraphMediaWorkExecutionOutcome.Rejected => new GraphMediaWorkExecutionFoldResult.Rejected(
                entry.Command, entry.Fact, entry.CommandBody, entry.FactBody, entry.FactBody.SafeCode!.Value, _records, _bytes),
            _ => new GraphMediaWorkExecutionFoldResult.InvalidHistory(new BoundedAscii("outcome-invalid"))
        };
    }

    private GraphMediaWorkExecutionFoldApplyResult ApplyCommand(AuthorityFactEnvelopeV1 envelope)
    {
        if (!GraphMediaWorkExecutionCodecs.TryDecodeOuter(envelope.PayloadMemory, out var outer) || outer is null ||
            !GraphMediaWorkExecutionCodecs.TryDecodeCommandBody(outer.BodyMemory, out var body) || body is null)
            return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session) return Fail("session-mismatch");
        if (!body.Work.ResidenceId.Equals(_residenceId)) return Fail("operation-conflict");
        if (envelope.Correlation.OperationId != body.OperationId) return Fail("operation-conflict");
        if (GraphMediaWorkExecutionFactIds.Command(_session, body.OperationId) != envelope.FactId)
            return Fail("fact-id-mismatch");
        if (_operations.ContainsKey(body.OperationId)) return Fail("operation-conflict");
        if (_operations.Count >= GraphMediaWorkLedger.MaximumWorkPerRuntime)
            return Fail("record-wire-invalid");
        if (_operations.Values.Any(x => x.CommandBody.Work.WorkId.Equals(body.Work.WorkId) &&
            x.FactBody?.Outcome is GraphMediaWorkExecutionOutcome.Completed or GraphMediaWorkExecutionOutcome.Unknown))
            return Fail("operation-conflict");
        if (_operations.Values.Any(x => x.Fact is null) || body.ExpectedWorkFact != _predecessor)
            return Fail("predecessor-conflict");
        _operations.Add(body.OperationId, new Entry(envelope, body, null, null));
        return new GraphMediaWorkExecutionFoldApplyResult.Applied(false);
    }

    private GraphMediaWorkExecutionFoldApplyResult ApplyFact(AuthorityFactEnvelopeV1 envelope)
    {
        if (!GraphMediaWorkExecutionCodecs.TryDecodeOuter(envelope.PayloadMemory, out var outer) || outer is null ||
            !GraphMediaWorkExecutionCodecs.TryDecodeFactBody(outer.BodyMemory, out var body) || body is null)
            return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session || body.CommandPosition.Session != _session)
            return Fail("session-mismatch");
        var operation = _operations.SingleOrDefault(x => x.Value.Command.Position == body.CommandPosition);
        if (!operation.Key.IsValid) return Fail("fact-without-command");
        var entry = operation.Value;
        if (entry.Fact is not null || GraphMediaWorkExecutionFactIds.Fact(body.CommandPosition) != envelope.FactId)
            return Fail("command-fact-join-invalid");
        if (!GraphMediaWorkExecutionCodecs.TryDecodeOuter(entry.Command.PayloadMemory, out var commandOuter) || commandOuter is null ||
            outer.ExpectedAuthority != commandOuter.ExpectedAuthority || envelope.Correlation != entry.Command.Correlation ||
            envelope.ObservedAt != entry.Command.ObservedAt || body.ObservedAt != entry.CommandBody.ObservedAt ||
            !body.WorkId.Equals(entry.CommandBody.Work.WorkId) || body.WorkRequestHash != entry.CommandBody.Work.RequestHash)
            return Fail("command-fact-join-invalid");
        _operations[operation.Key] = entry with { Fact = envelope, FactBody = body };
        _predecessor = envelope.Position;
        return new GraphMediaWorkExecutionFoldApplyResult.Applied(false);
    }

    private GraphMediaWorkExecutionFoldApplyResult.InvalidHistory Fail(string code) =>
        _invalid ??= new(new BoundedAscii(code));

    private static bool IsTarget(AuthorityFactEnvelopeV1 envelope) =>
        envelope.PayloadSchema == GraphMediaWorkExecutionPayloadRegistrations.Command.Schema ||
        envelope.PayloadSchema == GraphMediaWorkExecutionPayloadRegistrations.Fact.Schema;

    private static bool SameEnvelope(AuthorityFactEnvelopeV1 left, AuthorityFactEnvelopeV1 right) =>
        left.FactId == right.FactId && left.Position == right.Position && left.ThreadScope == right.ThreadScope &&
        left.Owner == right.Owner && left.PayloadSchema == right.PayloadSchema && left.PayloadHash == right.PayloadHash &&
        left.Correlation == right.Correlation && left.ObservedAt == right.ObservedAt && left.AdmittedAt == right.AdmittedAt &&
        left.PayloadBytes.SequenceEqual(right.PayloadBytes) && left.Integrity.Profile == right.Integrity.Profile &&
        left.Integrity.KeyVersion == right.Integrity.KeyVersion && left.Integrity.Digest == right.Integrity.Digest &&
        left.Integrity.SignatureBytes.SequenceEqual(right.Integrity.SignatureBytes);

    private sealed record Entry(AuthorityFactEnvelopeV1 Command, GraphMediaWorkExecutionCommandBody CommandBody,
        AuthorityFactEnvelopeV1? Fact, GraphMediaWorkExecutionFactBody? FactBody);
}

internal abstract record GraphMediaWorkExecutionFoldApplyResult
{
    private GraphMediaWorkExecutionFoldApplyResult() { }
    internal sealed record Applied(bool Duplicate) : GraphMediaWorkExecutionFoldApplyResult;
    internal sealed record Ignored(bool Duplicate) : GraphMediaWorkExecutionFoldApplyResult;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaWorkExecutionFoldApplyResult;
}

internal abstract record GraphMediaWorkExecutionFoldResult
{
    private GraphMediaWorkExecutionFoldResult() { }
    internal sealed record NotFound(ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResult;
    internal sealed record CommandOnly(AuthorityFactEnvelopeV1 Command, GraphMediaWorkExecutionCommandBody Body,
        ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResult;
    internal sealed record Completed(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        GraphMediaWorkExecutionCommandBody CommandBody, GraphMediaWorkExecutionFactBody FactBody,
        Hash256 EvidenceHash, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResult;
    internal sealed record Unknown(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        GraphMediaWorkExecutionCommandBody CommandBody, GraphMediaWorkExecutionFactBody FactBody,
        ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResult;
    internal sealed record Rejected(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact,
        GraphMediaWorkExecutionCommandBody CommandBody, GraphMediaWorkExecutionFactBody FactBody,
        BoundedAscii SafeCode, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaWorkExecutionFoldResult;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaWorkExecutionFoldResult;
}

internal abstract record GraphMediaWorkExecutionFoldSnapshot
{
    private GraphMediaWorkExecutionFoldSnapshot() { }
    internal sealed record Current : GraphMediaWorkExecutionFoldSnapshot
    {
        private readonly GraphMediaWorkExecutionFoldResult[] _operations;
        internal Current(IReadOnlyList<GraphMediaWorkExecutionFoldResult> operations,
            ulong recordCount, ulong totalCanonicalRecordBytes)
        {
            ArgumentNullException.ThrowIfNull(operations);
            _operations = operations.ToArray();
            Operations = Array.AsReadOnly(_operations);
            RecordCount = recordCount;
            TotalCanonicalRecordBytes = totalCanonicalRecordBytes;
        }
        internal IReadOnlyList<GraphMediaWorkExecutionFoldResult> Operations { get; }
        internal ulong RecordCount { get; }
        internal ulong TotalCanonicalRecordBytes { get; }
    }
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaWorkExecutionFoldSnapshot;
}
