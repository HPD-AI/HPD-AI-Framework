namespace HPD.Base.AspNetCore;

internal static class EndpointRouteBuilderValidation
{
    public static void Validate(HPDBaseEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RoutePrefix);
        if (!options.RoutePrefix.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("RoutePrefix must start with '/'.", nameof(options));
    }
}
