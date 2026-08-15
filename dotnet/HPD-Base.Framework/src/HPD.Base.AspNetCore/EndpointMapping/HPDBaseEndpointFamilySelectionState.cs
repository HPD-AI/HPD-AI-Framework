namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseEndpointFamilySelectionState
{
    private readonly object _gate = new();
    private readonly List<(BaseReadExposure Exposure, HPDBaseEndpointAudience Audience)> _registeredReads = [];
    private HPDBaseEndpointAudience? _generationAudience;

    internal void SelectGeneration(HPDBaseEndpointAudience audience)
    {
        if (audience is HPDBaseEndpointAudience.Public)
            throw new InvalidOperationException("base.clientGeneration.audienceInvalid");
        lock (_gate)
        {
            if (_generationAudience is not null)
                throw new InvalidOperationException("base.clientGeneration.audienceConflict");
            _generationAudience = audience;
        }
    }

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
