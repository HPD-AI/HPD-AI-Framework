using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

internal static class BaseIfMatchHeaderBinder
{
    public static RevisionToken? Bind(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue(BaseHttpHeaders.IfMatch, out var values))
            return null;

        var value = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        return new RevisionToken(value);
    }
}
