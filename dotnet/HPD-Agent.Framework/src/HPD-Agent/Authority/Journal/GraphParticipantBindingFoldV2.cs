namespace HPD.Agent.Authority;

internal sealed class GraphParticipantBindingFoldV2
{
    private readonly SessionAuthorityStampV1 _session;
    private readonly GraphParticipantReservationFoldV2 _reservations;
    private readonly Dictionary<OperationId, AuthorityFactEnvelopeV1> _reservationCommands = [];
    private readonly Dictionary<OperationId, GraphParticipantReservationFoldV2.AppliedReservation> _applied = [];
    private readonly Dictionary<OperationId, (AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1? Fact, GraphParticipantBindingV1? Binding, CapacityGrantBindingProofV1? Proof, BoundedAscii? Code)> _bindings = [];
    private readonly HashSet<JournalFactId> _factIds = [];
    private GraphParticipantBindingFoldApplyResultV2.InvalidHistory? _invalid;
    private bool _completed;
    private long _through;
    private ulong _records;
    private ulong _bytes;
    private JournalPositionV1? _boundFact;

    private GraphParticipantBindingFoldV2(SessionAuthorityStampV1 session)
    {
        _session = session;
        _reservations = GraphParticipantReservationFoldV2.Create(session);
    }

    internal static GraphParticipantBindingFoldV2 Create(SessionAuthorityStampV1 session)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session is required.", nameof(session));
        return new(session);
    }

    internal GraphParticipantBindingFoldApplyResultV2 Apply(AuthorityFactEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_invalid is not null) return _invalid;
        if (_completed) return Fail("safe-code-invalid");
        if (envelope.Position.Session != _session) return Fail("session-mismatch");
        if (envelope.Position.Sequence != _through + 1) return Fail("position-invalid");
        if (!_factIds.Add(envelope.FactId)) return Fail("duplicate-changed");
        var encodedLength = AuthorityCanonicalCborV1.GetEnvelopeEncodedLength(envelope);
        if (_records == 65_536 || encodedLength > 8192 || _bytes > 536_870_912UL - (ulong)encodedLength) return Fail("record-wire-invalid");
        _through++;
        _records++;
        _bytes += (ulong)encodedLength;

        var reservationApply = _reservations.Apply(envelope);
        if (reservationApply is GraphParticipantReservationFoldV2.InvalidHistory reservationInvalid)
            return Fail(reservationInvalid.SafeCode.ToString());
        ApplyReservation(envelope);

        var commandRegistration = GraphParticipantBindingPayloadRegistrationsV1.BindingCommand;
        var factRegistration = GraphParticipantBindingPayloadRegistrationsV1.BindingFact;
        if (envelope.PayloadSchema != commandRegistration.Schema && envelope.PayloadSchema != factRegistration.Schema)
            return new GraphParticipantBindingFoldApplyResultV2.Accepted();
        if (_boundFact is not null) return Fail("target-after-terminal");
        if (envelope.Owner != OwnerSliceId.S1 || envelope.ThreadScope is not null) return Fail("record-wire-invalid");
        var exact = envelope.PayloadBytes.ToArray();
        var registration = envelope.PayloadSchema == commandRegistration.Schema ? commandRegistration : factRegistration;
        if (envelope.PayloadHash != AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, exact)) return Fail("record-wire-invalid");
        return envelope.PayloadSchema == commandRegistration.Schema ? ApplyBindingCommand(envelope, exact) : ApplyBindingFact(envelope, exact);
    }

    internal GraphParticipantBindingFoldCompleteResultV2 Complete()
    {
        if (_invalid is not null) return new GraphParticipantBindingFoldCompleteResultV2.InvalidHistory(_invalid.SafeCode);
        var completed = _reservations.Complete();
        _completed = true;
        return new GraphParticipantBindingFoldCompleteResultV2.Completed(_session, _through, _records, _bytes, _boundFact, completed.AppliedReservationFact);
    }

    internal GraphParticipantBindingFoldQueryResultV2 Query(OperationId operationId)
    {
        if (!_completed || _invalid is not null || !operationId.IsValid) throw new InvalidOperationException("Only a valid completed history is queryable.");
        if (!_applied.TryGetValue(operationId, out var reservation)) return new GraphParticipantBindingFoldQueryResultV2.NotFound();
        if (!_bindings.TryGetValue(operationId, out var value)) return new GraphParticipantBindingFoldQueryResultV2.ReservationOnly(reservation);
        if (value.Fact is null) return new GraphParticipantBindingFoldQueryResultV2.CommandOnly(reservation, value.Command);
        return value.Binding is not null
            ? new GraphParticipantBindingFoldQueryResultV2.Bound(reservation, value.Command, value.Fact, value.Binding, value.Proof!)
            : new GraphParticipantBindingFoldQueryResultV2.Rejected(reservation, value.Command, value.Fact, value.Code!.Value);
    }

    internal GraphParticipantBindingElectionResultV2 Elect(OperationId operationId)
    {
        if (!_completed || _invalid is not null || !operationId.IsValid) throw new InvalidOperationException("Only a valid completed history is electable.");
        if (_boundFact is not null) return new GraphParticipantBindingElectionResultV2.BlockedByBound(_boundFact.Value);
        if (!_bindings.TryGetValue(operationId, out var target) || target.Fact is not null || !_applied.ContainsKey(operationId)) return new GraphParticipantBindingElectionResultV2.PredecessorConflict(_boundFact);
        var leader = _bindings.Where(x => x.Value.Fact is null && _applied.ContainsKey(x.Key)).OrderBy(x => x.Key.ToString(), StringComparer.Ordinal).First().Key;
        return leader == operationId ? new GraphParticipantBindingElectionResultV2.Leader() : new GraphParticipantBindingElectionResultV2.Follower(leader);
    }

    private void ApplyReservation(AuthorityFactEnvelopeV1 envelope)
    {
        if (envelope.PayloadSchema == GraphParticipantReservationPayloadRegistrationsV2.ReservationCommand.Schema)
        {
            if (GraphParticipantReservationCodecsV2.TryDecodeReservationCommand(envelope.PayloadMemory, out var outer) && outer is not null && GraphParticipantReservationCodecsV2.TryDecodeReservationCommandBody(outer.BodyBytes.ToArray(), out var command) && command is not null)
                _reservationCommands[command.OperationId] = envelope;
            return;
        }
        if (envelope.PayloadSchema != GraphParticipantReservationPayloadRegistrationsV2.ReservationFact.Schema) return;
        if (!GraphParticipantReservationCodecsV2.TryDecodeReservationFact(envelope.PayloadMemory, out var factOuter) || factOuter is null || !GraphParticipantReservationCodecsV2.TryDecodeReservationFactBody(factOuter.BodyBytes.ToArray(), out var fact) || fact is null || fact.Reservation is null || !_reservationCommands.TryGetValue(fact.OperationId, out var commandEnvelope)) return;
        _applied[fact.OperationId] = new GraphParticipantReservationFoldV2.AppliedReservation(commandEnvelope, envelope, fact.Reservation);
    }

    private GraphParticipantBindingFoldApplyResultV2 ApplyBindingCommand(AuthorityFactEnvelopeV1 envelope, byte[] exact)
    {
        if (!GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(exact, out var outer) || outer is null || !GraphParticipantBindingCodecsV1.TryDecodeBindingCommandBody(outer.BodyBytes.ToArray(), out var body) || body is null) return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session) return Fail("session-mismatch");
        if (GraphParticipantBindingFactIdsV1.BindingCommand(_session, body.OperationId) != envelope.FactId) return Fail("fact-id-mismatch");
        if (!_applied.TryGetValue(body.OperationId, out var reservation) || reservation.Fact.Position != body.ReservationFact) return Fail("reservation-binding-join-invalid");
        if (!GraphParticipantReservationCodecsV2.TryDecodeReservationCommand(reservation.Command.PayloadMemory, out var reservationOuter) || reservationOuter is null ||
            !GraphParticipantReservationCodecsV2.TryDecodeReservationCommandBody(reservationOuter.BodyBytes.ToArray(), out var reservationCommand) || reservationCommand is null ||
            !GraphParticipantReservationCodecsV2.TryDecodeReservationFact(reservation.Fact.PayloadMemory, out var reservationFactOuter) || reservationFactOuter is null ||
            !GraphParticipantReservationCodecsV2.TryDecodeReservationFactBody(reservationFactOuter.BodyBytes.ToArray(), out var reservationFact) || reservationFact is null ||
            reservationFact.Outcome != 1 || reservationFact.Reservation is null || reservationFact.CommandPosition != reservation.Command.Position ||
            outer.ExpectedAuthority != reservationOuter.ExpectedAuthority || envelope.Correlation != reservation.Command.Correlation ||
            body.RuntimeGeneration != reservationCommand.RuntimeGeneration || body.GraphGeneration != reservationCommand.GraphGeneration ||
            body.ParticipantPlanFingerprint != reservationCommand.ParticipantPlanFingerprint ||
            reservationFact.RuntimeGeneration != reservationCommand.RuntimeGeneration || reservationFact.GraphGeneration != reservationCommand.GraphGeneration ||
            reservationFact.ParticipantPlanFingerprint != reservationCommand.ParticipantPlanFingerprint ||
            reservationFact.AllocationCarrierFingerprint != reservationCommand.AllocationCarrierFingerprint)
            return Fail("reservation-binding-join-invalid");
        if (_bindings.ContainsKey(body.OperationId)) return Fail("duplicate-changed");
        _bindings.Add(body.OperationId, (envelope, null, null, null, null));
        return new GraphParticipantBindingFoldApplyResultV2.Accepted();
    }

    private GraphParticipantBindingFoldApplyResultV2 ApplyBindingFact(AuthorityFactEnvelopeV1 envelope, byte[] exact)
    {
        if (!GraphParticipantBindingCodecsV1.TryDecodeBindingFact(exact, out var outer) || outer is null || !GraphParticipantBindingCodecsV1.TryDecodeBindingFactBody(outer.BodyBytes.ToArray(), out var body) || body is null) return Fail("record-wire-invalid");
        if (outer.Session != _session || outer.ExpectedAuthority.Session != _session || body.CommandPosition.Session != _session) return Fail("session-mismatch");
        if (!_bindings.TryGetValue(body.OperationId, out var value) || value.Command.Position != body.CommandPosition || value.Fact is not null || GraphParticipantBindingFactIdsV1.BindingFact(body.CommandPosition) != envelope.FactId) return Fail("command-fact-join-invalid");
        if (!GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(value.Command.PayloadMemory, out var commandOuter) || commandOuter is null || !GraphParticipantBindingCodecsV1.TryDecodeBindingCommandBody(commandOuter.BodyBytes.ToArray(), out var command) || command is null) return Fail("command-fact-join-invalid");
        if (outer.ExpectedAuthority != commandOuter.ExpectedAuthority || body.ReservationFact != command.ReservationFact || body.GraphGeneration != command.GraphGeneration || body.RuntimeGeneration != command.RuntimeGeneration || body.ParticipantPlanFingerprint != command.ParticipantPlanFingerprint || body.TopologyFingerprint != command.TopologyFingerprint || body.ExecutablePlanFingerprint != command.ExecutablePlanFingerprint || body.Outcome == 1 && body.CapacityGrantProof != command.CapacityGrantProof || envelope.Correlation != value.Command.Correlation || envelope.ObservedAt != value.Command.ObservedAt || body.ObservedAt != command.ObservedAt) return Fail("command-fact-join-invalid");
        if (!_applied.TryGetValue(body.OperationId, out var reservation)) return Fail("reservation-binding-join-invalid");
        var code = body.SafeCode?.ToString();
        if (body.Outcome == 1 && body.Binding is not null && body.CapacityGrantProof is not null && body.SafeCode is null && _boundFact is null)
        {
            if (body.Binding.ParticipantId != reservation.Reservation.ParticipantId || body.Binding.ParticipantFactoryKey != reservation.Reservation.ParticipantFactoryKey || !body.Binding.OrderedTopologyNodeKeys.SequenceEqual(reservation.Reservation.OrderedTopologyNodeKeys)) return Fail("reservation-binding-join-invalid");
            _boundFact = envelope.Position; _bindings[body.OperationId] = (value.Command, envelope, body.Binding, body.CapacityGrantProof, null);
        }
        else if (body.Outcome == 2 && body.Binding is null && body.CapacityGrantProof is null && code is "participant-binding-already-applied" or "binding-predecessor-conflict")
            _bindings[body.OperationId] = (value.Command, envelope, null, null, body.SafeCode);
        else return Fail("outcome-invalid");
        return new GraphParticipantBindingFoldApplyResultV2.Accepted();
    }

    private GraphParticipantBindingFoldApplyResultV2.InvalidHistory Fail(string code) => _invalid ??= new(new BoundedAscii(code));
}

