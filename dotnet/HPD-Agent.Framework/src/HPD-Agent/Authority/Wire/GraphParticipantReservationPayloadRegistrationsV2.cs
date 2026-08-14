namespace HPD.Agent.Authority;

internal static class GraphParticipantReservationPayloadRegistrationsV2
{
    internal const ushort ReservationCommandDiscriminator = 43;
    internal const ushort ReservationFactDiscriminator = 44;

    internal static readonly AuthorityPayloadRegistrationV1 ReservationCommand =
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new BoundedAscii(GraphParticipantReservationCodecsV2.ReservationCommandSchemaId),GraphParticipantReservationCodecsV2.Major,GraphParticipantReservationCodecsV2.Minor,OwnerSliceId.S1,GraphParticipantReservationCodecsV2.MaximumOuterBytes,ValidateReservationCommand);
    internal static readonly AuthorityPayloadRegistrationV1 ReservationFact =
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(new BoundedAscii(GraphParticipantReservationCodecsV2.ReservationFactSchemaId),GraphParticipantReservationCodecsV2.Major,GraphParticipantReservationCodecsV2.Minor,OwnerSliceId.S1,GraphParticipantReservationCodecsV2.MaximumOuterBytes,ValidateReservationFact);

    private static bool ValidateReservationCommand(ReadOnlyMemory<byte> payload,SessionAuthorityStampV1 session) =>
        GraphParticipantReservationCodecsV2.ReservationCommandSchemaId=="hpd.authority-payload-graph-participant-reservation-command.v2"&&GraphParticipantReservationCodecsV2.TryDecodeReservationCommand(payload,out var outer)&&outer!.Session==session&&outer.Body.Count<=16384&&
        GraphParticipantReservationCodecsV2.TryDecodeReservationCommandBody(outer.BodyBytes.ToArray(),out var body)&&
        (body!.ExpectedReservationFact is null||body.ExpectedReservationFact.Value.Session==session);
    private static bool ValidateReservationFact(ReadOnlyMemory<byte> payload,SessionAuthorityStampV1 session) =>
        GraphParticipantReservationCodecsV2.ReservationFactSchemaId=="hpd.authority-payload-graph-participant-reservation-fact.v2"&&GraphParticipantReservationCodecsV2.TryDecodeReservationFact(payload,out var outer)&&outer!.Session==session&&outer.Body.Count<=16384&&
        GraphParticipantReservationCodecsV2.TryDecodeReservationFactBody(outer.BodyBytes.ToArray(),out var body)&&
        body!.CommandPosition.Session==session&&(body.ActualPredecessor is null||body.ActualPredecessor.Value.Session==session);
    internal static AuthorityPayloadAdmissionV1 ValidateEnvelope(SessionAuthorityStampV1 session,ProposedAuthorityFactV1 proposal,AuthorityPayloadRegistrationV1 registration)
    { if(!ReferenceEquals(registration,ReservationCommand)&&!ReferenceEquals(registration,ReservationFact))return AuthorityPayloadAdmissionV1.InvalidPayload;var expected=ReferenceEquals(registration,ReservationCommand)?ReservationCommand:ReservationFact;bool newSchemaVersionV1(ushort major,ushort minor)=>registration.Schema.Major==major&&registration.Schema.Minor==minor;if(!newSchemaVersionV1(2,0)||registration.Schema!=expected.Schema||registration.SchemaToken!=expected.SchemaToken||registration.Owner!=expected.Owner||registration.MaximumPayloadBytes!=GraphParticipantReservationCodecsV2.MaximumOuterBytes||proposal.ThreadId is not null)return AuthorityPayloadAdmissionV1.InvalidPayload;var admission=new AuthorityPayloadAdmissionRegistryV1([registration]).Validate(session,proposal,out _);return admission==AuthorityPayloadAdmissionV1.Exact?admission:AuthorityPayloadAdmissionV1.InvalidPayload; }
}
