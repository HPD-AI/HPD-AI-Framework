using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HPD.Auth.ControlPlane;

internal sealed class ControlPlaneAuthorizationResultHandler(
    IProblemDetailsService problemDetails) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        var isControlPlane = context.GetEndpoint()?.Metadata
            .GetMetadata<ControlPlaneEndpointMetadata>() is not null;

        if (!isControlPlane || authorizeResult.Succeeded)
        {
            await _fallback.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
        if (context.Response.HasStarted)
            return;

        var challenged = authorizeResult.Challenged;
        var status = challenged
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;
        var code = challenged
            ? "hpd.auth.authenticationRequired"
            : "hpd.auth.accessDenied";

        context.Response.StatusCode = status;
        context.Response.Headers.Location = default;

        var details = new ProblemDetails
        {
            Type = challenged
                ? "https://hpd.dev/problems/authentication-required"
                : "https://hpd.dev/problems/access-denied",
            Title = challenged ? "Authentication required" : "Access denied",
            Status = status
        };
        details.Extensions["code"] = code;

        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = details
        });
    }
}
