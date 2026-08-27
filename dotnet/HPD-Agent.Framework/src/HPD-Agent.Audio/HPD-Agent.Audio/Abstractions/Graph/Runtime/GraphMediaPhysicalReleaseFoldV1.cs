using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal sealed class GraphMediaPhysicalReleaseFoldV1
{
    private readonly SessionAuthorityStampV1 _session;
    private readonly StableId128 _residenceId;
    private readonly AuthorityPayloadAdmissionRegistryV1 _registry;
    private readonly Dictionary<long, AuthorityFactEnvelopeV1> _positions = [];
    private readonly Dictionary<JournalFactId, AuthorityFactEnvelopeV1> _facts = [];
    private readonly Dictionary<OperationId, (AuthorityFactEnvelopeV1 Command,
        GraphMediaPhysicalReleaseCommandBodyV1 CommandBody, AuthorityFactEnvelopeV1? Fact,
        GraphMediaPhysicalReleaseFactBodyV1? FactBody)> _operations = [];
    private GraphMediaPhysicalReleaseFoldApplyResultV1.InvalidHistory? _invalid;
    private JournalPositionV1? _predecessor;
    private bool _terminal;
    private bool _completed;
    private long _through;
    private ulong _records;
    private ulong _bytes;
    private int _targetRecords;

    private GraphMediaPhysicalReleaseFoldV1(SessionAuthorityStampV1 session, StableId128 residenceId,
        AuthorityPayloadAdmissionRegistryV1 registry)
    { _session = session; _residenceId = residenceId; _registry = registry; }

    internal static GraphMediaPhysicalReleaseFoldV1 Create(SessionAuthorityStampV1 session,
        StableId128 residenceId, AuthorityPayloadAdmissionRegistryV1 registry)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
        if (residenceId.Equals(default(StableId128))) throw new ArgumentException("A valid residence is required.", nameof(residenceId));
        ArgumentNullException.ThrowIfNull(registry);
        return new(session, residenceId, registry);
    }

    internal GraphMediaPhysicalReleaseFoldApplyResultV1 Apply(AuthorityFactEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_invalid is not null) return _invalid;
        if (_completed) return Fail("post-terminal-record");
        if (envelope.Position.Session != _session) return Fail("session-mismatch");

        if (_positions.TryGetValue(envelope.Position.Sequence, out var positioned))
            return SameEnvelope(positioned, envelope)
                ? IsTarget(envelope) ? new GraphMediaPhysicalReleaseFoldApplyResultV1.Applied(true) : new GraphMediaPhysicalReleaseFoldApplyResultV1.Ignored(true)
                : Fail("position-invalid");
        if (_facts.TryGetValue(envelope.FactId, out var identified))
            return SameEnvelope(identified, envelope)
                ? IsTarget(envelope) ? new GraphMediaPhysicalReleaseFoldApplyResultV1.Applied(true) : new GraphMediaPhysicalReleaseFoldApplyResultV1.Ignored(true)
                : Fail("record-wire-invalid");
        if (envelope.Position.Sequence != _through + 1) return Fail("position-invalid");

        var encodedLength = AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
        if (_records == 65_536 || encodedLength > 1_048_576 || _bytes > 67_108_864UL - (ulong)encodedLength)
            return Fail("record-wire-invalid");
        _through++;
        _records++;
        _bytes += (ulong)encodedLength;
        _positions.Add(envelope.Position.Sequence, envelope);
        _facts.Add(envelope.FactId, envelope);

        if (!IsTarget(envelope))
        {
            var proposal = new ProposedAuthorityFactV1(envelope.FactId, envelope.ThreadScope?.ThreadId,
                envelope.Owner, envelope.PayloadSchema, envelope.PayloadBytes, envelope.PayloadHash,
                envelope.Correlation, envelope.ObservedAt);
            return _registry.Validate(_session, proposal, out _) == AuthorityPayloadAdmissionV1.Exact
                ? new GraphMediaPhysicalReleaseFoldApplyResultV1.Ignored(false)
                : Fail("record-wire-invalid");
        }
        if (_terminal) return Fail("post-terminal-record");
        if (++_targetRecords > 16) return Fail("record-wire-invalid");
        if (envelope.Owner != OwnerSliceId.S1 || envelope.ThreadScope is not null) return Fail("record-wire-invalid");

        var registration = envelope.PayloadSchema == GraphMediaPhysicalReleasePayloadRegistrationsV1.Command.Schema
            ? GraphMediaPhysicalReleasePayloadRegistrationsV1.Command
            : GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact;
        var proposalTarget = new ProposedAuthorityFactV1(envelope.FactId, null, envelope.Owner,
            envelope.PayloadSchema, envelope.PayloadBytes, envelope.PayloadHash, envelope.Correlation, envelope.ObservedAt);
        var admission = ReferenceEquals(registration, GraphMediaPhysicalReleasePayloadRegistrationsV1.Command)
            ? GraphMediaPhysicalReleasePayloadRegistrationsV1.ValidateCommandEnvelope(_session, proposalTarget)
            : GraphMediaPhysicalReleasePayloadRegistrationsV1.ValidateFactEnvelope(_session, proposalTarget);
        if (admission != AuthorityPayloadAdmissionV1.Exact)
            return Fail("record-wire-invalid");
        return ReferenceEquals(registration, GraphMediaPhysicalReleasePayloadRegistrationsV1.Command)
            ? ApplyCommand(envelope)
            : ApplyFact(envelope);
    }

    internal GraphMediaPhysicalReleaseFoldResultV1 Complete()
    {
        _completed = true;
        if (_invalid is not null) return new GraphMediaPhysicalReleaseFoldResultV1.InvalidHistory(_invalid.SafeCode);
        if (_operations.Count == 0) return new GraphMediaPhysicalReleaseFoldResultV1.NotFound(_records, _bytes);
        var entry = _operations.Values.OrderBy(x => x.Command.Position.Sequence).Last();
        if (entry.Fact is null) return new GraphMediaPhysicalReleaseFoldResultV1.CommandOnly(entry.Command, entry.CommandBody, _records, _bytes);
        var fact = entry.FactBody!;
        return fact.Outcome switch
        {
            GraphMediaPhysicalReleaseOutcomeV1.Released => new GraphMediaPhysicalReleaseFoldResultV1.Released(entry.Command, entry.Fact, entry.CommandBody, fact, fact.EvidenceHash!.Value, _records, _bytes),
            GraphMediaPhysicalReleaseOutcomeV1.Unknown => new GraphMediaPhysicalReleaseFoldResultV1.Unknown(entry.Command, entry.Fact, entry.CommandBody, fact, _records, _bytes),
            GraphMediaPhysicalReleaseOutcomeV1.Rejected => new GraphMediaPhysicalReleaseFoldResultV1.Rejected(entry.Command, entry.Fact, entry.CommandBody, fact, fact.SafeCode!.Value, _records, _bytes),
            _ => new GraphMediaPhysicalReleaseFoldResultV1.InvalidHistory(new BoundedAscii("outcome-invalid"))
        };
    }

    private GraphMediaPhysicalReleaseFoldApplyResultV1 ApplyCommand(AuthorityFactEnvelopeV1 envelope)
    {
        if (!GraphMediaPhysicalReleaseCodecsV1.TryDecodeOuter(envelope.PayloadMemory, out var outer) || outer is null ||
            !GraphMediaPhysicalReleaseCodecsV1.TryDecodeCommandBody(outer.BodyMemory, out var body) || body is null)
            return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session) return Fail("session-mismatch");
        if (!body.Residence.ResidenceId.Equals(_residenceId)) return Fail("operation-conflict");
        if (GraphMediaPhysicalReleaseFactIdsV1.Command(_session, body.OperationId) != envelope.FactId) return Fail("record-wire-invalid");
        if (_operations.ContainsKey(body.OperationId)) return Fail("operation-conflict");
        if (_operations.Count >= 8) return Fail("record-wire-invalid");
        if (_operations.Values.Any(x => x.Fact is null)) return Fail("predecessor-conflict");
        if (body.ExpectedReleaseFact != _predecessor) return Fail("predecessor-conflict");
        _operations.Add(body.OperationId, (envelope, body, null, null));
        return new GraphMediaPhysicalReleaseFoldApplyResultV1.Applied(false);
    }

    private GraphMediaPhysicalReleaseFoldApplyResultV1 ApplyFact(AuthorityFactEnvelopeV1 envelope)
    {
        if (!GraphMediaPhysicalReleaseCodecsV1.TryDecodeOuter(envelope.PayloadMemory, out var outer) || outer is null ||
            !GraphMediaPhysicalReleaseCodecsV1.TryDecodeFactBody(outer.BodyMemory, out var body) || body is null)
            return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session || body.CommandPosition.Session != _session)
            return Fail("session-mismatch");
        var match = _operations.SingleOrDefault(x => x.Value.Command.Position == body.CommandPosition);
        if (!match.Key.IsValid) return Fail("fact-without-command");
        var entry = match.Value;
        if (entry.Fact is not null || GraphMediaPhysicalReleaseFactIdsV1.Fact(body.CommandPosition) != envelope.FactId)
            return Fail("command-fact-join-invalid");
        if (!GraphMediaPhysicalReleaseCodecsV1.TryDecodeOuter(entry.Command.PayloadMemory, out var commandOuter) || commandOuter is null)
            return Fail("command-fact-join-invalid");
        var command = entry.CommandBody;
        if (outer.ExpectedAuthority != commandOuter.ExpectedAuthority || envelope.Correlation != entry.Command.Correlation ||
            envelope.ObservedAt != entry.Command.ObservedAt || body.ObservedAt != command.ObservedAt ||
            !body.ResidenceId.Equals(command.Residence.ResidenceId) || body.ResidenceRequestHash != command.Residence.RequestHash ||
            body.GrantId != command.Residence.GrantId || body.CurrentFact != command.Residence.CurrentFact ||
            body.Assignment != command.Residence.Assignment)
            return Fail("command-fact-join-invalid");
        _operations[match.Key] = (entry.Command, entry.CommandBody, envelope, body);
        _predecessor = envelope.Position;
        if (body.Outcome is GraphMediaPhysicalReleaseOutcomeV1.Released or GraphMediaPhysicalReleaseOutcomeV1.Unknown)
            _terminal = true;
        return new GraphMediaPhysicalReleaseFoldApplyResultV1.Applied(false);
    }

    private GraphMediaPhysicalReleaseFoldApplyResultV1.InvalidHistory Fail(string code) =>
        _invalid ??= new(new BoundedAscii(code));

    private static bool SameEnvelope(AuthorityFactEnvelopeV1 left, AuthorityFactEnvelopeV1 right) =>
        left.FactId == right.FactId && left.Position == right.Position && left.ThreadScope == right.ThreadScope &&
        left.Owner == right.Owner && left.PayloadSchema == right.PayloadSchema && left.PayloadHash == right.PayloadHash &&
        left.Correlation == right.Correlation && left.ObservedAt == right.ObservedAt && left.AdmittedAt == right.AdmittedAt &&
        left.PayloadBytes.SequenceEqual(right.PayloadBytes) && left.Integrity.Profile == right.Integrity.Profile &&
        left.Integrity.KeyVersion == right.Integrity.KeyVersion && left.Integrity.Digest == right.Integrity.Digest &&
        left.Integrity.SignatureBytes.SequenceEqual(right.Integrity.SignatureBytes);

    private static bool IsTarget(AuthorityFactEnvelopeV1 envelope) =>
        envelope.PayloadSchema == GraphMediaPhysicalReleasePayloadRegistrationsV1.Command.Schema ||
        envelope.PayloadSchema == GraphMediaPhysicalReleasePayloadRegistrationsV1.Fact.Schema;

}

