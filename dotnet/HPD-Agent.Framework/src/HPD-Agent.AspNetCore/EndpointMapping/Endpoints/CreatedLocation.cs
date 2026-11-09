using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

internal static class CreatedLocation
{
    internal static string Resolve(
        LinkGenerator links,
        string endpointName,
        params (string Key, string Value)[] routeValues)
    {
        var values = new RouteValueDictionary();
        foreach (var (key, value) in routeValues)
            values[key] = value;

        return links.GetPathByName(endpointName, values)
            ?? throw new InvalidOperationException(
                $"Unable to resolve created-resource location for endpoint '{endpointName}'.");
    }
}
