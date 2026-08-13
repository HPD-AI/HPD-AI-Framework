namespace HPD.Agent.Authority;

internal static class GlobalParticipantAllocatorPayloadRegistrationV1
{
    internal const ushort Discriminator = 42;

    internal static readonly AuthorityPayloadRegistrationV1 ClaimRecord =
        AuthorityPayloadRegistrationV1.CreateOwnerRegistration(
            new BoundedAscii(GlobalParticipantAllocatorCodecsV1.OuterSchemaId), 1, 0,
            OwnerSliceId.S1, GlobalParticipantAllocatorCodecsV1.MaximumOuterBytes,
            static (payload, session) =>
                GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(payload, out var outer) &&
                outer!.SourceSession == session &&
                GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer.BodyBytes.ToArray(), out var body) &&
                body!.Source.LiveSessionId == session.LiveSessionId &&
                body.Source.SourceFactPosition.Session == session);

    internal static AuthorityPayloadAdmissionV1 ValidateEnvelope(
        SessionAuthorityStampV1 session, ProposedAuthorityFactV1 proposal)
    {
        if (proposal.ThreadId is not null)
            return AuthorityPayloadAdmissionV1.InvalidPayload;
        return new AuthorityPayloadAdmissionRegistryV1([ClaimRecord]).Validate(session, proposal, out _);
    }
}
