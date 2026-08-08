namespace HPD.Base.AspNetCore;

internal static class HPDBaseHttpAuthOptionsValidator
{
    internal static void ValidateAndFreeze(HPDBaseHttpAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxClaims is < 0 or > 64 || options.MaxRoles is < 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(options), "Claim and role limits must be between zero and 64.");
        options.SubjectIdClaimTypes = Copy(options.SubjectIdClaimTypes, nameof(options.SubjectIdClaimTypes));
        options.DisplayNameClaimTypes = Copy(options.DisplayNameClaimTypes, nameof(options.DisplayNameClaimTypes));
        options.RoleClaimTypes = Copy(options.RoleClaimTypes, nameof(options.RoleClaimTypes));
        options.ServicePrincipalClaimTypes = Copy(options.ServicePrincipalClaimTypes, nameof(options.ServicePrincipalClaimTypes));
        options.AdminRoleNames = Copy(options.AdminRoleNames, nameof(options.AdminRoleNames));
        options.CopiedClaimTypes = Copy(options.CopiedClaimTypes, nameof(options.CopiedClaimTypes));
        string[] forbidden = ["token", "secret", "password", "credential", "authorization", "securitystamp", "recovery"];
        if (options.CopiedClaimTypes.Any(type => forbidden.Any(fragment => type.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Copied claim types contain a forbidden credential family.", nameof(options));
    }

    private static string[] Copy(string[]? values, string name)
    {
        if (values is null) throw new ArgumentNullException(name);
        string[] copy = values.Select(value => new string(value.AsSpan())).ToArray();
        if (copy.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) ||
            copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Values must be distinct bounded visible strings.", name);
        return copy;
    }
}
