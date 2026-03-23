using HPD.Auth.Audit.Middleware;
using Microsoft.AspNetCore.Builder;

namespace HPD.Auth.Audit.Extensions;

/// <summary>
/// Extension methods for registering <see cref="AuthEventObserverMiddleware"/> in the
/// ASP.NET Core pipeline.
/// </summary>
public static class AuthEventObserverExtensions
{
    /// <summary>
    /// Adds the <see cref="AuthEventObserverMiddleware"/> to the pipeline.
    /// Call this after <c>UseRouting</c> and before endpoint mapping.
    /// </summary>
    public static IApplicationBuilder UseAuthEventObserver(this IApplicationBuilder app)
        => app.UseMiddleware<AuthEventObserverMiddleware>();
}
