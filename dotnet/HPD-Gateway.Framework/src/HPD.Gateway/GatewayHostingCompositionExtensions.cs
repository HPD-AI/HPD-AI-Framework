using Microsoft.AspNetCore.Builder;

namespace HPD.Gateway;

public static class GatewayHostingCompositionExtensions
{
    public static WebApplicationBuilder UseHpdGatewayHost(
        this WebApplicationBuilder builder,
        GatewayHostCandidate candidate,
        Action<GatewayCertificateSourceRegistryBuilder> configureCertificates)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WebHost.UseHpdGatewayHost(
            builder.Services,
            candidate,
            configureCertificates);
        return builder;
    }
}
