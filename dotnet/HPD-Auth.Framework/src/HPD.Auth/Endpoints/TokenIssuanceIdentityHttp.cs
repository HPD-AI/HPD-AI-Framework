using HPD.Auth.Core.Models;
using Microsoft.AspNetCore.Http;

namespace HPD.Auth.Endpoints;

/// <summary>Creates bounded token-issuance identities from an HTTP request.</summary>
public static class TokenIssuanceIdentityHttp
{
    private const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Creates one stable identity for a token-producing HTTP operation.</summary>
    /// <param name="context">Current HTTP context.</param>
    /// <param name="requestScope">Stable Auth flow identifier.</param>
    /// <returns>An identity that is stable across retries carrying the same idempotency key.</returns>
    public static TokenIssuanceIdentity Create(HttpContext context, string requestScope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestScope);

        string? supplied = context.Request.Headers[IdempotencyHeader].Count == 1
            ? context.Request.Headers[IdempotencyHeader][0]
            : null;
        string key = IsCanonicalKey(supplied) ? supplied! : context.TraceIdentifier;
        if (!IsCanonicalKey(key))
            key = Guid.NewGuid().ToString("N");

        return new TokenIssuanceIdentity { RequestScope = requestScope, IdempotencyKey = key };
    }

    private static bool IsCanonicalKey(string? value) => value is { Length: >= 1 and <= 128 }
        && value.All(static character => character is >= '!' and <= '~');
}
