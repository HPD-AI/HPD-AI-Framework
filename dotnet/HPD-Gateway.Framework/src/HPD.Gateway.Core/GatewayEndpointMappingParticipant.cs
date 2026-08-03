namespace HPD.Gateway.Core;

internal interface IGatewayEndpointMappingParticipant
{
    bool IsMapped { get; }
    void MarkMapped();
}
