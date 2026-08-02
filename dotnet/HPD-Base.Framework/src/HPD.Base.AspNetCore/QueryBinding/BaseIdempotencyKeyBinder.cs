using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

internal static class BaseIdempotencyKeyBinder
{
    /// <summary>Executes the bind operation.</summary>
    public static string? Bind(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue(BaseHttpHeaders.IdempotencyKey, out var values))
            return null;

        var value = values.FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
