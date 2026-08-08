using System.Security.Claims;
using System.Text;

namespace HPD.Base.AspNetCore;

internal static class BasePrincipalProjectionGuard
{
    internal static string? Single(ClaimsPrincipal principal, IEnumerable<string> types, int maximumBytes, string fact)
    {
        HashSet<string> allowed = types.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] values = principal.Claims.Where(claim => allowed.Contains(claim.Type))
            .Select(claim => Owned(claim.Value, maximumBytes, fact)).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length > 1)
            throw new InvalidOperationException("base.auth.actor.projectionFailed");
        return values.FirstOrDefault();
    }

    internal static string? Single(ClaimsPrincipal principal, string? type, int maximumBytes, string fact) =>
        string.IsNullOrWhiteSpace(type) ? null : Single(principal, [type], maximumBytes, fact);

    internal static string Owned(string value, int maximumBytes, string fact)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > maximumBytes ||
            value.Any(static character => char.IsControl(character)))
            throw new InvalidOperationException("base.auth.actor.projectionFailed");
        return new string(value.AsSpan());
    }

    internal static string[] Multiple(
        ClaimsPrincipal principal,
        IEnumerable<string> types,
        int maximumBytes,
        int maximumCount,
        string fact)
    {
        HashSet<string> allowed = types.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] values = principal.Claims.Where(claim => allowed.Contains(claim.Type))
            .Select(claim => Owned(claim.Value, maximumBytes, fact)).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length > maximumCount)
            throw new InvalidOperationException("base.auth.actor.projectionFailed");
        return values;
    }

    internal static ClaimValue[] CopiedClaims(ClaimsPrincipal principal, IEnumerable<string> types, int maximumCount)
    {
        HashSet<string> allowed = types.ToHashSet(StringComparer.Ordinal);
        ClaimValue[] claims = principal.Claims.Where(claim => allowed.Contains(claim.Type))
            .Select(claim => new ClaimValue
            {
                Type = Owned(claim.Type, 128, "claim type"),
                Value = Owned(claim.Value, 512, "claim value"),
                Issuer = Owned(claim.Issuer, 128, "claim issuer"),
                ValueType = Owned(claim.ValueType, 128, "claim value type")
            })
            .Distinct().ToArray();
        if (claims.Length > maximumCount)
            throw new InvalidOperationException("base.auth.actor.projectionFailed");
        return claims;
    }
}
