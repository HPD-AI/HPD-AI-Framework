namespace HPD.Base.AspNetCore;

internal static class EndpointRouteBuilderValidation
{
    internal static string RoutePrefix(string routePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);
        if (!routePrefix.StartsWith("/", StringComparison.Ordinal) || routePrefix.Length > 256 || routePrefix.Any(char.IsControl))
            throw new ArgumentException("Route prefix must be an absolute bounded route pattern.", nameof(routePrefix));
        return routePrefix == "/" ? routePrefix : routePrefix.TrimEnd('/');
    }
}