internal abstract record GraphParticipantBindingFoldApplyResultV2
{
    private GraphParticipantBindingFoldApplyResultV2() { } internal sealed record Accepted() : GraphParticipantBindingFoldApplyResultV2; internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphParticipantBindingFoldApplyResultV2;
}
internal abstract record GraphParticipantBindingFoldCompleteResultV2
{
    private GraphParticipantBindingFoldCompleteResultV2() { } internal sealed record Completed(SessionAuthorityStampV1 Session, long SnapshotThrough, ulong RecordCount, ulong TotalCanonicalBytes, JournalPositionV1? BoundFact, JournalPositionV1? ReservationFact) : GraphParticipantBindingFoldCompleteResultV2; internal sealed record InvalidHistory(BoundedAscii SafeCode) : GraphParticipantBindingFoldCompleteResultV2;
}
internal abstract record GraphParticipantBindingFoldQueryResultV2
{
    private GraphParticipantBindingFoldQueryResultV2() { } internal sealed record NotFound() : GraphParticipantBindingFoldQueryResultV2; internal sealed record ReservationOnly(GraphParticipantReservationFoldV2.AppliedReservation Reservation) : GraphParticipantBindingFoldQueryResultV2; internal sealed record CommandOnly(GraphParticipantReservationFoldV2.AppliedReservation Reservation, AuthorityFactEnvelopeV1 Command) : GraphParticipantBindingFoldQueryResultV2; internal sealed record Bound(GraphParticipantReservationFoldV2.AppliedReservation Reservation, AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, GraphParticipantBindingV1 Binding, CapacityGrantBindingProofV1 CapacityGrantProof) : GraphParticipantBindingFoldQueryResultV2; internal sealed record Rejected(GraphParticipantReservationFoldV2.AppliedReservation Reservation, AuthorityFactEnvelopeV1 Command, AuthorityFactEnvelopeV1 Fact, BoundedAscii SafeCode) : GraphParticipantBindingFoldQueryResultV2;
}
internal abstract record GraphParticipantBindingElectionResultV2
{
    private GraphParticipantBindingElectionResultV2() { } internal sealed record Leader() : GraphParticipantBindingElectionResultV2; internal sealed record Follower(OperationId OperationId) : GraphParticipantBindingElectionResultV2; internal sealed record BlockedByBound(JournalPositionV1 FactPosition) : GraphParticipantBindingElectionResultV2; internal sealed record PredecessorConflict(JournalPositionV1? ActualPredecessor) : GraphParticipantBindingElectionResultV2;
}
