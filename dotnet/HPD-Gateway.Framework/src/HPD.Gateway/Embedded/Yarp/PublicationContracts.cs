using System.Collections.Immutable;

namespace HPD.Gateway;

public enum GatewayPublicationState : byte
{
    ActiveAcknowledged = 0,
    PublicationIndeterminate = 1,
    Duplicate = 2,
    Stale = 3,
    IdentityConflict = 4,
    Superseded = 5,
    CanceledBeforePublish = 6,
    RejectedBeforePublish = 7
}

public sealed record PublicationCandidateIdentity(
    CandidateId CandidateId,
    string AuthorityId,
    string AuthorityEpoch,
    ulong AuthorityVersion,
    ContentHash ContentHash);

public sealed record ActivePublicationIdentity(
    PublicationCandidateIdentity Candidate,
    string ApplicationId,
    ContentHash SymbolicPlanIdentity,
    string NativeRevisionId,
    DateTimeOffset AcknowledgedAt);

public sealed record GatewayPublicationDiagnostic(string Code, string SafeMessage);

public sealed record GatewayPublicationOutcome(
    GatewayPublicationState State,
    PublicationCandidateIdentity Attempted,
    ActivePublicationIdentity? Active,
    ActivePublicationIdentity? LastKnownGood,
    string? AttemptedNativeRevisionId,
    ImmutableArray<GatewayPublicationDiagnostic> Diagnostics);
