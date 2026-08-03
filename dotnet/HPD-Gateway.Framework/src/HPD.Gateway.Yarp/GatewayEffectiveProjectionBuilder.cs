using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Core;
using HPD.Gateway.Effective;

namespace HPD.Gateway.Yarp;

internal sealed record EffectiveSelection<T>(T? Value, ImmutableArray<GatewayEffectiveContribution> Contributions)
    where T : class;

internal sealed class GatewayEffectiveProjectionBuilder(
    GatewayCandidateReadResult candidate,
    PublicationCandidateIdentity identity)
{
    private const ushort SchemaVersion = 1;
    private readonly GatewayCandidateReadResult _candidate = candidate;
    private readonly PublicationCandidateIdentity _identity = identity;
    private readonly ImmutableArray<GatewayEffectiveRecord>.Builder _records = ImmutableArray.CreateBuilder<GatewayEffectiveRecord>();

    internal GatewayEffectiveSnapshot Build()
    {
        if (_records.Count > GatewayEffectiveBounds.MaximumRecords)
            throw new InvalidOperationException("The effective projection exceeds its record bound.");
        var records = _records
            .OrderBy(static item => item.TargetId, StringComparer.Ordinal)
            .ThenBy(static item => item.Family, StringComparer.Ordinal)
            .ToImmutableArray();
        return new GatewayEffectiveSnapshot(SchemaVersion, _identity.CandidateId, _identity.ContentHash, records, false);
    }

    internal EffectiveSelection<T> Resolve<T>(
        RouteId routeId,
        string family,
        string seam,
        DeclarationReference<T>? root,
        DeclarationReference<T>? local,
        ImmutableArray<DeclarationDefinition<T>> definitions,
        Func<T, ContentHash> hashValue,
        Func<T, GatewayEffectiveContribution?>? hostContribution = null)
        where T : class
    {
        var contributions = ImmutableArray.CreateBuilder<GatewayEffectiveContribution>();
        T? value = null;
        if (root is not null)
        {
            value = ResolveReference(root, definitions, family, "gateway", GatewayContributionSourceKind.RootDefault,
                local is null ? GatewayContributionDisposition.Selected : GatewayContributionDisposition.Overridden,
                0, hashValue, contributions);
        }
        if (local is not null)
        {
            value = ResolveReference(local, definitions, family, $"routes/{routeId.Value}", GatewayContributionSourceKind.Inline,
                GatewayContributionDisposition.Selected, contributions.Count, hashValue, contributions);
        }
        if (value is null) return new EffectiveSelection<T>(null, []);
        var valueHash = hashValue(value);
        GatewayEffectiveContribution? host = null;
        if (hostContribution?.Invoke(value) is { } correlated)
        {
            host = correlated with { DeterministicOrder = contributions.Count };
            contributions.Add(host);
        }
        var effectiveHash = Hash("hpd.gateway/effective-value/v1", family, valueHash.Value, host?.ContentHash.Value);
        AddRecord(routeId, family, GatewayEffectiveComposition.ReplaceMoreSpecific, seam, contributions.ToImmutable(), effectiveHash);
        return new EffectiveSelection<T>(value, contributions.ToImmutable());
    }

    internal void AddTransforms(RouteId routeId, OrderedRequestTransforms? request, OrderedResponseTransforms? response)
    {
        if (request is { Headers.IsEmpty: false })
            AddTransformRecord(routeId, GatewayEffectiveFamilies.RequestHeaderTransforms,
                "RouteConfig.Transforms/request-header", request.Headers.Select(static item => (item.Kind, item.Name, item.Value)));
        if (response is { Headers.IsEmpty: false })
            AddTransformRecord(routeId, GatewayEffectiveFamilies.ResponseHeaderTransforms,
                "RouteConfig.Transforms/response-header", response.Headers.Select(static item => (item.Kind, item.Name, item.Value)));
        if (response is { Trailers.IsEmpty: false })
            AddTransformRecord(routeId, GatewayEffectiveFamilies.ResponseTrailerTransforms,
                "RouteConfig.Transforms/response-trailer", response.Trailers.Select(static item => (item.Kind, item.Name, item.Value)));
    }

    internal GatewayEffectiveContribution HostProfile(string identity, ContentHash hash) => new(
        GatewayContributionSourceKind.HostProfile,
        GatewayContributionDisposition.Correlated,
        identity,
        null,
        0,
        hash);

    internal GatewayEffectiveContribution? OutputCacheProfile(OutputCacheBinding binding)
    {
        if (!_candidate.OutputCacheProfiles.TryGetValue(binding.PolicyName, out var profile)) return null;
        return HostProfile(
            $"host/output-cache/{profile.Name}@{profile.Version}",
            Hash("hpd.gateway/output-cache-profile/v1", profile.Name, profile.Version.ToString(),
                profile.RetainsDefaultSafetyPolicy.ToString(), profile.StoreId, profile.StoreScope.ToString(),
                profile.Expiration.Ticks.ToString(), profile.MaximumBodyBytes.ToString(), profile.StoreCapacityBytes.ToString(),
                string.Join('\n', profile.QueryKeys), string.Join('\n', profile.HeaderNames)));
    }

    internal GatewayEffectiveContribution InspectorProfile(RequestInspectionBinding binding) =>
        HostProfile($"host/inspector/{binding.InspectorName}", Hash("hpd.gateway/inspector/v1", binding.InspectorName));

    internal GatewayEffectiveContribution CredentialCatalog(CredentialDispositionBinding _) =>
        HostProfile("host/protected-credential-catalog", Hash("hpd.gateway/protected-credential-catalog/v1", string.Join('\n', _candidate.ProtectedCredentialHeaders)));

    private void AddTransformRecord(RouteId routeId, string family, string seam, IEnumerable<(HeaderTransformKind Kind, string Name, string? Value)> headers)
    {
        var contributions = headers.Select((header, order) => new GatewayEffectiveContribution(
                GatewayContributionSourceKind.Inline,
                GatewayContributionDisposition.Selected,
                $"routes/{routeId.Value}",
                null,
                order,
                Hash("hpd.gateway/effective-transform-contribution/v1", header.Kind.ToString(), header.Name.ToLowerInvariant(), header.Value)))
            .ToImmutableArray();
        var effectiveHash = Hash(["hpd.gateway/effective-transform/v1", family, .. contributions.Select(static item => item.ContentHash.Value)]);
        AddRecord(routeId, family, GatewayEffectiveComposition.AdditiveOrdered, seam, contributions, effectiveHash);
    }

    private void AddRecord(
        RouteId routeId,
        string family,
        GatewayEffectiveComposition composition,
        string seam,
        ImmutableArray<GatewayEffectiveContribution> contributions,
        ContentHash effectiveHash)
    {
        if (contributions.Length > GatewayEffectiveBounds.MaximumContributionsPerRecord)
            throw new InvalidOperationException("The effective contribution bound was exceeded.");
        _records.Add(new GatewayEffectiveRecord(
            SchemaVersion,
            GatewayEffectiveTargetKind.Route,
            routeId.Value,
            family,
            composition,
            contributions,
            new GatewayNativeProjection("ASP.NET Core/YARP", seam, "Yarp.ReverseProxy/2.3.0"),
            "HPD.Gateway.Yarp",
            "1.0.0",
            GatewayMaterializationDisposition.Materialized,
            effectiveHash,
            []));
    }

    private static T ResolveReference<T>(
        DeclarationReference<T> reference,
        ImmutableArray<DeclarationDefinition<T>> definitions,
        string family,
        string sourceIdentity,
        GatewayContributionSourceKind inlineSourceKind,
        GatewayContributionDisposition disposition,
        int order,
        Func<T, ContentHash> hashValue,
        ImmutableArray<GatewayEffectiveContribution>.Builder contributions)
        where T : class
    {
        if (reference.Inline is { } inline)
        {
            contributions.Add(new GatewayEffectiveContribution(inlineSourceKind, disposition, sourceIdentity, null, order, hashValue(inline)));
            return inline;
        }
        if (reference.Definition is { } id)
        {
            var definition = definitions.FirstOrDefault(item => item.Id == id)
                ?? throw new InvalidOperationException("Accepted definition reference is unresolved.");
            contributions.Add(new GatewayEffectiveContribution(
                GatewayContributionSourceKind.ReusableDefinition,
                disposition,
                $"definitions/{family}/{id.Value}",
                id,
                order,
                hashValue(definition.Specification)));
            return definition.Specification;
        }
        throw new InvalidOperationException("Accepted declaration reference is empty.");
    }

    internal static ContentHash Hash(params string?[] fields)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field ?? "<null>");
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            incremental.AppendData(length);
            incremental.AppendData(bytes);
        }
        return new ContentHash("sha-256", Convert.ToHexStringLower(incremental.GetHashAndReset()));
    }

}
