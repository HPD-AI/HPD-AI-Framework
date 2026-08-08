namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseEndpointFamilySelectionState
{
    private readonly object _gate = new();
    private readonly List<(BaseReadExposure Exposure, HPDBaseEndpointAudience Audience)> _registeredReads = [];

    internal void SelectRegisteredReads(BaseReadExposure exposure, HPDBaseEndpointAudience audience)
    {
        lock (_gate)
        {
            if (_registeredReads.Contains((exposure, audience)))
                throw new InvalidOperationException("base.http.endpoint.familyDuplicate");
            _registeredReads.Add((exposure, audience));
        }
    }

    internal (BaseReadExposure Exposure, HPDBaseEndpointAudience Audience)[] RegisteredReads()
    {
        lock (_gate) return [.. _registeredReads];
    }
}
