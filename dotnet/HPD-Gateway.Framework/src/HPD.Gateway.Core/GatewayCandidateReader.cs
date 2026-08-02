using System.Collections.Immutable;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;

namespace HPD.Gateway.Core;

public sealed class GatewayCandidateReadResult
{
    internal GatewayCandidateReadResult(
        GatewayConfiguration? configuration,
        GatewayCanonicalDocument? canonicalDocument,
        ImmutableArray<GatewayValidationError> errors,
        ImmutableArray<string> protectedCredentialHeaders = default)
    {
        Configuration = configuration;
        CanonicalDocument = canonicalDocument;
        Errors = errors;
        ProtectedCredentialHeaders = protectedCredentialHeaders;
    }

    public GatewayConfiguration? Configuration { get; }
    public GatewayCanonicalDocument? CanonicalDocument { get; }
    public ImmutableArray<GatewayValidationError> Errors { get; }
    public ImmutableArray<string> ProtectedCredentialHeaders { get; }
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
            ? new GatewayCandidateReadResult(parsed.Configuration, canonical.Document, [], capabilities.ProtectedCredentialHeaders)
            : new GatewayCandidateReadResult(null, null, canonical.Errors);
    }
}
