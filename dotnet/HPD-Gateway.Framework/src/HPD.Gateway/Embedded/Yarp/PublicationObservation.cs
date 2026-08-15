using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;

namespace HPD.Gateway;

internal sealed record GatewayPublishedUpstream(string UpstreamId, string AvailabilityPolicy);

internal sealed record GatewayPublicationObservation(
    ulong Sequence,
    DateTimeOffset ObservedAt,
    GatewayPublicationOutcome? LatestOutcome,
    ActivePublicationIdentity? Active,
    ActivePublicationIdentity? LastKnownGood,
    ImmutableArray<GatewayPublishedUpstream> ActiveUpstreams);

internal interface IGatewayPublicationObservationReader
{
    GatewayPublicationObservation GetCurrent();
    IChangeToken GetChangeToken();
}
