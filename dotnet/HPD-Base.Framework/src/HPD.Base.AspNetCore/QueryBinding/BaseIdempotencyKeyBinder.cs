using HPD.Base.AspNetCore.Http;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore.QueryBinding;

internal static class BaseIdempotencyKeyBinder
{
    public static string? Bind(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue(BaseHttpHeaders.IdempotencyKey, out var values))
            return null;

        var value = values.FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
