using System.Collections.Immutable;
using HPD.Gateway.Abstractions;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway.Yarp;

internal sealed record NativePublicationBundle(
    PublicationCandidateIdentity Identity,
    string NativeRevisionId,
    ImmutableArray<RouteConfig> Routes,
    ImmutableArray<ClusterConfig> Clusters)
{
    internal const int MaximumNativeRevisionIdLength = 256;

    internal static NativePublicationBundle Create(
        PublicationCandidateIdentity identity,
        ImmutableArray<RouteConfig> routes,
        ImmutableArray<ClusterConfig> clusters,
        string? nativeRevisionId = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!GatewayIdentifier.IsCanonical(identity.CandidateId.Value)) throw new ArgumentException("Candidate identity is not canonical.", nameof(identity));
        if (string.IsNullOrWhiteSpace(identity.AuthorityId) || string.IsNullOrWhiteSpace(identity.AuthorityEpoch)) throw new ArgumentException("Authority identity and epoch are required.", nameof(identity));
        if (identity.ContentHash.Algorithm != "sha-256" || identity.ContentHash.Value?.Length != 64) throw new ArgumentException("A canonical SHA-256 content identity is required.", nameof(identity));
        if (routes.IsDefault || clusters.IsDefault) throw new ArgumentException("Native publication arrays must be initialized.");
        var revision = nativeRevisionId ?? $"hpd-{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(revision) || revision.Length > MaximumNativeRevisionIdLength || revision.Any(char.IsControl))
            throw new ArgumentException($"Native revision identity must be nonblank, at most {MaximumNativeRevisionIdLength} characters, and contain no control characters.", nameof(nativeRevisionId));
        return new NativePublicationBundle(identity, revision, routes, clusters);
    }
}
