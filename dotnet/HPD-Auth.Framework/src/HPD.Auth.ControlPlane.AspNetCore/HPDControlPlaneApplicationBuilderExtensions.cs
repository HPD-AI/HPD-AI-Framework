using System.Security.Cryptography;
using HPD.Auth.Core.Audit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.ControlPlane;

public static class HPDControlPlaneApplicationBuilderExtensions
{
    public static IApplicationBuilder UseHPDControlPlaneCorrelation(
        this IApplicationBuilder application,
        string headerName = "X-Correlation-ID")
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);
        return application.Use(async (context, next) =>
        {
            var supplied = context.Request.Headers[headerName];
            var value = supplied.Count == 1 && IsValid(supplied[0])
                ? supplied[0]!
                : Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            ((ControlPlaneCorrelationContext)context.RequestServices
                .GetRequiredService<IAuthCorrelationContext>()).Initialize(value);
            context.Response.Headers[headerName] = value;
            await next(context);
        });
    }

    private static bool IsValid(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(static character => character is >= '!' and <= '~');
}
