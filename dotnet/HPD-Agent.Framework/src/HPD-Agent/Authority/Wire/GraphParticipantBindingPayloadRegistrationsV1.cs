namespace HPD.Agent.Authority;

internal static class GraphParticipantBindingPayloadRegistrationsV1
{
    internal const ushort ReservationCommandDiscriminator = 38;
    internal const ushort ReservationFactDiscriminator = 39;
    internal const ushort BindingCommandDiscriminator = 40;
    internal const ushort BindingFactDiscriminator = 41;

    internal static readonly AuthorityPayloadRegistrationV1 ReservationCommand = Create(
        GraphParticipantBindingCodecsV1.ReservationCommandSchemaId,
        static (payload, session) => GraphParticipantBindingCodecsV1.TryDecodeReservationCommand(payload, out var outer) &&
            outer!.Session == session && outer.Body.Count <= GraphParticipantBindingCodecsV1.MaximumReservationCommandBodyBytes &&
            GraphParticipantBindingCodecsV1.TryDecodeReservationCommandBody(outer.BodyBytes.ToArray(), out var body) &&
            (body!.ExpectedReservationFact is null || body.ExpectedReservationFact.Value.Session == session));

    internal static readonly AuthorityPayloadRegistrationV1 ReservationFact = Create(
        GraphParticipantBindingCodecsV1.ReservationFactSchemaId,
        static (payload, session) => GraphParticipantBindingCodecsV1.TryDecodeReservationFact(payload, out var outer) &&
            outer!.Session == session && outer.Body.Count <= GraphParticipantBindingCodecsV1.MaximumReservationFactBodyBytes &&
            GraphParticipantBindingCodecsV1.TryDecodeReservationFactBody(outer.BodyBytes.ToArray(), out var body) &&
            body!.CommandPosition.Session == session && (body.ActualPredecessor is null || body.ActualPredecessor.Value.Session == session));

    internal static readonly AuthorityPayloadRegistrationV1 BindingCommand = Create(
        GraphParticipantBindingCodecsV1.BindingCommandSchemaId,
        static (payload, session) => GraphParticipantBindingCodecsV1.TryDecodeBindingCommand(payload, out var outer) &&
            outer!.Session == session && outer.Body.Count <= GraphParticipantBindingCodecsV1.MaximumBindingCommandBodyBytes &&
            GraphParticipantBindingCodecsV1.TryDecodeBindingCommandBody(outer.BodyBytes.ToArray(), out var body) &&
            body!.ReservationFact.Session == session && (body.ExpectedBindingFact is null || body.ExpectedBindingFact.Value.Session == session) &&
            body.CapacityGrantProof.GrantedAt.Session == session && body.CapacityGrantProof.CurrentFact.Session == session);

    internal static readonly AuthorityPayloadRegistrationV1 BindingFact = Create(
        GraphParticipantBindingCodecsV1.BindingFactSchemaId,
        static (payload, session) => GraphParticipantBindingCodecsV1.TryDecodeBindingFact(payload, out var outer) &&
            outer!.Session == session && outer.Body.Count <= GraphParticipantBindingCodecsV1.MaximumBindingFactBodyBytes &&
            GraphParticipantBindingCodecsV1.TryDecodeBindingFactBody(outer.BodyBytes.ToArray(), out var body) &&
            body!.CommandPosition.Session == session && body.ReservationFact.Session == session &&
            (body.ActualPredecessor is null || body.ActualPredecessor.Value.Session == session) &&
            (body.CapacityGrantProof is null || body.CapacityGrantProof.GrantedAt.Session == session && body.CapacityGrantProof.CurrentFact.Session == session));

    private static AuthorityPayloadRegistrationV1 Create(string schema, Func<ReadOnlyMemory<byte>, SessionAuthorityStampV1, bool> validator) =>
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new BoundedAscii(schema), GraphParticipantBindingCodecsV1.Major,
            GraphParticipantBindingCodecsV1.Minor, OwnerSliceId.S1, GraphParticipantBindingCodecsV1.MaximumOuterBytes, validator);

    internal static AuthorityPayloadAdmissionV1 ValidateEnvelope(SessionAuthorityStampV1 session, ProposedAuthorityFactV1 proposal, AuthorityPayloadRegistrationV1 registration)
    {
        if (proposal.ThreadId is not null) return AuthorityPayloadAdmissionV1.InvalidPayload;
        return new AuthorityPayloadAdmissionRegistryV1([registration]).Validate(session, proposal, out _);
    }
}
