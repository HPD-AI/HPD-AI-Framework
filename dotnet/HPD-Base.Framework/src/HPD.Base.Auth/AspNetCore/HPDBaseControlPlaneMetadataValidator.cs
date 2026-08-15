using HPD.Auth.ControlPlane;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.Auth;

internal sealed record HPDBaseSelectedControlPlaneProfileMetadata(string Profile);

internal sealed class HPDBaseControlPlaneMetadataValidator : IHPDBaseEndpointSecurityMetadataValidator
{
    public void Validate(Endpoint endpoint, HPDBaseEndpointDescriptor descriptor)
    {
        ControlPlaneEndpointMetadata[] profiles = endpoint.Metadata.GetOrderedMetadata<ControlPlaneEndpointMetadata>().ToArray();
        ControlPlaneCapabilityMetadata[] capabilities = endpoint.Metadata.GetOrderedMetadata<ControlPlaneCapabilityMetadata>().ToArray();
        HPDBaseSelectedControlPlaneProfileMetadata[] selected = endpoint.Metadata.GetOrderedMetadata<HPDBaseSelectedControlPlaneProfileMetadata>().ToArray();
        if (descriptor.Audience == HPDBaseEndpointAudience.ControlPlane)
        {
            if (profiles.Length != 1 || capabilities.Length != 1 || selected.Length != 1 ||
                !string.Equals(profiles[0].Profile, selected[0].Profile, StringComparison.Ordinal) ||
                !string.Equals(capabilities[0].Capability, descriptor.Capability, StringComparison.Ordinal))
                throw new InvalidOperationException("base.auth.controlPlane.metadataMismatch");
            return;
        }

        if (profiles.Length != 0 || capabilities.Length != 0 || selected.Length != 0)
            throw new InvalidOperationException("base.auth.controlPlane.metadataMismatch");
    }
}
