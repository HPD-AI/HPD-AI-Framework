using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;

namespace HPD.Gateway.Core;

public static class GatewayCandidateReader
{
    public static GatewayConfigurationReadResult Read(ReadOnlySpan<byte> utf8Json, HostCapabilitySnapshot capabilities)
    {
        var parsed = GatewayConfigurationParser.Parse(utf8Json);
        if (!parsed.IsParsed)
        {
            return new GatewayConfigurationReadResult { Errors = parsed.Errors };
        }

        var validation = GatewayCandidateValidator.Validate(parsed.Configuration, capabilities);
        if (!validation.IsValid)
        {
            return new GatewayConfigurationReadResult { Errors = validation.Errors };
        }

        var canonical = GatewayConfigurationCanonicalizer.TryCanonicalize(parsed.Configuration);
        return canonical.IsCanonicalized
            ? new GatewayConfigurationReadResult
            {
                Configuration = parsed.Configuration,
                CanonicalDocument = canonical.Document,
                Errors = []
            }
            : new GatewayConfigurationReadResult { Errors = canonical.Errors };
    }
}
