using Microsoft.AspNetCore.Builder;

namespace HPD.Gateway.Core;

internal interface IGatewayEndpointMappingParticipant
{
    bool IsMapped { get; }
    void MarkMapped();
}

internal interface IGatewayApplicationPipelineParticipant
{
    void Configure(IApplicationBuilder application);
}
