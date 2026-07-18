namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

internal static class RouteValue
{
    public static string Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Uri.UnescapeDataString(value);
    }
}
