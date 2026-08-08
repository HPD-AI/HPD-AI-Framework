using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

internal sealed class DefaultBaseHttpCorrelationProvider(IOptions<HPDBaseAspNetCoreOptions> options)
    : IBaseHttpCorrelationProvider
{
    public string GetCorrelationId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string header = options.Value.RequestContext.CorrelationIdHeaderName;
        if (!context.Request.Headers.TryGetValue(header, out var supplied) || supplied.Count == 0)
            return Copy(context.TraceIdentifier);
        if (supplied.Count != 1 || !Valid(supplied[0]))
            throw new BaseHttpCorrelationException();
        return Copy(supplied[0]!);
    }

    private static bool Valid(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(static character => character is >= '!' and <= '~');

    private static string Copy(string value) => new(value.AsSpan());
}
