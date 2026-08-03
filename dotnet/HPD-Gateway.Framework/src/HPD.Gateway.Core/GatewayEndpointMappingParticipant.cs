using Microsoft.AspNetCore.Builder;

namespace HPD.Gateway.Core;

internal interface IGatewayEndpointMappingParticipant
{
    bool IsMapped { get; }
    void MarkMapped();
}

internal interface IGatewayApplicationPipelineParticipant
{
    int Order { get; }
    void Configure(IApplicationBuilder application);
}