internal abstract record GraphMediaPhysicalReleaseFoldApplyResultV1
{
    private GraphMediaPhysicalReleaseFoldApplyResultV1() { }
    internal sealed record Applied(bool Duplicate) : GraphMediaPhysicalReleaseFoldApplyResultV1;
    internal sealed record Ignored(bool Duplicate) : GraphMediaPhysicalReleaseFoldApplyResultV1;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseFoldApplyResultV1;
}

internal abstract record GraphMediaPhysicalReleaseFoldResultV1
{
    private GraphMediaPhysicalReleaseFoldResultV1() { }
    internal sealed record NotFound(ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResultV1;
    internal sealed record CommandOnly(AuthorityFactEnvelopeV1 Command, GraphMediaPhysicalReleaseCommandBodyV1 Body, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResultV1;
    internal sealed record Released(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, GraphMediaPhysicalReleaseCommandBodyV1 CommandBody, GraphMediaPhysicalReleaseFactBodyV1 FactBody, Hash256 EvidenceHash, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResultV1;
    internal sealed record Unknown(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, GraphMediaPhysicalReleaseCommandBodyV1 CommandBody, GraphMediaPhysicalReleaseFactBodyV1 FactBody, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResultV1;
    internal sealed record Rejected(AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, GraphMediaPhysicalReleaseCommandBodyV1 CommandBody, GraphMediaPhysicalReleaseFactBodyV1 FactBody, BoundedAscii SafeCode, ulong RecordCount, ulong TotalCanonicalRecordBytes) : GraphMediaPhysicalReleaseFoldResultV1;
    internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphMediaPhysicalReleaseFoldResultV1;
}
