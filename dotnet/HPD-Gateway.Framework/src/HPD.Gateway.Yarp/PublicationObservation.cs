using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;

namespace HPD.Gateway.Yarp;

public sealed record GatewayPublishedUpstream(string UpstreamId, string AvailabilityPolicy);

public sealed record GatewayPublicationObservation(
    ulong Sequence,
    DateTimeOffset ObservedAt,
    GatewayPublicationOutcome? LatestOutcome,
    ActivePublicationIdentity? Active,
    ActivePublicationIdentity? LastKnownGood,
    ImmutableArray<GatewayPublishedUpstream> ActiveUpstreams);

public interface IGatewayPublicationObservationReader
{
    GatewayPublicationObservation GetCurrent();
    IChangeToken GetChangeToken();
}
