using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Studio;

public static class HPDAuthStudioEndpointExtensions
{
    /// <summary>Maps the HPD.Auth fresh-authentication entry used by the Studio challenge flow.</summary>
    public static IEndpointRouteBuilder MapHPDAuthStudioFreshAuthentication(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        HPDAuthStudioOptions options = endpoints.ServiceProvider.GetRequiredService<HPDAuthStudioOptions>();
        endpoints.MapGet(options.FreshAuthenticationRoute, (HttpContext context) =>
        {
            string? continuation = context.Request.Query.TryGetValue("continuation", out var values) && values.Count == 1
                ? values[0] : null;
            if (continuation is null || continuation.Length is < 32 or > 4096 ||
                continuation.Any(static value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
                return Results.BadRequest();
            string callback = QueryHelpers.AddQueryString("/base/studio/auth/fresh/callback",
                "continuation", continuation);
            string login = QueryHelpers.AddQueryString(options.ProviderLoginRoute,
                options.ProviderReturnQueryParameter, callback);
            return Results.Redirect(login);
        }).WithName("HPDAuthStudioFreshAuthentication")
          .WithSummary("Re-authenticates the current HPD.Auth session for one protected Studio command.");
        return endpoints;
    }
}
