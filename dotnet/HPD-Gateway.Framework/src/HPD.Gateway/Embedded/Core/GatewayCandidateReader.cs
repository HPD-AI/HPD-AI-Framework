using System.Collections.Immutable;

namespace HPD.Gateway;

public sealed class GatewayCandidateReadResult
{
    internal GatewayCandidateReadResult(
        GatewayConfiguration? configuration,
        GatewayCanonicalDocument? canonicalDocument,
        ImmutableArray<GatewayValidationError> errors,
        ImmutableArray<string> protectedCredentialHeaders = default,
        ImmutableDictionary<string, OutputCacheCapability>? outputCacheProfiles = null,
        ImmutableDictionary<DiscoveryProfileId, DiscoveryProfileCapability>? discoveryProfiles = null,
        ImmutableDictionary<string, UpstreamResilienceCapability>? upstreamResilienceProfiles = null)
    {
        Configuration = configuration;
        CanonicalDocument = canonicalDocument;
        Errors = errors;
        ProtectedCredentialHeaders = protectedCredentialHeaders;
        OutputCacheProfiles = outputCacheProfiles ?? ImmutableDictionary<string, OutputCacheCapability>.Empty;
        DiscoveryProfiles = discoveryProfiles ?? ImmutableDictionary<DiscoveryProfileId, DiscoveryProfileCapability>.Empty;
        UpstreamResilienceProfiles = upstreamResilienceProfiles ?? ImmutableDictionary<string, UpstreamResilienceCapability>.Empty;
    }

    public GatewayConfiguration? Configuration { get; }
    public GatewayCanonicalDocument? CanonicalDocument { get; }
    public ImmutableArray<GatewayValidationError> Errors { get; }
    public ImmutableArray<string> ProtectedCredentialHeaders { get; }
    public ImmutableDictionary<string, OutputCacheCapability> OutputCacheProfiles { get; }
    public ImmutableDictionary<DiscoveryProfileId, DiscoveryProfileCapability> DiscoveryProfiles { get; }
    public ImmutableDictionary<string, UpstreamResilienceCapability> UpstreamResilienceProfiles { get; }
    public bool IsAccepted => Configuration is not null && CanonicalDocument is not null && Errors.IsEmpty;
}

public static class GatewayCandidateReader
{
    public static GatewayCandidateReadResult Read(ReadOnlySpan<byte> utf8Json, HostCapabilitySnapshot capabilities)
    {
        var parsed = GatewayConfigurationParser.Parse(utf8Json);
        if (!parsed.IsParsed) return new GatewayCandidateReadResult(null, null, parsed.Errors);

        var validation = GatewayCandidateValidator.Validate(parsed.Configuration, capabilities);
        if (!validation.IsValid) return new GatewayCandidateReadResult(null, null, validation.Errors);

        var canonical = GatewayConfigurationCanonicalizer.TryCanonicalize(parsed.Configuration);
        return canonical.IsCanonicalized
            ? new GatewayCandidateReadResult(parsed.Configuration, canonical.Document, [], capabilities.ProtectedCredentialHeaders,
                capabilities.OutputCacheProfiles, capabilities.DiscoveryProfiles, capabilities.UpstreamResilienceProfiles)
            : new GatewayCandidateReadResult(null, null, canonical.Errors);
    }
}
