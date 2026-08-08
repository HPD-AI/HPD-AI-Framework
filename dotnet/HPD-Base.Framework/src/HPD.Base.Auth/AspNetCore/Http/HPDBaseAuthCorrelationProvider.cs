using HPD.Auth.Core.Audit;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.Auth;

internal sealed class HPDBaseAuthCorrelationProvider(IAuthCorrelationContext correlation)
    : IBaseHttpCorrelationProvider
{
    public string GetCorrelationId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string value = correlation.CorrelationId
            ?? throw new HPDBaseAuthProjectionException("base.auth.correlation.missing", StatusCodes.Status500InternalServerError);
        return new string(value.AsSpan());
    }
}
